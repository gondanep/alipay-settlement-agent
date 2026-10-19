namespace Settlement.Core.Model;

public sealed class ProcessedEntry
{
    public required AdviceRecord Record { get; init; }
    public required string CanonicalType { get; init; }
    public decimal SignedGross { get; init; }
    public decimal SignedFee { get; init; }
    public decimal Net => SignedGross - SignedFee;
    public bool MatchInternal { get; init; }
    public bool Valid { get; set; } = true;
}
