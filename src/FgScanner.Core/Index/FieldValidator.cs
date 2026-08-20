using System.Globalization;

namespace FgScanner.Core.Index;

/// <summary>Validates one field value against its definition. Dates ISO, numbers invariant (PLAN §5.2).</summary>
public static class FieldValidator
{
    /// <summary>Returns null when valid, else a human-readable reason.</summary>
    public static string? Validate(IndexFieldDef field, string? value, IReadOnlyList<string>? listChoices = null)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return field.Required ? $"{field.Name} is required." : null;
        }

        return field.Type switch
        {
            IndexFieldType.Date when !DateOnly.TryParseExact(value, "yyyy-MM-dd", out _) =>
                $"{field.Name} must be a date in YYYY-MM-DD format.",
            IndexFieldType.Number when !double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out _) =>
                $"{field.Name} must be a number (use . as the decimal separator).",
            IndexFieldType.List when listChoices is { Count: > 0 } && !listChoices.Contains(value, StringComparer.OrdinalIgnoreCase) =>
                $"{field.Name} must be one of: {string.Join(", ", listChoices)}.",
            _ => null,
        };
    }
}

/// <summary>Expands $(today), $(group), $(counter), $(user) in field default values (PLAN §5.4).</summary>
public static class TokenExpander
{
    public static string Expand(string template, string groupName, int counter, TimeProvider? time = null)
    {
        var today = (time ?? TimeProvider.System).GetLocalNow().Date;
        return template
            .Replace("$(today)", today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase)
            .Replace("$(group)", groupName, StringComparison.OrdinalIgnoreCase)
            .Replace("$(counter)", counter.ToString(CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase)
            .Replace("$(user)", Environment.UserName, StringComparison.OrdinalIgnoreCase);
    }
}
