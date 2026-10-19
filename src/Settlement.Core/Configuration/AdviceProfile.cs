namespace Settlement.Core.Configuration;

public sealed class AdviceProfile
{
    public string Provider { get; set; } = "Alipay";
    public string FileNamePattern { get; set; } = ".*";
    public string Encoding { get; set; } = "utf-8";
    public string Delimiter { get; set; } = ",";
    public char Quote { get; set; } = '"';
    public bool HasHeader { get; set; } = true;
    public int SkipLeadingRows { get; set; }
    public List<string> TrailerPrefixes { get; set; } = [];
    public ControlTotalOptions? ControlTotals { get; set; }
    public string Culture { get; set; } = "";
    public string TimeZone { get; set; } = "UTC";
    public List<FieldMapping> Fields { get; set; } = [];
    public Dictionary<string, TransactionTypeRule> TransactionTypes { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public enum FieldDataType
{
    String,
    Decimal,
    Integer,
    Date,
    DateTime,
    Boolean,
}

public enum FieldTransform
{
    None,
    Upper,
    Lower,
}

public sealed class FieldMapping
{
    public string Name { get; set; } = string.Empty;
    public string? Source { get; set; }
    public int? Index { get; set; }
    public FieldDataType Type { get; set; } = FieldDataType.String;
    public string? Format { get; set; }
    public bool Required { get; set; }
    public string? Default { get; set; }
    public bool Trim { get; set; } = true;
    public FieldTransform Transform { get; set; } = FieldTransform.None;
    public int? MaxLength { get; set; }
}

public sealed class ControlTotalOptions
{
    public string RowPrefix { get; set; } = "TOTAL";
    public int? CountIndex { get; set; }
    public int? AmountIndex { get; set; }
    public string AmountField { get; set; } = CanonicalFields.SettlementAmount;
}

public enum Direction
{
    Credit,
    Debit,
    Signed,
}

public sealed class TransactionTypeRule
{
    public string Type { get; set; } = string.Empty;
    public Direction? Direction { get; set; }
    public bool? RequiresOriginal { get; set; }
    public bool? MatchInternal { get; set; }
}
