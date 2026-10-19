namespace Settlement.Core.Model;

public sealed class AdviceRecord
{
    public int LineNumber { get; init; }
    public string TransactionId { get; set; } = string.Empty;
    public string? ProviderTransactionId { get; set; }
    public string? OriginalTransactionId { get; set; }
    public string TypeCode { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public decimal Fee { get; set; }
    public decimal? SettlementAmount { get; set; }
    public string? SettlementCurrency { get; set; }
    public decimal? ExchangeRate { get; set; }
    public DateTimeOffset? TransactionTime { get; set; }
    public DateTimeOffset? SettlementTime { get; set; }
    public string? Status { get; set; }
    public string? BatchId { get; set; }

    /// <summary>All mapped values (canonical and custom) keyed by configured field name.</summary>
    public Dictionary<string, object?> Fields { get; } = new(StringComparer.OrdinalIgnoreCase);

    public decimal GrossSettlement => SettlementAmount ?? Amount;
}

public sealed record RejectedRow(int LineNumber, string Reason, string RawLine);

public sealed record ControlTotals(int? ExpectedCount, decimal? ExpectedAmount);

public sealed class ParseResult
{
    public List<AdviceRecord> Records { get; } = [];
    public List<RejectedRow> Rejected { get; } = [];
    public ControlTotals? ControlTotals { get; set; }
}
