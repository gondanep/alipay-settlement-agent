using Settlement.Core.Configuration;
using Settlement.Core.Parsing;

namespace Settlement.Tests;

public class AdviceParserTests
{
    private readonly AdviceParser _parser = new();

    [Fact]
    public void Maps_fields_by_header_name_with_types_defaults_and_transforms()
    {
        var result = _parser.Parse([TestSupport.Header, "P1,,p,100.50,,usd,2026-10-17 10:00:00,2026-10-18 09:00:00"], TestSupport.SimpleProfile());

        var r = Assert.Single(result.Records);
        Assert.Equal("P1", r.TransactionId);
        Assert.Equal("P", r.TypeCode);
        Assert.Equal(100.50m, r.Amount);
        Assert.Equal(0m, r.Fee);
        Assert.Equal("usd", r.Currency);
        Assert.Equal(new DateTimeOffset(2026, 10, 17, 10, 0, 0, TimeSpan.Zero), r.TransactionTime);
        Assert.Empty(result.Rejected);
    }

    [Fact]
    public void Maps_fields_by_index_when_file_has_no_header()
    {
        var profile = new AdviceProfile
        {
            HasHeader = false,
            Delimiter = "|",
            Fields =
            [
                new FieldMapping { Name = CanonicalFields.TransactionId, Index = 0 },
                new FieldMapping { Name = CanonicalFields.TransactionType, Index = 1 },
                new FieldMapping { Name = CanonicalFields.Amount, Index = 2, Type = FieldDataType.Decimal },
                new FieldMapping { Name = CanonicalFields.Currency, Index = 3 },
            ],
        };

        var r = Assert.Single(_parser.Parse(["T9|P|12.30|EUR"], profile).Records);

        Assert.Equal(("T9", 12.30m, "EUR"), (r.TransactionId, r.Amount, r.Currency));
    }

    [Fact]
    public void Keeps_custom_fields_alongside_canonical_ones()
    {
        var profile = TestSupport.SimpleProfile();
        profile.Fields.Add(new FieldMapping { Name = "WalletBrand", Source = "wallet", Transform = FieldTransform.Upper });

        var r = Assert.Single(_parser.Parse([TestSupport.Header + ",wallet", "P1,,P,1,0,USD,,,gcash"], profile).Records);

        Assert.Equal("GCASH", r.Fields["WalletBrand"]);
    }

    [Fact]
    public void Rejects_rows_with_invalid_or_missing_required_values()
    {
        var result = _parser.Parse([TestSupport.Header, "P1,,P,abc,0,USD,,", ",,P,1,0,USD,,", "P3,,P,5,0,USD,,"], TestSupport.SimpleProfile());

        Assert.Single(result.Records);
        Assert.Collection(result.Rejected,
            r => Assert.Contains("'Amount' value 'abc' is not a valid decimal", r.Reason),
            r => Assert.Contains("Required field 'TransactionId' is empty", r.Reason));
        Assert.Equal([2, 3], result.Rejected.Select(r => r.LineNumber));
    }

    [Fact]
    public void Rejects_file_when_header_misses_required_column()
    {
        var result = _parser.Parse(["id,type,ccy", "P1,P,USD"], TestSupport.SimpleProfile());

        Assert.Empty(result.Records);
        Assert.Contains("amount", Assert.Single(result.Rejected).Reason);
    }

    [Fact]
    public void Handles_quoted_values_containing_delimiters()
    {
        var profile = TestSupport.SimpleProfile();
        profile.Fields.Add(new FieldMapping { Name = "Remarks", Source = "remarks" });

        var r = Assert.Single(_parser.Parse([TestSupport.Header + ",remarks", "P1,,P,1,0,USD,,,\"refund, \"\"partial\"\"\""], profile).Records);

        Assert.Equal("refund, \"partial\"", r.Fields["Remarks"]);
    }

    [Fact]
    public void Reads_control_totals_and_skips_trailer_and_leading_rows()
    {
        var profile = TestSupport.SimpleProfile();
        profile.SkipLeadingRows = 1;
        profile.TrailerPrefixes = ["#"];
        profile.ControlTotals = new ControlTotalOptions { RowPrefix = "TOTAL", CountIndex = 1, AmountIndex = 2 };

        var result = _parser.Parse(["Alipay settlement 20261018", TestSupport.Header, "P1,,P,10,0,USD,,", "TOTAL,1,10.00", "# end"], profile);

        Assert.Single(result.Records);
        Assert.Equal(1, result.ControlTotals!.ExpectedCount);
        Assert.Equal(10.00m, result.ControlTotals.ExpectedAmount);
    }

    [Fact]
    public void Applies_profile_time_zone_to_dates()
    {
        var profile = TestSupport.SimpleProfile();
        profile.TimeZone = "Asia/Shanghai";

        var r = Assert.Single(_parser.Parse([TestSupport.Header, "P1,,P,1,0,USD,2026-10-17 10:30:00,"], profile).Records);

        Assert.Equal(new DateTimeOffset(2026, 10, 17, 2, 30, 0, TimeSpan.Zero), r.TransactionTime!.Value.ToUniversalTime());
    }

    [Fact]
    public void Parses_shipped_alipay_plus_sample_with_pipe_delimiter()
    {
        var profile = TestSupport.LoadOptions().Profiles["alipay-plus"];
        using var stream = File.OpenRead(TestSupport.Sample("advice", "APLUS_SETTLE_20261018.csv"));

        var result = _parser.Parse(stream, profile);

        Assert.Equal(4, result.Records.Count);
        Assert.Empty(result.Rejected);
        Assert.Equal("GCASH", result.Records[1].Fields["WalletBrand"]);
    }
}
