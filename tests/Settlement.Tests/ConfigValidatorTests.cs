using Settlement.Core.Configuration;

namespace Settlement.Tests;

public class ConfigValidatorTests
{
    private static SettlementOptions With(AdviceProfile profile) => new() { Profiles = { ["p"] = profile } };

    [Fact]
    public void Reports_missing_canonical_field_and_unresolvable_type()
    {
        var profile = TestSupport.SimpleProfile();
        profile.Fields.RemoveAll(f => f.Name == CanonicalFields.Currency);
        profile.TransactionTypes["Q"] = new TransactionTypeRule { Type = "Mystery" };

        var errors = ConfigValidator.Validate(With(profile));

        Assert.Contains(errors, e => e.Contains("'Currency' is not mapped"));
        Assert.Contains(errors, e => e.Contains("'Q' maps to 'Mystery'"));
    }

    [Fact]
    public void Requires_index_mapping_when_file_has_no_header()
    {
        var profile = TestSupport.SimpleProfile();
        profile.HasHeader = false;

        Assert.Contains(ConfigValidator.Validate(With(profile)), e => e.Contains("needs an Index"));
    }

    [Fact]
    public void Reports_invalid_pattern_time_zone_and_no_profiles()
    {
        var profile = TestSupport.SimpleProfile();
        profile.FileNamePattern = "([";
        profile.TimeZone = "Mars/Olympus";

        var errors = ConfigValidator.Validate(With(profile));

        Assert.Contains(errors, e => e.Contains("FileNamePattern"));
        Assert.Contains(errors, e => e.Contains("time zone"));
        Assert.NotEmpty(ConfigValidator.Validate(new SettlementOptions()));
    }
}
