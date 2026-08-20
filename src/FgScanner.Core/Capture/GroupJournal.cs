using System.Globalization;

namespace FgScanner.Core.Capture;

/// <summary>
/// Append-only journal.txt in the group directory: dropped pages, separator hits, commit-hook
/// outcomes. Research-5 C2: "always journal dropped pages" — the file is the user's audit trail
/// for anything the pipeline did without asking.
/// </summary>
public static class GroupJournal
{
    public const string FileName = "journal.txt";

    public static async Task AppendAsync(
        string groupDirectory, string message, CancellationToken cancellationToken = default)
    {
        var line = string.Create(
            CultureInfo.InvariantCulture, $"{DateTime.UtcNow:yyyy-MM-dd'T'HH:mm:ss'Z'}  {message}{Environment.NewLine}");
        await File.AppendAllTextAsync(
            Path.Combine(groupDirectory, FileName), line, cancellationToken).ConfigureAwait(false);
    }
}
