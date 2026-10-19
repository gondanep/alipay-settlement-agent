using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Settlement.Core.Model;
using Settlement.Core.Pipeline;

namespace Settlement.Core.Reporting;

public sealed class ReportWriter
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public string Write(SettlementRunResult result, string reportsRoot)
    {
        var dir = Path.Combine(reportsRoot, Path.GetFileNameWithoutExtension(result.Summary.FileName));
        Directory.CreateDirectory(dir);

        File.WriteAllText(Path.Combine(dir, "summary.json"), JsonSerializer.Serialize(result.Summary, Json));
        WriteCsv(Path.Combine(dir, "breaks.csv"),
            ["BreakType", "Severity", "TransactionId", "TransactionType", "AdviceAmount", "InternalAmount", "LineNumber", "Reason"],
            result.Breaks.Select(b => new object?[] { b.Type, b.Severity, b.TransactionId, b.TransactionType, b.AdviceAmount, b.InternalAmount, b.LineNumber, b.Reason }));
        WriteCsv(Path.Combine(dir, "rejected.csv"),
            ["LineNumber", "Reason", "RawLine"],
            result.Parse.Rejected.Select(r => new object?[] { r.LineNumber, r.Reason, r.RawLine }));

        var fieldNames = result.Entries.SelectMany(e => e.Record.Fields.Keys).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var matchedIds = result.Matched.ToHashSet();
        WriteCsv(Path.Combine(dir, "entries.csv"),
            new[] { "LineNumber", "CanonicalType", "SignedGross", "Fee", "Net", "Matched", "Valid" }.Concat(fieldNames).ToArray(),
            result.Entries.Select(e => new object?[] { e.Record.LineNumber, e.CanonicalType, e.SignedGross, e.SignedFee, e.Net, matchedIds.Contains(e), e.Valid }
                .Concat(fieldNames.Select(f => e.Record.Fields.GetValueOrDefault(f))).ToArray()));
        return dir;
    }

    private static void WriteCsv(string path, string[] header, IEnumerable<object?[]> rows)
    {
        var sb = new StringBuilder();
        sb.AppendLine(string.Join(',', header.Select(Escape)));
        foreach (var row in rows)
        {
            sb.AppendLine(string.Join(',', row.Select(v => Escape(Format(v)))));
        }

        File.WriteAllText(path, sb.ToString());
    }

    private static string Format(object? v) => v switch
    {
        null => string.Empty,
        DateTimeOffset d => d.ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture),
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => v.ToString() ?? string.Empty,
    };

    private static string Escape(string s) =>
        s.IndexOfAny([',', '"', '\n', '\r']) >= 0 ? $"\"{s.Replace("\"", "\"\"")}\"" : s;
}
