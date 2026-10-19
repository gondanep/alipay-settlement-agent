using System.Globalization;
using System.Text;
using Settlement.Core.Configuration;
using Settlement.Core.Model;

namespace Settlement.Core.Parsing;

public sealed class AdviceParser
{
    public ParseResult Parse(Stream stream, AdviceProfile profile)
    {
        var encoding = Encoding.GetEncoding(profile.Encoding);
        using var reader = new StreamReader(stream, encoding, detectEncodingFromByteOrderMarks: true);
        var lines = new List<string>();
        while (reader.ReadLine() is { } line)
        {
            lines.Add(line);
        }

        return Parse(lines, profile);
    }

    public ParseResult Parse(IReadOnlyList<string> lines, AdviceProfile profile)
    {
        var culture = string.IsNullOrEmpty(profile.Culture) ? CultureInfo.InvariantCulture : CultureInfo.GetCultureInfo(profile.Culture);
        var converter = new FieldConverter(culture, TimeZoneInfo.FindSystemTimeZoneById(profile.TimeZone));
        var result = new ParseResult();
        var lineIndex = profile.SkipLeadingRows;
        Dictionary<string, int>? header = null;

        if (profile.HasHeader)
        {
            while (lineIndex < lines.Count && string.IsNullOrWhiteSpace(lines[lineIndex]))
            {
                lineIndex++;
            }

            if (lineIndex >= lines.Count)
            {
                return result;
            }

            header = DelimitedLineSplitter.Split(lines[lineIndex], profile.Delimiter, profile.Quote)
                .Select((name, i) => (name: name.Trim().TrimStart('\uFEFF'), i))
                .GroupBy(x => x.name, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First().i, StringComparer.OrdinalIgnoreCase);
            lineIndex++;

            var missing = profile.Fields
                .Where(f => f.Index is null && f.Required && !header.ContainsKey(f.Source ?? f.Name))
                .Select(f => f.Source ?? f.Name)
                .ToList();
            if (missing.Count > 0)
            {
                result.Rejected.Add(new RejectedRow(lineIndex, $"Header is missing required columns: {string.Join(", ", missing)}", lines[lineIndex - 1]));
                return result;
            }
        }

        for (; lineIndex < lines.Count; lineIndex++)
        {
            var line = lines[lineIndex];
            var lineNumber = lineIndex + 1;
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var values = DelimitedLineSplitter.Split(line, profile.Delimiter, profile.Quote);

            if (profile.ControlTotals is { } ct && StartsWith(values, ct.RowPrefix))
            {
                result.ControlTotals = ReadControlTotals(values, ct, culture);
                continue;
            }

            if (profile.TrailerPrefixes.Any(p => StartsWith(values, p)))
            {
                continue;
            }

            try
            {
                result.Records.Add(MapRecord(values, header, profile, converter, lineNumber));
            }
            catch (FieldConversionException ex)
            {
                result.Rejected.Add(new RejectedRow(lineNumber, ex.Message, line));
            }
        }

        return result;
    }

    private static bool StartsWith(List<string> values, string prefix) =>
        values.Count > 0 && values[0].Trim().StartsWith(prefix, StringComparison.OrdinalIgnoreCase);

    private static ControlTotals ReadControlTotals(List<string> values, ControlTotalOptions ct, CultureInfo culture)
    {
        int? count = ct.CountIndex is { } ci && ci < values.Count && int.TryParse(values[ci].Trim(), NumberStyles.Integer, culture, out var c) ? c : null;
        decimal? amount = ct.AmountIndex is { } ai && ai < values.Count && decimal.TryParse(values[ai].Trim(), NumberStyles.Number | NumberStyles.AllowLeadingSign, culture, out var a) ? a : null;
        return new ControlTotals(count, amount);
    }

    private static AdviceRecord MapRecord(List<string> values, Dictionary<string, int>? header, AdviceProfile profile, FieldConverter converter, int lineNumber)
    {
        var record = new AdviceRecord { LineNumber = lineNumber };
        foreach (var field in profile.Fields)
        {
            var index = field.Index ?? (header is not null && header.TryGetValue(field.Source ?? field.Name, out var hi) ? hi : -1);
            var raw = index >= 0 && index < values.Count ? values[index] : null;
            record.Fields[field.Name] = converter.Convert(field, raw);
        }

        Apply(record);
        foreach (var name in CanonicalFields.Required)
        {
            if (!record.Fields.TryGetValue(name, out var v) || v is null || (v is string s && s.Length == 0))
            {
                throw new FieldConversionException($"Canonical field '{name}' is not mapped or empty");
            }
        }

        return record;
    }

    private static void Apply(AdviceRecord r)
    {
        r.TransactionId = Str(r, CanonicalFields.TransactionId) ?? string.Empty;
        r.ProviderTransactionId = Str(r, CanonicalFields.ProviderTransactionId);
        r.OriginalTransactionId = Str(r, CanonicalFields.OriginalTransactionId);
        r.TypeCode = Str(r, CanonicalFields.TransactionType) ?? string.Empty;
        r.Amount = Dec(r, CanonicalFields.Amount) ?? 0m;
        r.Currency = Str(r, CanonicalFields.Currency) ?? string.Empty;
        r.Fee = Dec(r, CanonicalFields.Fee) ?? 0m;
        r.SettlementAmount = Dec(r, CanonicalFields.SettlementAmount);
        r.SettlementCurrency = Str(r, CanonicalFields.SettlementCurrency);
        r.ExchangeRate = Dec(r, CanonicalFields.ExchangeRate);
        r.TransactionTime = r.Fields.GetValueOrDefault(CanonicalFields.TransactionTime) as DateTimeOffset?;
        r.SettlementTime = r.Fields.GetValueOrDefault(CanonicalFields.SettlementTime) as DateTimeOffset?;
        r.Status = Str(r, CanonicalFields.Status);
        r.BatchId = Str(r, CanonicalFields.BatchId);
    }

    private static string? Str(AdviceRecord r, string name) =>
        r.Fields.TryGetValue(name, out var v) && v is not null ? System.Convert.ToString(v, CultureInfo.InvariantCulture) : null;

    private static decimal? Dec(AdviceRecord r, string name) =>
        r.Fields.TryGetValue(name, out var v) && v is not null ? System.Convert.ToDecimal(v, CultureInfo.InvariantCulture) : null;
}
