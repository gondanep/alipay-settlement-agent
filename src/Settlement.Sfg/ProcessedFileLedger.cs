using System.Security.Cryptography;
using System.Text.Json;

namespace Settlement.Sfg;

public sealed record LedgerEntry(string FileName, string Sha256, DateTimeOffset ProcessedAt, string Outcome);

/// <summary>Remembers content hashes of processed files so a re-delivered file is not settled twice.</summary>
public sealed class ProcessedFileLedger
{
    private readonly string _path;
    private readonly Dictionary<string, LedgerEntry> _entries;

    public ProcessedFileLedger(string path)
    {
        _path = path;
        _entries = File.Exists(path)
            ? (JsonSerializer.Deserialize<List<LedgerEntry>>(File.ReadAllText(path)) ?? []).ToDictionary(e => e.Sha256)
            : [];
    }

    public static string Hash(byte[] content) => Convert.ToHexString(SHA256.HashData(content));

    public bool TryGet(string sha256, out LedgerEntry? entry) => _entries.TryGetValue(sha256, out entry);

    public void Record(string fileName, string sha256, string outcome)
    {
        _entries[sha256] = new LedgerEntry(fileName, sha256, DateTimeOffset.UtcNow, outcome);
        var dir = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        File.WriteAllText(_path, JsonSerializer.Serialize(_entries.Values.OrderBy(e => e.ProcessedAt), new JsonSerializerOptions { WriteIndented = true }));
    }
}
