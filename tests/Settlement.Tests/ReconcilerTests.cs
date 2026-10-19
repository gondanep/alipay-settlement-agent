using Settlement.Core.Configuration;
using Settlement.Core.Engine;
using Settlement.Core.Model;
using Settlement.Core.Parsing;
using Settlement.Core.Reconciliation;

namespace Settlement.Tests;

public class ReconcilerTests
{
    private static (EngineContext Context, List<ProcessedEntry> Matched) Run(string[] rows, params InternalTransaction[] internalTxns)
    {
        var profile = TestSupport.SimpleProfile();
        var parsed = new AdviceParser().Parse([TestSupport.Header, .. rows], profile);
        var context = new EngineContext(new MatchingOptions(), internalTxns.ToDictionary(t => t.TransactionId, StringComparer.OrdinalIgnoreCase));
        new TransactionTypeEngine().Process(parsed.Records, profile, context);
        return (context, new Reconciler().Reconcile(context));
    }

    private const string Settled = "2026-10-17 10:00:00,2026-10-18 09:00:00";

    [Fact]
    public void Perfect_match_has_no_breaks()
    {
        var (ctx, matched) = Run([$"P1,,P,100,0,USD,{Settled}", $"R1,P1,R,10,0,USD,{Settled}"],
            TestSupport.Txn("P1", "Payment", 100), TestSupport.Txn("R1", "Refund", 10, orig: "P1"));

        Assert.Empty(ctx.Breaks);
        Assert.Equal(2, matched.Count);
    }

    [Theory]
    [InlineData("P1,,P,100,0,USD", "Payment", 90, "USD", "SUCCESS", BreakType.AmountMismatch)]
    [InlineData("P1,,P,100,0,EUR", "Payment", 100, "USD", "SUCCESS", BreakType.CurrencyMismatch)]
    [InlineData("P1,,P,100,0,USD", "Payment", 100, "USD", "FAILED", BreakType.StatusMismatch)]
    [InlineData("P1,,P,100,0,USD", "Refund", 100, "USD", "SUCCESS", BreakType.TypeMismatch)]
    public void Mismatches_are_classified(string row, string type, decimal amount, string currency, string status, BreakType expected)
    {
        var (ctx, matched) = Run([$"{row},{Settled}"],
            new InternalTransaction("P1", type, amount, currency, status, DateTimeOffset.Parse("2026-10-17T10:00:00Z"), null));

        Assert.Equal(expected, Assert.Single(ctx.Breaks).Type);
        Assert.Empty(matched);
    }

    [Fact]
    public void Amount_within_tolerance_matches()
    {
        var (ctx, matched) = Run([$"P1,,P,100.00,0,USD,{Settled}"], TestSupport.Txn("P1", "Payment", 100.01m));

        Assert.Empty(ctx.Breaks);
        Assert.Single(matched);
    }

    [Fact]
    public void Advice_record_unknown_to_our_system_is_missing_in_internal()
    {
        var (ctx, _) = Run([$"P1,,P,100,0,USD,{Settled}"]);

        Assert.Equal(BreakType.MissingInInternal, Assert.Single(ctx.Breaks).Type);
    }

    [Fact]
    public void Old_settled_internal_transaction_absent_from_advice_is_missing_in_advice()
    {
        var (ctx, _) = Run([$"P1,,P,100,0,USD,{Settled}"],
            TestSupport.Txn("P1", "Payment", 100),
            TestSupport.Txn("P_OLD", "Payment", 70, time: "2026-10-10 10:00:00"),
            TestSupport.Txn("P_RECENT", "Payment", 70, time: "2026-10-17 23:00:00"),
            TestSupport.Txn("P_FAILED", "Payment", 70, status: "FAILED", time: "2026-10-10 10:00:00"));

        var b = Assert.Single(ctx.Breaks);
        Assert.Equal((BreakType.MissingInAdvice, "P_OLD"), (b.Type, b.TransactionId));
    }

    [Fact]
    public void Settlement_outside_window_is_low_severity_late_settlement_but_still_matched()
    {
        var (ctx, matched) = Run(["P1,,P,100,0,USD,2026-10-14 10:00:00,2026-10-18 09:00:00"],
            TestSupport.Txn("P1", "Payment", 100, time: "2026-10-14 10:00:00"));

        var b = Assert.Single(ctx.Breaks);
        Assert.Equal((BreakType.LateSettlement, Severity.Low), (b.Type, b.Severity));
        Assert.Single(matched);
    }

    [Fact]
    public void High_value_break_is_high_severity()
    {
        var (ctx, _) = Run([$"P1,,P,25000,0,USD,{Settled}"]);

        Assert.Equal(Severity.High, Assert.Single(ctx.Breaks).Severity);
    }

    [Fact]
    public void Fee_and_adjustment_rows_are_not_matched_against_internal()
    {
        var (ctx, matched) = Run([$"F1,,F,15,0,USD,{Settled}", $"A1,,A,-5,0,USD,{Settled}"]);

        Assert.Empty(ctx.Breaks);
        Assert.Empty(matched);
    }
}
