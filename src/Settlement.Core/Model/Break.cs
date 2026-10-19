namespace Settlement.Core.Model;

public enum BreakType
{
    InvalidRecord,
    UnknownTransactionType,
    ControlTotalMismatch,
    Duplicate,
    MissingInInternal,
    MissingInAdvice,
    AmountMismatch,
    CurrencyMismatch,
    StatusMismatch,
    TypeMismatch,
    OriginalNotFound,
    RefundExceedsOriginal,
    CancelAmountMismatch,
    LateSettlement,
}

public enum Severity
{
    Low,
    Medium,
    High,
}

public sealed record Break(
    BreakType Type,
    Severity Severity,
    string? TransactionId,
    string? TransactionType,
    decimal? AdviceAmount,
    decimal? InternalAmount,
    string Reason,
    int? LineNumber = null);
