using Settlement.Core.Configuration;
using Settlement.Core.Model;

namespace Settlement.Core.Engine;

public sealed class TransactionTypeEngine
{
    private readonly Dictionary<string, TransactionTypeHandler> _handlers;

    public TransactionTypeEngine(IEnumerable<TransactionTypeHandler>? handlers = null)
    {
        _handlers = (handlers ?? DefaultHandlers()).ToDictionary(h => h.Type, StringComparer.OrdinalIgnoreCase);
    }

    public static IEnumerable<TransactionTypeHandler> DefaultHandlers() =>
    [
        new PaymentHandler(),
        new RefundHandler(),
        new CancelHandler(),
        new ChargebackHandler(),
        new ChargebackReversalHandler(),
        new FeeHandler(),
        new AdjustmentHandler(),
    ];

    public IReadOnlyCollection<string> KnownTypes => _handlers.Keys;

    public TransactionTypeHandler? Resolve(TransactionTypeRule rule)
    {
        if (_handlers.TryGetValue(rule.Type, out var handler))
        {
            if (rule.Direction is null && rule.RequiresOriginal is null && rule.MatchInternal is null)
            {
                return handler;
            }

            return new GenericHandler(handler.Type, rule.Direction ?? handler.Direction,
                rule.RequiresOriginal ?? handler.RequiresOriginal, rule.MatchInternal ?? handler.MatchInternal);
        }

        return rule.Direction is { } direction
            ? new GenericHandler(rule.Type, direction, rule.RequiresOriginal ?? false, rule.MatchInternal ?? true)
            : null;
    }

    public void Process(IEnumerable<AdviceRecord> records, AdviceProfile profile, EngineContext context)
    {
        var resolved = new List<(ProcessedEntry Entry, TransactionTypeHandler Handler)>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var record in records)
        {
            var handler = profile.TransactionTypes.TryGetValue(record.TypeCode, out var rule) ? Resolve(rule) : null;
            if (handler is null)
            {
                var unknown = new ProcessedEntry { Record = record, CanonicalType = record.TypeCode, Valid = false };
                context.Entries.Add(unknown);
                context.AddBreak(BreakType.UnknownTransactionType, unknown, null,
                    $"Transaction type code '{record.TypeCode}' is not configured for provider {profile.Provider}");
                continue;
            }

            var gross = Sign(record.GrossSettlement, handler.Direction);
            var entry = new ProcessedEntry
            {
                Record = record,
                CanonicalType = handler.Type,
                SignedGross = gross,
                SignedFee = record.Fee,
                MatchInternal = handler.MatchInternal,
            };
            context.Entries.Add(entry);
            if (!seen.Add($"{handler.Type}|{record.TransactionId}"))
            {
                entry.Valid = false;
                context.AddBreak(BreakType.Duplicate, entry, null,
                    $"Duplicate {handler.Type} {record.TransactionId} in advice (line {record.LineNumber})");
                continue;
            }

            resolved.Add((entry, handler));

            if (handler.RequiresOriginal)
            {
                ValidateOriginalReference(entry, context);
            }
        }

        foreach (var (entry, handler) in resolved.Where(r => r.Entry.Valid))
        {
            handler.Validate(entry, context);
        }
    }

    private static decimal Sign(decimal amount, Direction direction) => direction switch
    {
        Direction.Credit => Math.Abs(amount),
        Direction.Debit => -Math.Abs(amount),
        _ => amount,
    };

    private static void ValidateOriginalReference(ProcessedEntry entry, EngineContext context)
    {
        var originalId = entry.Record.OriginalTransactionId;
        if (string.IsNullOrEmpty(originalId))
        {
            entry.Valid = false;
            context.AddBreak(BreakType.OriginalNotFound, entry, null, $"{entry.CanonicalType} has no original transaction reference");
            return;
        }

        var inAdvice = context.Entries.Any(e => string.Equals(e.Record.TransactionId, originalId, StringComparison.OrdinalIgnoreCase));
        if (!inAdvice && !context.Internal.ContainsKey(originalId))
        {
            context.AddBreak(BreakType.OriginalNotFound, entry, null,
                $"Original transaction {originalId} not found in advice or internal transactions");
        }
    }
}
