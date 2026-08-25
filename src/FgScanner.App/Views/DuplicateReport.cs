using System.IO;

namespace FgScanner.App.Views;

/// <summary>
/// The service already returns which files were skipped as duplicates; the UI reported only a count,
/// so the user had no way to learn which ones (slice 1, docs/roadmap-v0.2.md).
/// </summary>
internal static class DuplicateReport
{
    private const int MaxNamesShown = 5;

    public static string Format(IReadOnlyList<string> duplicateSourceFiles)
    {
        if (duplicateSourceFiles.Count == 0)
        {
            return "";
        }

        var names = string.Join(", ", duplicateSourceFiles.Take(MaxNamesShown).Select(Path.GetFileName));
        var more = duplicateSourceFiles.Count > MaxNamesShown
            ? $" and {duplicateSourceFiles.Count - MaxNamesShown} more"
            : "";
        return $" {duplicateSourceFiles.Count} duplicate(s) skipped"
            + $" (same content already in this group): {names}{more}.";
    }
}
