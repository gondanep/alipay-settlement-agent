namespace Settlement.Core.Configuration;

public static class CanonicalFields
{
    public const string TransactionId = "TransactionId";
    public const string ProviderTransactionId = "ProviderTransactionId";
    public const string OriginalTransactionId = "OriginalTransactionId";
    public const string TransactionType = "TransactionType";
    public const string Amount = "Amount";
    public const string Currency = "Currency";
    public const string Fee = "Fee";
    public const string SettlementAmount = "SettlementAmount";
    public const string SettlementCurrency = "SettlementCurrency";
    public const string ExchangeRate = "ExchangeRate";
    public const string TransactionTime = "TransactionTime";
    public const string SettlementTime = "SettlementTime";
    public const string Status = "Status";
    public const string BatchId = "BatchId";

    public static readonly IReadOnlySet<string> Required = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        TransactionId,
        TransactionType,
        Amount,
        Currency,
    };
}
