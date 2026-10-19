namespace Settlement.Core.Configuration;

public sealed class SettlementOptions
{
    public const string SectionName = "Settlement";

    public SfgOptions Sfg { get; set; } = new();
    public Dictionary<string, AdviceProfile> Profiles { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public InternalSourceOptions InternalSource { get; set; } = new();
    public MatchingOptions Matching { get; set; } = new();
    public OutputOptions Output { get; set; } = new();
}

public enum SfgMode
{
    Local,
    Sftp,
}

public sealed class SfgOptions
{
    public SfgMode Mode { get; set; } = SfgMode.Local;
    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 22;
    public string Username { get; set; } = string.Empty;
    public string? Password { get; set; }
    public string? PrivateKeyPath { get; set; }
    public string? PrivateKeyPassphrase { get; set; }
    public string InboundPath { get; set; } = "inbound";
    public string ProcessedPath { get; set; } = "processed";
    public string ErrorPath { get; set; } = "error";
    public string FilePattern { get; set; } = "*.csv";
    public int PollIntervalSeconds { get; set; } = 300;
    public string LedgerPath { get; set; } = "state/processed-files.json";
    public string WorkingPath { get; set; } = "work";
}

public sealed class InternalSourceOptions
{
    public string Path { get; set; } = "internal/transactions.csv";
    public string Delimiter { get; set; } = ",";
    public string DateTimeFormat { get; set; } = "yyyy-MM-dd HH:mm:ss";
}

public sealed class MatchingOptions
{
    public decimal AmountTolerance { get; set; } = 0.01m;
    public int SettlementWindowDays { get; set; } = 2;
    public decimal HighSeverityAmount { get; set; } = 10000m;
    public bool ReportMissingInAdvice { get; set; } = true;
    public List<string> SettledInternalStatuses { get; set; } = ["SUCCESS", "SETTLED"];
}

public sealed class OutputOptions
{
    public string ReportsPath { get; set; } = "reports";
}
