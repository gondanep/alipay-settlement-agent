using Settlement.Core.Configuration;
using Settlement.Core.Internal;
using Settlement.Core.Model;
using Settlement.Core.Pipeline;
using Settlement.Core.Reporting;

namespace Settlement.Tests;

public class PipelineTests
{
    private static SettlementRunResult RunSample(string file)
    {
        var options = TestSupport.LoadOptions();
        var internalTxns = new FileInternalTransactionSource(options.InternalSource).Load();
        using var stream = File.OpenRead(TestSupport.Sample("advice", file));
        return new SettlementPipeline(options).Process(file, stream, internalTxns);
    }

    [Fact]
    public void Shipped_config_is_valid()
    {
        Assert.Empty(ConfigValidator.Validate(TestSupport.LoadOptions()));
    }

    [Fact]
    public void Alipay_sample_produces_each_seeded_break_exactly_once()
    {
        var result = RunSample("ALIPAY_SETTLE_2088000000000001_20261018.csv");

        var breaks = result.Breaks.Select(b => (b.Type, b.TransactionId)).OrderBy(b => b.Type).ThenBy(b => b.TransactionId).ToList();
        Assert.Equal(
            new (BreakType, string?)[]
            {
                (BreakType.InvalidRecord, null),
                (BreakType.UnknownTransactionType, "X7001"),
                (BreakType.Duplicate, "P1002"),
                (BreakType.MissingInInternal, "P1004"),
                (BreakType.MissingInAdvice, "P0950"),
                (BreakType.AmountMismatch, "P1003"),
                (BreakType.StatusMismatch, "P1005"),
                (BreakType.RefundExceedsOriginal, "R2002"),
                (BreakType.LateSettlement, "CB4001"),
            }.OrderBy(b => b.Item1).ThenBy(b => b.Item2),
            breaks);
    }

    [Fact]
    public void Alipay_sample_totals_are_consistent()
    {
        var s = RunSample("ALIPAY_SETTLE_2088000000000001_20261018.csv").Summary;

        Assert.Equal(("alipay-global", 14, 1), (s.Profile, s.Records, s.Rejected));
        Assert.Equal(s.Totals.Sum(t => t.Net), s.NetSettlement);
        var payment = s.Totals.Single(t => t.Type == TransactionTypes.Payment);
        Assert.Equal((6, 12530.00m, 225.54m), (payment.Count, payment.Gross, payment.Fee));
        Assert.Equal(-80.00m, s.Totals.Single(t => t.Type == TransactionTypes.Refund).Gross);
        Assert.Equal(12040.90m, s.NetSettlement);
        Assert.Equal(7, s.Matched);
        Assert.Equal(10, s.Matchable);
    }

    [Fact]
    public void Alipay_plus_sample_reconciles_fully_with_config_only_tax_type()
    {
        var result = RunSample("APLUS_SETTLE_20261018.csv");

        Assert.Equal("alipay-plus", result.Summary.Profile);
        Assert.Empty(result.Breaks.Where(b => b.Type != BreakType.MissingInAdvice));
        Assert.Equal(1m, result.Summary.MatchRate);
        Assert.Equal(-1.20m, result.Summary.Totals.Single(t => t.Type == "WithholdingTax").Net);
    }

    [Fact]
    public void Control_total_mismatch_is_reported()
    {
        var options = TestSupport.LoadOptions();
        var lines = File.ReadAllLines(TestSupport.Sample("advice", "ALIPAY_SETTLE_2088000000000001_20261018.csv"));
        lines[^1] = "TOTAL,16,99.00";
        var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(string.Join('\n', lines)));

        var result = new SettlementPipeline(options).Process("ALIPAY_SETTLE_X.csv", stream, []);

        Assert.Equal(2, result.Breaks.Count(b => b.Type == BreakType.ControlTotalMismatch));
    }

    [Fact]
    public void File_without_matching_profile_is_rejected()
    {
        Assert.Throws<NoMatchingProfileException>(() =>
            new SettlementPipeline(TestSupport.LoadOptions()).Process("OTHER_PROVIDER.csv", new MemoryStream(), []));
    }

    [Fact]
    public void Reports_are_written()
    {
        var result = RunSample("ALIPAY_SETTLE_2088000000000001_20261018.csv");
        var root = Path.Combine(Path.GetTempPath(), "settlement-tests", Guid.NewGuid().ToString("N"));

        var dir = new ReportWriter().Write(result, root);

        foreach (var name in new[] { "summary.json", "breaks.csv", "entries.csv", "rejected.csv" })
        {
            Assert.True(File.Exists(Path.Combine(dir, name)), name);
        }

        Assert.Equal(result.Breaks.Count + 1, File.ReadAllLines(Path.Combine(dir, "breaks.csv")).Length);
        Assert.Contains("RmbAmount", File.ReadLines(Path.Combine(dir, "entries.csv")).First());
        Directory.Delete(root, recursive: true);
    }
}
