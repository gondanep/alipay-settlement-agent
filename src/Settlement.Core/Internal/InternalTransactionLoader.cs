using System.Globalization;
using System.Text.Json;
using Settlement.Core.Configuration;
using Settlement.Core.Model;
using Settlement.Core.Parsing;

namespace Settlement.Core.Internal;

public interface IInternalTransactionSource
{
    IReadOnlyList<InternalTransaction> Load();
}

/// <summary>Loads our system's transaction export (CSV with header, or JSON array).</summary>
public sealed class FileInternalTransactionSource(InternalSourceOptions options) : IInternalTransactionSource
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public IReadOnlyList<InternalTransaction> Load()
    {
        if (!File.Exists(options.Path))
        {
            throw new FileNotFoundException($"Internal transactions file not found: {options.Path}");
        }

        return Path.GetExtension(options.Path).Equals(".json", StringComparison.OrdinalIgnoreCase)
            ? JsonSerializer.Deserialize<List<InternalTransaction>>(File.ReadAllText(options.Path), JsonOptions) ?? []
            : LoadCsv(File.ReadAllLines(options.Path));
    }

    private List<InternalTransaction> LoadCsv(string[] lines)
    {
        if (lines.Length == 0)
        {
            return [];
        }

        var header = DelimitedLineSplitter.Split(lines[0], options.Delimiter, '"')
            .Select((h, i) => (h: h.Trim(), i))
            .ToDictionary(x => x.h, x => x.i, StringComparer.OrdinalIgnoreCase);
        string? Get(List<string> v, string name) => header.TryGetValue(name, out var i) && i < v.Count && v[i].Trim().Length > 0 ? v[i].Trim() : null;

        var result = new List<InternalTransaction>();
        foreach (var line in lines.Skip(1).Where(l => !string.IsNullOrWhiteSpace(l)))
        {
            var v = DelimitedLineSplitter.Split(line, options.Delimiter, '"');
            var time = Get(v, "TransactionTime");
            result.Add(new InternalTransaction(
                Get(v, "TransactionId") ?? throw new FormatException($"Internal row without TransactionId: {line}"),
                Get(v, "Type") ?? TransactionTypes.Payment,
                decimal.Parse(Get(v, "Amount") ?? "0", NumberStyles.Number | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture),
                Get(v, "Currency") ?? string.Empty,
                Get(v, "Status") ?? string.Empty,
                time is null ? null : new DateTimeOffset(DateTime.ParseExact(time, options.DateTimeFormat, CultureInfo.InvariantCulture), TimeSpan.Zero),
                Get(v, "OriginalTransactionId")));
        }

        return result;
    }
}
