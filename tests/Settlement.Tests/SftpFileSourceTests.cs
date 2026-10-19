using Renci.SshNet;
using Settlement.Core.Configuration;
using Settlement.Sfg;

namespace Settlement.Tests;

/// <summary>
/// Runs against a real SFTP server standing in for SFG. Set SFTP_TEST_HOST/PORT/USER/PASSWORD
/// (CI starts atmoz/sftp); skipped otherwise.
/// </summary>
public class SftpFileSourceTests
{
    private static SfgOptions? Options()
    {
        var host = Environment.GetEnvironmentVariable("SFTP_TEST_HOST");
        if (string.IsNullOrEmpty(host))
        {
            return null;
        }

        var run = Guid.NewGuid().ToString("N")[..8];
        return new SfgOptions
        {
            Mode = SfgMode.Sftp,
            Host = host,
            Port = int.Parse(Environment.GetEnvironmentVariable("SFTP_TEST_PORT") ?? "22"),
            Username = Environment.GetEnvironmentVariable("SFTP_TEST_USER") ?? "sfg",
            Password = Environment.GetEnvironmentVariable("SFTP_TEST_PASSWORD"),
            InboundPath = $"/inbound/{run}",
            ProcessedPath = $"/inbound/{run}-processed",
            ErrorPath = $"/inbound/{run}-error",
            FilePattern = "ALIPAY_*.csv",
        };
    }

    [SkippableFact]
    public void Lists_downloads_and_archives_files_on_sftp_mailbox()
    {
        var options = Options();
        Skip.If(options is null, "SFTP_TEST_HOST not set");

        var content = File.ReadAllBytes(TestSupport.Sample("advice", "ALIPAY_SETTLE_2088000000000001_20261018.csv"));
        using (var client = new SftpClient(options!.Host, options.Port, options.Username, options.Password))
        {
            client.Connect();
            client.CreateDirectory(options.InboundPath);
            client.WriteAllBytes($"{options.InboundPath}/ALIPAY_SETTLE_1.csv", content);
            client.WriteAllBytes($"{options.InboundPath}/ALIPAY_SETTLE_2.csv", content);
            client.WriteAllText($"{options.InboundPath}/readme.txt", "not an advice file");
        }

        using var source = new SftpFileSource(options);
        var pending = source.ListPending();
        Assert.Equal(["ALIPAY_SETTLE_1.csv", "ALIPAY_SETTLE_2.csv"], pending.Select(f => f.Name));
        Assert.Equal(content, source.Download(pending[0]));

        source.MoveToProcessed(pending[0]);
        source.MoveToError(pending[1]);

        Assert.Empty(source.ListPending());
        using var check = new SftpClient(options.Host, options.Port, options.Username, options.Password);
        check.Connect();
        Assert.True(check.Exists($"{options.ProcessedPath}/ALIPAY_SETTLE_1.csv"));
        Assert.True(check.Exists($"{options.ErrorPath}/ALIPAY_SETTLE_2.csv"));
    }
}
