namespace FgScanner.Core.Station;

/// <summary>
/// Where one station keeps everything it writes, resolved once at startup. The default is
/// where 0.5.x-0.7.x always wrote; <c>--data-dir</c> lets a second station — Jim's, processed
/// on Franz's NucBox (SPEC-SCANNER-2026-010) — live beside it without sharing a database,
/// trash, recovery session or index drafts. The Gemini key stays in Credential Manager and the
/// single-instance lock stays machine-wide: one station open at a time.
/// </summary>
public sealed record StationPaths
{
    private const string Flag = "--data-dir";

    private StationPaths(string root, string logs, string indexPackages, bool isDefault)
    {
        Root = root;
        Logs = logs;
        IndexPackages = indexPackages;
        IsDefault = isDefault;
    }

    public string Root { get; }

    public string Logs { get; }

    public string IndexPackages { get; }

    public bool IsDefault { get; }

    /// <summary>The workspace's folder name, shown in the title bar so Franz can see whose
    /// station is open; null for the machine's own station.</summary>
    public string? Name => IsDefault ? null : Path.GetFileName(Root);

    public string Database => Path.Combine(Root, "fgscanner.db");

    public string Trash => Path.Combine(Root, "trash");

    public string Recovery => Path.Combine(Root, "recovery");

    public string IndexDrafts => Path.Combine(Root, "index-drafts");

    public string Tessdata => Path.Combine(Root, "tessdata");

    private static string DefaultRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FGScanner");

    private static string DefaultLocalRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FGScanner");

    public static StationPaths Default => Resolve(null);

    public static StationPaths Resolve(string? dataDir)
    {
        var root = dataDir is null
            ? DefaultRoot
            : Path.TrimEndingDirectorySeparator(Path.GetFullPath(dataDir));

        // Logs and extracted packages have always lived under Local AppData; a workspace keeps
        // them inside itself so deleting the workspace leaves nothing of Jim's behind.
        return string.Equals(root, DefaultRoot, StringComparison.OrdinalIgnoreCase)
            ? new StationPaths(DefaultRoot, Path.Combine(DefaultLocalRoot, "logs"),
                Path.Combine(DefaultLocalRoot, "index-packages"), isDefault: true)
            : new StationPaths(root, Path.Combine(root, "logs"),
                Path.Combine(root, "index-packages"), isDefault: false);
    }

    /// <summary>The folder after <c>--data-dir</c> (or <c>--data-dir=</c>), or null when the flag
    /// is absent. A flag with no folder is refused rather than read as the default station —
    /// that would open the wrong station's evidence.</summary>
    public static string? DataDirFromArgs(IReadOnlyList<string> args)
    {
        for (var i = 0; i < args.Count; i++)
        {
            if (args[i] == Flag)
            {
                return i + 1 < args.Count && !args[i + 1].StartsWith("--", StringComparison.Ordinal)
                    && !string.IsNullOrWhiteSpace(args[i + 1])
                    ? args[i + 1]
                    : throw new ArgumentException($"{Flag} needs a folder.", nameof(args));
            }

            if (args[i].StartsWith(Flag + "=", StringComparison.Ordinal))
            {
                var value = args[i][(Flag.Length + 1)..];
                return string.IsNullOrWhiteSpace(value)
                    ? throw new ArgumentException($"{Flag} needs a folder.", nameof(args))
                    : value;
            }
        }

        return null;
    }
}
