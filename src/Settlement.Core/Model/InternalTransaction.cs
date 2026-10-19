namespace Settlement.Core.Model;

public sealed record InternalTransaction(
    string TransactionId,
    string Type,
    decimal Amount,
    string Currency,
    string Status,
    DateTimeOffset? TransactionTime,
    string? OriginalTransactionId);
