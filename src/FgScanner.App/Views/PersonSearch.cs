using System.Globalization;
using System.Text;
using FgScanner.Core.IndexPackages;

namespace FgScanner.App.Views;

/// <summary>
/// Finds the people a few typed letters could mean (SPEC-2026-009 §08-2): any display name or alias
/// containing the text, capitals, accents and punctuation aside. It only lists — whether a typed
/// name becomes a person is decided by the typed-name path, which follows the portal's own rule
/// (ADR-0016). Every spelling is normalised once per package, so a keystroke is a plain scan.
/// </summary>
public sealed class PersonSearch
{
    /// <summary>More rows than anyone reads; past it the note says to keep typing.</summary>
    public const int MaxShown = 200;

    public sealed record Result(IReadOnlyList<PackagePerson> People, int Total);

    private readonly (PackagePerson Person, string[] Spellings)[] _index;

    public PersonSearch(IEnumerable<PackagePerson> people) =>
        _index = [.. people
            .OrderBy(p => p.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(p => p.Id, StringComparer.Ordinal)
            .Select(p => (p, p.Aliases.Prepend(p.DisplayName).Select(Normalise).ToArray()))];

    public Result Filter(string typed)
    {
        var wanted = Normalise(typed);
        if (wanted.Length == 0)
        {
            return new Result([], 0);
        }

        var hits = _index
            .Where(e => e.Spellings.Any(s => s.Contains(wanted, StringComparison.Ordinal)))
            .Select(e => e.Person)
            .ToList();
        return new Result(hits.Take(MaxShown).ToArray(), hits.Count);
    }

    /// <summary>Lower case, accents stripped, punctuation to spaces, spaces collapsed — so
    /// "Whitacre, Jason", "whitacre jason" and "Zoë" / "zoe" meet.</summary>
    private static string Normalise(string text)
    {
        var builder = new StringBuilder(text.Length);
        var pendingSpace = false;
        foreach (var c in text.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsLetterOrDigit(c))
            {
                if (pendingSpace && builder.Length > 0)
                {
                    builder.Append(' ');
                }

                pendingSpace = false;
                builder.Append(char.ToLowerInvariant(c));
            }
            else
            {
                pendingSpace = true;
            }
        }

        return builder.ToString();
    }
}
