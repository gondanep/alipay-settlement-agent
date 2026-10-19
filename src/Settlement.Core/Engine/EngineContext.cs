using Settlement.Core.Configuration;
using Settlement.Core.Model;

namespace Settlement.Core.Engine;

public sealed class EngineContext(MatchingOptions options, IReadOnlyDictionary<string, InternalTransaction> internalTransactions)
{
    public MatchingOptions Options { get; } = options;
    public IReadOnlyDictionary<string, InternalTransaction> Internal { get; } = internalTransactions;
    public List<ProcessedEntry> Entries { get; } = [];
    public List<Break> Breaks { get; } = [];

    public void AddBreak(BreakType type, ProcessedEntry? entry, decimal? internalAmount, string reason)
    {
        var amount = entry?.Record.Amount;
        Breaks.Add(new Break(
            type,
            SeverityPolicy.For(type, amount ?? internalAmount, Options),
            entry?.Record.TransactionId,
            entry?.CanonicalType,
            amount,
            internalAmount,
            reason,
            entry?.Record.LineNumber));
    }
}

public static class SeverityPolicy
{
    public static Severity For(BreakType type, decimal? amount, MatchingOptions options)
    {
        if (amount is { } a && Math.Abs(a) >= options.HighSeverityAmount)
        {
            return Severity.High;
        }

        return type switch
        {
            BreakType.LateSettlement => Severity.Low,
            BreakType.ControlTotalMismatch or BreakType.RefundExceedsOriginal or BreakType.Duplicate => Severity.High,
            _ => Severity.Medium,
        };
    }
}
