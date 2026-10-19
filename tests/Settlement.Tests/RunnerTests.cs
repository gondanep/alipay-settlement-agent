using Microsoft.Extensions.Logging.Abstractions;
using Settlement.Agent;
using Settlement.Core.Configuration;
using Settlement.Core.Internal;
using Settlement.Sfg;

namespace Settlement.Tests;

public sealed class RunnerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "settlement-runner", Guid.NewGuid().ToString("N"));
    private readonly SettlementOptions _options;

    public RunnerTests()
    {
        _options = TestSupport.LoadOptions();
        _options.Sfg.Mode = SfgMode.Local;
        _options.Sfg.InboundPath = Path.Combine(_root, "inbound");
        _options.Sfg.ProcessedPath = Path.Combine(_root, "processed");
        _options.Sfg.ErrorPath = Path.Combine(_root, "error");
        _options.Sfg.LedgerPath = Path.Combine(_root, "state", "ledger.json");
        _options.Sfg.WorkingPath = Path.Combine(_root, "work");
        _options.Output.ReportsPath = Path.Combine(_root, "reports");
        Directory.CreateDirectory(_options.Sfg.InboundPath);
    }

    private SettlementRunner Runner() => new(_options, () => FileSourceFactory.Create(_options.Sfg),
        new FileInternalTransactionSource(_options.InternalSource), NullLogger<SettlementRunner>.Instance);

    private void Drop(string sample, string? asName = null) =>
        File.Copy(TestSupport.Sample("advice", sample), Path.Combine(_options.Sfg.InboundPath, asName ?? sample));

    [Fact]
    public void Run_once_processes_and_archives_files_from_local_mailbox()
    {
        Drop("ALIPAY_SETTLE_2088000000000001_20261018.csv");
        Drop("APLUS_SETTLE_20261018.csv");

        var results = Runner().RunOnce();

        Assert.All(results, r => Assert.Equal(FileOutcome.Processed, r.Outcome));
        Assert.Empty(Directory.GetFiles(_options.Sfg.InboundPath));
        Assert.Equal(2, Directory.GetFiles(_options.Sfg.ProcessedPath).Length);
        Assert.True(File.Exists(Path.Combine(_options.Output.ReportsPath, "APLUS_SETTLE_20261018", "summary.json")));
    }

    [Fact]
    public void Same_content_delivered_twice_is_skipped_as_duplicate()
    {
        Drop("APLUS_SETTLE_20261018.csv");
        Runner().RunOnce();
        Drop("APLUS_SETTLE_20261018.csv", "APLUS_SETTLE_20261018_resend.csv");

        var result = Assert.Single(Runner().RunOnce());

        Assert.Equal(FileOutcome.Duplicate, result.Outcome);
        Assert.Single(Directory.GetFiles(_options.Sfg.ErrorPath));
    }

    [Fact]
    public void File_without_profile_goes_to_error_folder()
    {
        File.WriteAllText(Path.Combine(_options.Sfg.InboundPath, "UNKNOWN_PROVIDER.csv"), "a,b\n1,2");

        var result = Assert.Single(Runner().RunOnce());

        Assert.Equal(FileOutcome.Failed, result.Outcome);
        Assert.Single(Directory.GetFiles(_options.Sfg.ErrorPath));
    }

    [Fact]
    public void Only_files_matching_pattern_are_picked()
    {
        File.WriteAllText(Path.Combine(_options.Sfg.InboundPath, "notes.txt"), "ignore me");

        Assert.Empty(Runner().RunOnce());
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
