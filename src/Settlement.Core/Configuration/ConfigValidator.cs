using System.Text;
using System.Text.RegularExpressions;
using Settlement.Core.Engine;

namespace Settlement.Core.Configuration;

public static class ConfigValidator
{
    public static List<string> Validate(SettlementOptions options, TransactionTypeEngine? engine = null)
    {
        engine ??= new TransactionTypeEngine();
        var errors = new List<string>();
        if (options.Profiles.Count == 0)
        {
            errors.Add("At least one advice profile must be configured under Settlement:Profiles");
        }

        foreach (var (name, p) in options.Profiles)
        {
            var prefix = $"Profile '{name}'";
            try
            {
                _ = new Regex(p.FileNamePattern);
            }
            catch (ArgumentException ex)
            {
                errors.Add($"{prefix}: invalid FileNamePattern ({ex.Message})");
            }

            try
            {
                _ = Encoding.GetEncoding(p.Encoding);
            }
            catch (ArgumentException)
            {
                errors.Add($"{prefix}: unknown encoding '{p.Encoding}'");
            }

            try
            {
                _ = TimeZoneInfo.FindSystemTimeZoneById(p.TimeZone);
            }
            catch (TimeZoneNotFoundException)
            {
                errors.Add($"{prefix}: unknown time zone '{p.TimeZone}'");
            }

            if (string.IsNullOrEmpty(p.Delimiter))
            {
                errors.Add($"{prefix}: Delimiter must not be empty");
            }

            foreach (var dup in p.Fields.GroupBy(f => f.Name, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
            {
                errors.Add($"{prefix}: field '{dup.Key}' is mapped more than once");
            }

            foreach (var f in p.Fields)
            {
                if (string.IsNullOrWhiteSpace(f.Name))
                {
                    errors.Add($"{prefix}: a field has no Name");
                }

                if (!p.HasHeader && f.Index is null)
                {
                    errors.Add($"{prefix}: field '{f.Name}' needs an Index because the file has no header");
                }
            }

            foreach (var required in CanonicalFields.Required)
            {
                if (!p.Fields.Any(f => string.Equals(f.Name, required, StringComparison.OrdinalIgnoreCase)))
                {
                    errors.Add($"{prefix}: canonical field '{required}' is not mapped");
                }
            }

            if (p.TransactionTypes.Count == 0)
            {
                errors.Add($"{prefix}: no TransactionTypes configured");
            }

            foreach (var (code, rule) in p.TransactionTypes)
            {
                if (engine.Resolve(rule) is null)
                {
                    errors.Add($"{prefix}: type code '{code}' maps to '{rule.Type}', which has no handler; set Direction to use the generic handler");
                }
            }
        }

        return errors;
    }
}
