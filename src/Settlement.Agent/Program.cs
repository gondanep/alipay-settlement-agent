using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Settlement.Core.Configuration;
using Settlement.Core.Internal;
using Settlement.Sfg;

namespace Settlement.Agent;

public static class Program
{
    public const string Usage = """
        Alipay settlement agent

        Usage: Settlement.Agent <command> [options]

        Commands:
          run-once                 Process all pending advice files in the SFG inbound mailbox, then exit
          watch                    Poll the SFG mailbox every Settlement:Sfg:PollIntervalSeconds
          process --file <path>    Settle a single local advice file (no SFG, no archiving)
          validate-config          Check profiles, field mappings and transaction type rules

        Options:
          --config <path>          Extra JSON config file layered over appsettings.json
          --fail-on-breaks         Exit with code 2 when any break is found
        Any setting can be overridden with --Settlement:Section:Key=value or SETTLEMENT__SECTION__KEY env vars.
        """;

    public static int Main(string[] args)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
        {
            Console.WriteLine(Usage);
            return args.Length == 0 ? 1 : 0;
        }

        var command = args[0];
        var rest = args.Skip(1).ToList();
        var failOnBreaks = rest.Remove("--fail-on-breaks");
        var file = TakeOption(rest, "--file");
        var extraConfig = TakeOption(rest, "--config");

        var builder = Host.CreateApplicationBuilder(rest.ToArray());
        builder.Configuration.Sources.Clear();
        builder.Configuration
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true)
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: true);
        if (extraConfig is not null)
        {
            builder.Configuration.AddJsonFile(Path.GetFullPath(extraConfig), optional: false);
        }

        builder.Configuration.AddEnvironmentVariables().AddCommandLine(rest.ToArray());

        var options = builder.Configuration.GetSection(SettlementOptions.SectionName).Get<SettlementOptions>() ?? new SettlementOptions();
        builder.Services.AddSingleton(options);
        builder.Services.AddSingleton<IInternalTransactionSource>(new FileInternalTransactionSource(options.InternalSource));
        builder.Services.AddSingleton<Func<IFileSource>>(() => FileSourceFactory.Create(options.Sfg));
        builder.Services.AddSingleton<SettlementRunner>();
        if (command == "watch")
        {
            builder.Services.AddHostedService<SettlementWorker>();
        }

        var errors = ConfigValidator.Validate(options);
        if (command == "validate-config" || errors.Count > 0)
        {
            foreach (var error in errors)
            {
                Console.Error.WriteLine($"CONFIG ERROR: {error}");
            }

            if (errors.Count == 0)
            {
                Console.WriteLine($"Configuration OK: {options.Profiles.Count} profile(s): {string.Join(", ", options.Profiles.Keys)}");
            }

            return errors.Count == 0 ? 0 : 1;
        }

        using var host = builder.Build();
        var runner = host.Services.GetRequiredService<SettlementRunner>();
        var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Settlement.Agent");

        switch (command)
        {
            case "watch":
                host.Run();
                return 0;
            case "run-once":
                {
                    var results = runner.RunOnce();
                    Print(results);
                    if (results.Any(r => r.Outcome == FileOutcome.Failed))
                    {
                        return 3;
                    }

                    return failOnBreaks && results.Any(r => r.Summary?.BreaksByType.Count > 0) ? 2 : 0;
                }

            case "process":
                {
                    if (file is null)
                    {
                        logger.LogError("process needs --file <path>");
                        return 1;
                    }

                    var result = runner.ProcessLocalFile(file);
                    Print([result]);
                    return failOnBreaks && result.Summary?.BreaksByType.Count > 0 ? 2 : 0;
                }

            default:
                Console.Error.WriteLine($"Unknown command '{command}'.");
                Console.WriteLine(Usage);
                return 1;
        }
    }

    private static string? TakeOption(List<string> args, string name)
    {
        var i = args.IndexOf(name);
        if (i < 0 || i + 1 >= args.Count)
        {
            return null;
        }

        var value = args[i + 1];
        args.RemoveRange(i, 2);
        return value;
    }

    private static void Print(IEnumerable<FileRunResult> results) =>
        Console.WriteLine(JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true, Converters = { new JsonStringEnumConverter() } }));
}
