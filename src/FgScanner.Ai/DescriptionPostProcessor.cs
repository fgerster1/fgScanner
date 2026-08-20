using System.Text.RegularExpressions;

namespace FgScanner.Ai;

/// <summary>Normalizes and hard-limits model output (PLAN §5.6: code enforces what prompts only aim at).</summary>
public static partial class DescriptionPostProcessor
{
    public const int MaxLength = 1000;

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRuns();

    public static bool IsBlankSentinel(string? text) =>
        string.Equals(text?.Trim(), DescriptionPrompt.BlankPageSentinel, StringComparison.OrdinalIgnoreCase);

    /// <summary>Collapses whitespace, then truncates at the last sentence boundary within 1000 chars
    /// (falling back to a word boundary + ellipsis when no sentence fits).</summary>
    public static string Normalize(string text)
    {
        var collapsed = WhitespaceRuns().Replace(text, " ").Trim();
        if (collapsed.Length <= MaxLength)
        {
            return collapsed;
        }

        var window = collapsed[..MaxLength];
        var lastSentenceEnd = window.LastIndexOfAny(['.', '!', '?']);
        if (lastSentenceEnd > 0)
        {
            return window[..(lastSentenceEnd + 1)];
        }

        var lastSpace = window.LastIndexOf(' ');
        return lastSpace > 0 ? window[..lastSpace] + "…" : window;
    }
}
