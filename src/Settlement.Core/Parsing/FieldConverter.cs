using System.Globalization;
using Settlement.Core.Configuration;

namespace Settlement.Core.Parsing;

public sealed class FieldConversionException(string message) : Exception(message);

public sealed class FieldConverter(CultureInfo culture, TimeZoneInfo timeZone)
{
    public object? Convert(FieldMapping field, string? raw)
    {
        var value = raw;
        if (value is not null && field.Trim)
        {
            value = value.Trim();
        }

        if (string.IsNullOrEmpty(value))
        {
            value = field.Default;
        }

        if (string.IsNullOrEmpty(value))
        {
            if (field.Required)
            {
                throw new FieldConversionException($"Required field '{field.Name}' is empty");
            }

            return null;
        }

        value = field.Transform switch
        {
            FieldTransform.Upper => value.ToUpperInvariant(),
            FieldTransform.Lower => value.ToLowerInvariant(),
            _ => value,
        };

        if (field.MaxLength is { } max && value.Length > max)
        {
            throw new FieldConversionException($"Field '{field.Name}' exceeds max length {max}");
        }

        return field.Type switch
        {
            FieldDataType.String => value,
            FieldDataType.Decimal => ParseDecimal(field, value),
            FieldDataType.Integer => long.TryParse(value, NumberStyles.Integer, culture, out var l)
                ? l
                : throw Invalid(field, value, "integer"),
            FieldDataType.Boolean => ParseBoolean(field, value),
            FieldDataType.Date or FieldDataType.DateTime => ParseDateTime(field, value),
            _ => value,
        };
    }

    private decimal ParseDecimal(FieldMapping field, string value)
    {
        return decimal.TryParse(value, NumberStyles.Number | NumberStyles.AllowLeadingSign, culture, out var d)
            ? d
            : throw Invalid(field, value, "decimal");
    }

    private static bool ParseBoolean(FieldMapping field, string value) => value.ToUpperInvariant() switch
    {
        "TRUE" or "Y" or "YES" or "1" => true,
        "FALSE" or "N" or "NO" or "0" => false,
        _ => throw Invalid(field, value, "boolean"),
    };

    private DateTimeOffset ParseDateTime(FieldMapping field, string value)
    {
        DateTime parsed;
        var ok = string.IsNullOrEmpty(field.Format)
            ? DateTime.TryParse(value, culture, DateTimeStyles.None, out parsed)
            : DateTime.TryParseExact(value, field.Format.Split('|'), culture, DateTimeStyles.None, out parsed);
        if (!ok)
        {
            throw Invalid(field, value, field.Format ?? "date");
        }

        var offset = timeZone.GetUtcOffset(DateTime.SpecifyKind(parsed, DateTimeKind.Unspecified));
        return new DateTimeOffset(DateTime.SpecifyKind(parsed, DateTimeKind.Unspecified), offset);
    }

    private static FieldConversionException Invalid(FieldMapping field, string value, string expected) =>
        new($"Field '{field.Name}' value '{value}' is not a valid {expected}");
}
