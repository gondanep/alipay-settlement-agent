using System.Text.RegularExpressions;
using Settlement.Core.Configuration;
using Settlement.Core.Engine;
using Settlement.Core.Model;
using Settlement.Core.Parsing;
using Settlement.Core.Reconciliation;
using Settlement.Core.Reporting;

namespace Settlement.Core.Pipeline;

public sealed record SettlementRunResult(
    SettlementSummary Summary,
    ParseResult Parse,
    IReadOnlyList<ProcessedEntry> Entries,
    IReadOnlyList<ProcessedEntry> Matched,
    IReadOnlyList<Break> Breaks);

public sealed class NoMatchingProfileException(string fileName)
    : Exception($"No advice profile matches file name '{fileName}'");

public sealed class SettlementPipeline(SettlementOptions options, TransactionTypeEngine? engine = null)
{
    private readonly TransactionTypeEngine _engine = engine ?? new TransactionTypeEngine();
    private readonly AdviceParser _parser = new();
    private readonly Reconciler _reconciler = new();

    public KeyValuePair<string, AdviceProfile> SelectProfile(string fileName)
    {
        foreach (var profile in options.Profiles)
        {
            if (Regex.IsMatch(fileName, profile.Value.FileNamePattern, RegexOptions.IgnoreCase))
            {
                return profile;
            }
        }

        throw new NoMatchingProfileException(fileName);
    }

    public SettlementRunResult Process(string fileName, Stream content, IReadOnlyList<InternalTransaction> internalTransactions)
    {
        var (profileName, profile) = SelectProfile(fileName);
        var parse = _parser.Parse(content, profile);

        var internalById = internalTransactions
            .GroupBy(t => t.TransactionId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        var context = new EngineContext(options.Matching, internalById);

        foreach (var rejected in parse.Rejected)
        {
            context.Breaks.Add(new Break(BreakType.InvalidRecord, Severity.Medium, null, null, null, null, rejected.Reason, rejected.LineNumber));
        }

        CheckControlTotals(parse, profile, context);
        _engine.Process(parse.Records, profile, context);
        var matched = _reconciler.Reconcile(context);

        var summary = SettlementSummary.Build(fileName, profileName, profile.Provider, parse, context.Entries, matched, context.Breaks);
        return new SettlementRunResult(summary, parse, context.Entries, matched, context.Breaks);
    }

    private static void CheckControlTotals(ParseResult parse, AdviceProfile profile, EngineContext context)
    {
        if (parse.ControlTotals is not { } ct || profile.ControlTotals is not { } cfg)
        {
            return;
        }

        var actualCount = parse.Records.Count + parse.Rejected.Count;
        if (ct.ExpectedCount is { } expectedCount && expectedCount != actualCount)
        {
            context.Breaks.Add(new Break(BreakType.ControlTotalMismatch, Severity.High, null, null, null, null,
                $"Control total count {expectedCount} but file has {actualCount} records"));
        }

        if (ct.ExpectedAmount is { } expectedAmount)
        {
            var actual = parse.Records.Sum(r => r.Fields.GetValueOrDefault(cfg.AmountField) is decimal d ? d : 0m);
            if (Math.Abs(actual - expectedAmount) > context.Options.AmountTolerance)
            {
                context.Breaks.Add(new Break(BreakType.ControlTotalMismatch, Severity.High, null, null, actual, expectedAmount,
                    $"Control total {cfg.AmountField} {expectedAmount} but records sum to {actual}"));
            }
        }
    }
}
