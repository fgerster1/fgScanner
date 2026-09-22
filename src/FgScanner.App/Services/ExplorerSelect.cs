using System.Diagnostics;

namespace FgScanner.App.Services;

/// <summary>Opens Explorer with one file selected — the send fallback and the Groups page share it.</summary>
public static class ExplorerSelect
{
    /// <summary>
    /// Always quoted, with no space after the comma. Explorer splits /select's argument on commas,
    /// and ArgumentList quoted the path only when it held a space — so "Smith,John.pdf", a file
    /// named after an ordinary subject, reached Explorer as two arguments and opened the wrong
    /// place. A Windows path cannot contain a quote, so quoting needs no escaping.
    /// </summary>
    public static string Arguments(string path) => $"/select,\"{path}\"";

    public static bool Reveal(string path)
    {
        using var started = Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = Arguments(path),
            UseShellExecute = false,
        });
        return true;
    }
}
