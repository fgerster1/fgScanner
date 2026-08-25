namespace FgScanner.Core.Duplicates;

/// <summary>
/// Similarity between two OCR results, as a fraction from 0 to 1.
///
/// Token-set Jaccard rather than edit distance: OCR of the same page twice differs by scattered
/// character errors and reflowed whitespace, which edit distance punishes heavily while token
/// overlap barely notices. It is also linear rather than quadratic, so comparing every pair of
/// pages in a group stays cheap.
/// </summary>
public static class TextSimilarity
{
    /// <summary>
    /// Texts shorter than this are not compared at all. Two nearly-empty pages share their handful
    /// of tokens and score 1.0, which would report every blank page as a duplicate of every other.
    /// </summary>
    public const int MinimumTokens = 10;

    public static IReadOnlySet<string> Tokenize(string? text)
    {
        var tokens = new HashSet<string>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(text))
        {
            return tokens;
        }

        var current = new System.Text.StringBuilder();
        foreach (var ch in text)
        {
            if (char.IsLetterOrDigit(ch))
            {
                current.Append(char.ToLowerInvariant(ch));
            }
            else if (current.Length > 0)
            {
                tokens.Add(current.ToString());
                current.Clear();
            }
        }

        if (current.Length > 0)
        {
            tokens.Add(current.ToString());
        }

        return tokens;
    }

    /// <summary>
    /// Jaccard overlap of the two token sets, or null when either side is too short to judge.
    /// Null means "cannot say", which callers must not treat as zero.
    /// </summary>
    public static double? Compare(string? left, string? right)
    {
        var a = Tokenize(left);
        var b = Tokenize(right);
        if (a.Count < MinimumTokens || b.Count < MinimumTokens)
        {
            return null;
        }

        var intersection = a.Count(b.Contains);
        var union = a.Count + b.Count - intersection;
        return union == 0 ? null : (double)intersection / union;
    }
}
