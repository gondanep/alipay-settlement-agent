using Settlement.Core.Configuration;
using Settlement.Core.Engine;
using Settlement.Core.Model;
using Settlement.Core.Parsing;

namespace Settlement.Tests;

public class TransactionTypeEngineTests
{
    private static EngineContext Run(string[] rows, AdviceProfile? profile = null, params InternalTransaction[] internalTxns)
    {
        profile ??= TestSupport.SimpleProfile();
        var parsed = new AdviceParser().Parse([TestSupport.Header, .. rows], profile);
        var context = new EngineContext(new MatchingOptions(), internalTxns.ToDictionary(t => t.TransactionId, StringComparer.OrdinalIgnoreCase));
        new TransactionTypeEngine().Process(parsed.Records, profile, context);
        return context;
    }

    [Theory]
    [InlineData("P", null, 100, TransactionTypes.Payment, 100)]
    [InlineData("R", "P0", 30, TransactionTypes.Refund, -30)]
    [InlineData("C", "P0", 100, TransactionTypes.Cancel, -100)]
    [InlineData("CB", "P0", 40, TransactionTypes.Chargeback, -40)]
    [InlineData("CBR", "P0", 40, TransactionTypes.ChargebackReversal, 40)]
    [InlineData("F", null, 15, TransactionTypes.Fee, -15)]
    [InlineData("A", null, -5, TransactionTypes.Adjustment, -5)]
    [InlineData("A", null, 7, TransactionTypes.Adjustment, 7)]
    public void Applies_sign_per_transaction_type(string code, string? orig, decimal amount, string expectedType, decimal expectedGross)
    {
        var ctx = Run([$"X1,{orig},{code},{amount},0,USD,,"], null, TestSupport.Txn("P0", TransactionTypes.Payment, 100));

        var e = Assert.Single(ctx.Entries);
        Assert.Equal(expectedType, e.CanonicalType);
        Assert.Equal(expectedGross, e.SignedGross);
        Assert.Empty(ctx.Breaks);
    }

    [Fact]
    public void Net_is_payin_minus_refunds_minus_fees()
    {
        var ctx = Run(["P1,,P,100,1.80,USD,,", "R1,P1,R,30,-0.54,USD,,", "R2,P1,R,20,-0.36,USD,,"]);

        Assert.Equal(100 - 1.80m - 30 + 0.54m - 20 + 0.36m, ctx.Entries.Sum(e => e.Net));
        Assert.Empty(ctx.Breaks);
    }

    [Fact]
    public void Refund_exceeding_original_including_prior_internal_refunds_is_a_break()
    {
        var ctx = Run(["R2,P1,R,50,0,USD,,"], null,
            TestSupport.Txn("P1", TransactionTypes.Payment, 100),
            TestSupport.Txn("R1", TransactionTypes.Refund, 60, orig: "P1"));

        var b = Assert.Single(ctx.Breaks);
        Assert.Equal(BreakType.RefundExceedsOriginal, b.Type);
        Assert.Equal(Severity.High, b.Severity);
    }

    [Fact]
    public void Refund_without_original_reference_is_invalid()
    {
        var ctx = Run(["R1,,R,10,0,USD,,"]);

        Assert.Equal(BreakType.OriginalNotFound, Assert.Single(ctx.Breaks).Type);
        Assert.False(ctx.Entries[0].Valid);
    }

    [Fact]
    public void Refund_with_unknown_original_is_a_break()
    {
        var ctx = Run(["R1,P404,R,10,0,USD,,"]);

        Assert.Contains("P404", Assert.Single(ctx.Breaks, b => b.Type == BreakType.OriginalNotFound).Reason);
    }

    [Fact]
    public void Partial_cancel_is_a_break()
    {
        var ctx = Run(["C1,P1,C,60,0,USD,,"], null, TestSupport.Txn("P1", TransactionTypes.Payment, 100));

        Assert.Equal(BreakType.CancelAmountMismatch, Assert.Single(ctx.Breaks).Type);
    }

    [Fact]
    public void Unknown_type_code_is_a_break()
    {
        var ctx = Run(["Z1,,ZZ,10,0,USD,,"]);

        Assert.Equal(BreakType.UnknownTransactionType, Assert.Single(ctx.Breaks).Type);
    }

    [Fact]
    public void Duplicate_lines_are_flagged_and_excluded()
    {
        var ctx = Run(["P1,,P,10,0,USD,,", "P1,,P,10,0,USD,,"]);

        Assert.Equal(BreakType.Duplicate, Assert.Single(ctx.Breaks).Type);
        Assert.Single(ctx.Entries, e => e.Valid);
    }

    [Fact]
    public void New_type_can_be_onboarded_by_configuration_only()
    {
        var profile = TestSupport.SimpleProfile();
        profile.TransactionTypes["TAX"] = new TransactionTypeRule { Type = "WithholdingTax", Direction = Direction.Debit, MatchInternal = false };

        var e = Assert.Single(Run(["T1,,TAX,1.20,0,USD,,"], profile).Entries);

        Assert.Equal(("WithholdingTax", -1.20m, false), (e.CanonicalType, e.SignedGross, e.MatchInternal));
    }

    [Fact]
    public void Rule_can_override_handler_defaults()
    {
        var profile = TestSupport.SimpleProfile();
        profile.TransactionTypes["RV"] = new TransactionTypeRule { Type = TransactionTypes.Refund, RequiresOriginal = false };

        var ctx = Run(["RV1,,RV,10,0,USD,,"], profile);

        Assert.Empty(ctx.Breaks);
        Assert.Equal(-10m, ctx.Entries[0].SignedGross);
    }
}
