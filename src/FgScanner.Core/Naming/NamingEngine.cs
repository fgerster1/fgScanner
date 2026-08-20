using System.Globalization;
using System.Text.RegularExpressions;

namespace FgScanner.Core.Naming;

/// <summary>Inputs for one file-name expansion. Timestamp is supplied by the caller so names are testable.</summary>
public sealed record NamingContext
{
    public DateTime Timestamp { get; init; }
    public string GroupName { get; init; } = "";
    public int DocumentSequence { get; init; }
    public int PageSequence { get; init; }
    public IReadOnlyDictionary<string, string?> FieldValues { get; init; } =
        new Dictionary<string, string?>();
}

/// <summary>
/// NAPS2-style placeholder engine (PLAN prompt 4): $(YYYY) $(MM) $(DD) $(hh) $(mm) $(ss),
/// $(n)…$(nnnn) auto-increment, plus FG tokens $(group) $(doc) $(page) $(field:Name).
/// $(barcode) is reserved (expands empty until phase 10). Values are slugified for Windows;
/// collisions get the counter bumped, or a numeric suffix when the pattern has no counter.
/// </summary>
public static partial class NamingEngine
{
    [GeneratedRegex(@"\$\(([^)]+)\)")]
    private static partial Regex TokenRegex();

    public static string Expand(string pattern, NamingContext context, int counter = 1) =>
        TokenRegex().Replace(pattern, match => ExpandToken(match.Groups[1].Value, context, counter));

    /// <summary>
    /// Expands to a name not already taken per <paramref name="exists"/>. Patterns with a counter
    /// token bump the counter (NAPS2 behavior); others get " (2)", " (3)"… before the extension.
    /// </summary>
    public static string ExpandUnique(string pattern, NamingContext context, Func<string, bool> exists)
    {
        if (TokenRegex().Matches(pattern).Any(m => IsCounterToken(m.Groups[1].Value)))
        {
            for (var counter = 1; ; counter++)
            {
                var name = Expand(pattern, context, counter);
                if (!exists(name))
                {
                    return name;
                }
            }
        }

        var expanded = Expand(pattern, context);
        if (!exists(expanded))
        {
            return expanded;
        }

        var stem = Path.GetFileNameWithoutExtension(expanded);
        var extension = Path.GetExtension(expanded);
        for (var suffix = 2; ; suffix++)
        {
            var candidate = $"{stem} ({suffix.ToString(CultureInfo.InvariantCulture)}){extension}";
            if (!exists(candidate))
            {
                return candidate;
            }
        }
    }

    private static bool IsCounterToken(string token) =>
        token.Length is >= 1 and <= 4 && token.All(c => c == 'n');

    private static string ExpandToken(string token, NamingContext context, int counter)
    {
        if (IsCounterToken(token))
        {
            return counter.ToString(CultureInfo.InvariantCulture).PadLeft(token.Length, '0');
        }

        if (token.StartsWith("field:", StringComparison.OrdinalIgnoreCase))
        {
            var fieldName = token["field:".Length..];
            return context.FieldValues.TryGetValue(fieldName, out var value)
                ? Slugify(value ?? "")
                : "";
        }

        var t = context.Timestamp;
        return token switch
        {
            "YYYY" => t.Year.ToString("0000", CultureInfo.InvariantCulture),
            "YY" => (t.Year % 100).ToString("00", CultureInfo.InvariantCulture),
            "MM" => t.Month.ToString("00", CultureInfo.InvariantCulture),
            "DD" => t.Day.ToString("00", CultureInfo.InvariantCulture),
            "hh" => t.Hour.ToString("00", CultureInfo.InvariantCulture),
            "mm" => t.Minute.ToString("00", CultureInfo.InvariantCulture),
            "ss" => t.Second.ToString("00", CultureInfo.InvariantCulture),
            "group" => Slugify(context.GroupName),
            "doc" => context.DocumentSequence.ToString(CultureInfo.InvariantCulture),
            "page" => context.PageSequence.ToString(CultureInfo.InvariantCulture),
            "barcode" => "", // reserved for phase 10 barcode work
            _ => $"$({token})", // unknown tokens pass through so typos are visible
        };
    }

    /// <summary>Substituted values must never break the file name; the pattern itself is the user's choice.</summary>
    private static string Slugify(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = value.Select(c => invalid.Contains(c) ? '-' : c).ToArray();
        return new string(chars).Trim();
    }
}
