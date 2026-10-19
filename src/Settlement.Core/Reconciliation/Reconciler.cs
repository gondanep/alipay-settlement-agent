using Settlement.Core.Engine;
using Settlement.Core.Model;

namespace Settlement.Core.Reconciliation;

public sealed class Reconciler
{
    public List<ProcessedEntry> Reconcile(EngineContext context)
    {
        var options = context.Options;
        var matched = new List<ProcessedEntry>();

        foreach (var entry in context.Entries)
        {
            if (!entry.Valid || !entry.MatchInternal)
            {
                continue;
            }

            if (!context.Internal.TryGetValue(entry.Record.TransactionId, out var txn))
            {
                context.AddBreak(BreakType.MissingInInternal, entry, null,
                    $"{entry.CanonicalType} {entry.Record.TransactionId} is in the advice but not in our system");
                continue;
            }

            var before = context.Breaks.Count;
            if (!string.Equals(txn.Type, entry.CanonicalType, StringComparison.OrdinalIgnoreCase))
            {
                context.AddBreak(BreakType.TypeMismatch, entry, txn.Amount, $"Advice type {entry.CanonicalType}, internal type {txn.Type}");
            }

            if (!string.Equals(txn.Currency, entry.Record.Currency, StringComparison.OrdinalIgnoreCase))
            {
                context.AddBreak(BreakType.CurrencyMismatch, entry, txn.Amount, $"Advice currency {entry.Record.Currency}, internal currency {txn.Currency}");
            }

            if (Math.Abs(Math.Abs(txn.Amount) - Math.Abs(entry.Record.Amount)) > options.AmountTolerance)
            {
                context.AddBreak(BreakType.AmountMismatch, entry, txn.Amount, $"Advice amount {entry.Record.Amount}, internal amount {txn.Amount}");
            }

            if (!options.SettledInternalStatuses.Contains(txn.Status, StringComparer.OrdinalIgnoreCase))
            {
                context.AddBreak(BreakType.StatusMismatch, entry, txn.Amount, $"Settled by provider but internal status is {txn.Status}");
            }

            var hardBreak = context.Breaks.Count > before;

            var txnTime = txn.TransactionTime ?? entry.Record.TransactionTime;
            if (txnTime is { } t && entry.Record.SettlementTime is { } s && (s - t).TotalDays > options.SettlementWindowDays)
            {
                context.AddBreak(BreakType.LateSettlement, entry, txn.Amount,
                    $"Settled {(s - t).TotalDays:F1} days after transaction (window {options.SettlementWindowDays} days)");
            }

            if (!hardBreak)
            {
                matched.Add(entry);
            }
        }

        if (options.ReportMissingInAdvice)
        {
            AddMissingInAdvice(context);
        }

        return matched;
    }

    private static void AddMissingInAdvice(EngineContext context)
    {
        var settlementDate = context.Entries.Select(e => e.Record.SettlementTime).Where(t => t is not null).Max();
        if (settlementDate is null)
        {
            return;
        }

        var cutoff = settlementDate.Value.AddDays(-context.Options.SettlementWindowDays);
        var adviceIds = context.Entries.Select(e => e.Record.TransactionId)
            .Concat(context.Entries.Select(e => e.Record.OriginalTransactionId).OfType<string>())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var txn in context.Internal.Values)
        {
            if (adviceIds.Contains(txn.TransactionId) ||
                !context.Options.SettledInternalStatuses.Contains(txn.Status, StringComparer.OrdinalIgnoreCase) ||
                txn.TransactionTime is null || txn.TransactionTime > cutoff)
            {
                continue;
            }

            context.Breaks.Add(new Break(
                BreakType.MissingInAdvice,
                SeverityPolicy.For(BreakType.MissingInAdvice, txn.Amount, context.Options),
                txn.TransactionId,
                txn.Type,
                null,
                txn.Amount,
                $"{txn.Type} {txn.TransactionId} is settled in our system but not in the advice (older than {context.Options.SettlementWindowDays}-day window)"));
        }
    }
}
