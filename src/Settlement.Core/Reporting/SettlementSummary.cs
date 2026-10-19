using Settlement.Core.Model;

namespace Settlement.Core.Reporting;

public sealed record TypeTotals(string Type, int Count, decimal Gross, decimal Fee, decimal Net);

public sealed record SettlementSummary(
    string FileName,
    string Profile,
    string Provider,
    DateTimeOffset ProcessedAt,
    int Records,
    int Rejected,
    int Matchable,
    int Matched,
    decimal MatchRate,
    decimal NetSettlement,
    IReadOnlyList<TypeTotals> Totals,
    IReadOnlyDictionary<string, int> BreaksByType,
    IReadOnlyDictionary<string, decimal> NetByCurrency)
{
    public static SettlementSummary Build(string fileName, string profileName, string provider, ParseResult parse,
        IReadOnlyList<ProcessedEntry> entries, IReadOnlyList<ProcessedEntry> matched, IReadOnlyList<Break> breaks)
    {
        var valid = entries.Where(e => e.Valid).ToList();
        var matchable = valid.Count(e => e.MatchInternal);
        var totals = valid.GroupBy(e => e.CanonicalType)
            .Select(g => new TypeTotals(g.Key, g.Count(), g.Sum(e => e.SignedGross), g.Sum(e => e.SignedFee), g.Sum(e => e.Net)))
            .OrderBy(t => t.Type)
            .ToList();
        return new SettlementSummary(
            fileName,
            profileName,
            provider,
            DateTimeOffset.UtcNow,
            parse.Records.Count,
            parse.Rejected.Count,
            matchable,
            matched.Count,
            matchable == 0 ? 1m : Math.Round((decimal)matched.Count / matchable, 4),
            totals.Sum(t => t.Net),
            totals,
            breaks.GroupBy(b => b.Type.ToString()).ToDictionary(g => g.Key, g => g.Count()),
            valid.GroupBy(e => e.Record.SettlementCurrency ?? e.Record.Currency).ToDictionary(g => g.Key, g => g.Sum(e => e.Net)));
    }
}
