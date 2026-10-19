using Settlement.Core.Configuration;
using Settlement.Core.Model;

namespace Settlement.Core.Engine;

public abstract class TransactionTypeHandler
{
    public abstract string Type { get; }
    public abstract Direction Direction { get; }
    public virtual bool RequiresOriginal => false;
    public virtual bool MatchInternal => true;

    /// <summary>Type-specific validation, run after all entries in the file have been built.</summary>
    public virtual void Validate(ProcessedEntry entry, EngineContext context)
    {
    }

    protected static decimal OriginalAmount(ProcessedEntry entry, EngineContext context, out bool found)
    {
        var originalId = entry.Record.OriginalTransactionId!;
        var inAdvice = context.Entries.FirstOrDefault(e =>
            e.CanonicalType == TransactionTypes.Payment &&
            string.Equals(e.Record.TransactionId, originalId, StringComparison.OrdinalIgnoreCase));
        if (inAdvice is not null)
        {
            found = true;
            return Math.Abs(inAdvice.Record.Amount);
        }

        if (context.Internal.TryGetValue(originalId, out var internalTxn))
        {
            found = true;
            return Math.Abs(internalTxn.Amount);
        }

        found = false;
        return 0m;
    }
}

public sealed class PaymentHandler : TransactionTypeHandler
{
    public override string Type => TransactionTypes.Payment;
    public override Direction Direction => Direction.Credit;
}

public sealed class RefundHandler : TransactionTypeHandler
{
    public override string Type => TransactionTypes.Refund;
    public override Direction Direction => Direction.Debit;
    public override bool RequiresOriginal => true;

    public override void Validate(ProcessedEntry entry, EngineContext context)
    {
        var originalId = entry.Record.OriginalTransactionId;
        if (string.IsNullOrEmpty(originalId))
        {
            return;
        }

        var original = OriginalAmount(entry, context, out var found);
        if (!found)
        {
            return;
        }

        var refundsInAdvice = context.Entries
            .Where(e => e.CanonicalType == TransactionTypes.Refund && e.Valid &&
                        string.Equals(e.Record.OriginalTransactionId, originalId, StringComparison.OrdinalIgnoreCase))
            .ToList();

        // Only evaluate once per original, on the last refund line for it.
        if (!ReferenceEquals(refundsInAdvice.LastOrDefault(), entry))
        {
            return;
        }

        var adviceIds = refundsInAdvice.Select(e => e.Record.TransactionId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var priorInternalRefunds = context.Internal.Values
            .Where(t => string.Equals(t.Type, TransactionTypes.Refund, StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(t.OriginalTransactionId, originalId, StringComparison.OrdinalIgnoreCase) &&
                        !adviceIds.Contains(t.TransactionId) &&
                        context.Options.SettledInternalStatuses.Contains(t.Status, StringComparer.OrdinalIgnoreCase))
            .Sum(t => Math.Abs(t.Amount));
        var totalRefunded = refundsInAdvice.Sum(e => Math.Abs(e.Record.Amount)) + priorInternalRefunds;

        if (totalRefunded > original + context.Options.AmountTolerance)
        {
            context.AddBreak(BreakType.RefundExceedsOriginal, entry, null,
                $"Total refunded {totalRefunded} exceeds original payment {originalId} amount {original}");
        }
    }
}

public sealed class CancelHandler : TransactionTypeHandler
{
    public override string Type => TransactionTypes.Cancel;
    public override Direction Direction => Direction.Debit;
    public override bool RequiresOriginal => true;

    public override void Validate(ProcessedEntry entry, EngineContext context)
    {
        if (string.IsNullOrEmpty(entry.Record.OriginalTransactionId))
        {
            return;
        }

        var original = OriginalAmount(entry, context, out var found);
        if (found && Math.Abs(Math.Abs(entry.Record.Amount) - original) > context.Options.AmountTolerance)
        {
            context.AddBreak(BreakType.CancelAmountMismatch, entry, original,
                $"Cancel amount {Math.Abs(entry.Record.Amount)} must equal original amount {original}");
        }
    }
}

public sealed class ChargebackHandler : TransactionTypeHandler
{
    public override string Type => TransactionTypes.Chargeback;
    public override Direction Direction => Direction.Debit;
    public override bool RequiresOriginal => true;
}

public sealed class ChargebackReversalHandler : TransactionTypeHandler
{
    public override string Type => TransactionTypes.ChargebackReversal;
    public override Direction Direction => Direction.Credit;
    public override bool RequiresOriginal => true;
}

public sealed class FeeHandler : TransactionTypeHandler
{
    public override string Type => TransactionTypes.Fee;
    public override Direction Direction => Direction.Debit;
    public override bool MatchInternal => false;
}

public sealed class AdjustmentHandler : TransactionTypeHandler
{
    public override string Type => TransactionTypes.Adjustment;
    public override Direction Direction => Direction.Signed;
    public override bool MatchInternal => false;
}

/// <summary>Handler for provider types defined purely in configuration.</summary>
public sealed class GenericHandler(string type, Direction direction, bool requiresOriginal, bool matchInternal) : TransactionTypeHandler
{
    public override string Type => type;
    public override Direction Direction => direction;
    public override bool RequiresOriginal => requiresOriginal;
    public override bool MatchInternal => matchInternal;
}
