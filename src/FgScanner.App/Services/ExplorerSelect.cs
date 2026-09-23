using System.Diagnostics;
using System.IO;

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

    /// <summary>
    /// The real Explorer, by full path. Started by bare name with UseShellExecute false, Windows
    /// searches the calling process's own directory first — and FG Scanner is registered as an
    /// Open-With handler for images and PDFs, so that directory can be whichever folder the
    /// operator opened a scan from.
    /// </summary>
    public static string Executable { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");

    public static bool Reveal(string path)
    {
        using var started = Process.Start(new ProcessStartInfo
        {
            FileName = Executable,
            Arguments = Arguments(path),
            UseShellExecute = false,
        });
        return true;
    }
}
