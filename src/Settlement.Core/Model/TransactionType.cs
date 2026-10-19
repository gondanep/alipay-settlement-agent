namespace Settlement.Core.Model;

public static class TransactionTypes
{
    public const string Payment = "Payment";
    public const string Refund = "Refund";
    public const string Cancel = "Cancel";
    public const string Chargeback = "Chargeback";
    public const string ChargebackReversal = "ChargebackReversal";
    public const string Fee = "Fee";
    public const string Adjustment = "Adjustment";
}
