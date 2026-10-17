using System.Security.Cryptography;

namespace FgScanner.Core.Station;

/// <summary>Where one station's data lies on disk, and what to stamp on its manifest.</summary>
public sealed record StationSource(
    string Station,
    string Database,
    string EvidenceRoot,
    string Trash,
    string AppVersion,
    string? LastMigration,
    string? BasedOn,
    DateTimeOffset SentAt);

/// <summary>Hashes a station's database, evidence root and trash into a manifest.</summary>
public static class StationManifestBuilder
{
    private static readonly string[] ImageExtensions = [".jpg", ".jpeg", ".png", ".tif", ".tiff", ".bmp", ".pdf"];

    // Hidden and system files are evidence too when they sit in a group folder; skipping them by
    // default would let a file leave Jim's laptop unaccounted for.
    internal static readonly EnumerationOptions AllFiles = new()
    {
        RecurseSubdirectories = true,
        AttributesToSkip = FileAttributes.None,
        IgnoreInaccessible = false,
    };

    public static StationManifest Build(StationSource source)
    {
        if (!File.Exists(source.Database))
        {
            throw new FileNotFoundException("The station has no database.", source.Database);
        }

        if (!Directory.Exists(source.EvidenceRoot))
        {
            throw new DirectoryNotFoundException($"The evidence folder \"{source.EvidenceRoot}\" does not exist.");
        }

        var files = Hash(source.EvidenceRoot, StationManifest.EvidenceFolder)
            .Concat(Directory.Exists(source.Trash) ? Hash(source.Trash, StationManifest.TrashFolder) : [])
            .OrderBy(f => f.Rel, StringComparer.Ordinal)
            .ToList();

        return new StationManifest(
            StationManifest.CurrentFormat,
            source.Station,
            StationManifest.FormatSentAt(source.SentAt),
            source.AppVersion,
            source.LastMigration,
            Sha256Of(source.Database),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(source.EvidenceRoot)),
            source.BasedOn,
            files);
    }

    private static IEnumerable<StationManifestFile> Hash(string root, string prefix)
    {
        foreach (var path in FilesUnder(root))
        {
            var rel = prefix + "/" + RelativeOf(root, path);
            yield return new StationManifestFile(rel, new FileInfo(path).Length, Sha256Of(path), KindOf(prefix, rel));
        }
    }

    /// <summary>
    /// Every file under <paramref name="root"/>, refusing any link on the way (Revision D, P10-8):
    /// a junction or symlink inside a station or a pack would make pack ship, verify read and
    /// apply move files that live somewhere else entirely.
    /// </summary>
    /// <exception cref="InvalidDataException">A junction or symbolic link was found.</exception>
    internal static IEnumerable<string> FilesUnder(string root)
    {
        foreach (var entry in new DirectoryInfo(root).EnumerateFileSystemInfos("*", AllFiles))
        {
            if (entry.LinkTarget is not null)
            {
                throw new InvalidDataException($"{entry.FullName} is a link to {entry.LinkTarget}; links are not allowed");
            }

            if (entry is FileInfo)
            {
                yield return entry.FullName;
            }
        }
    }

    internal static string RelativeOf(string root, string path) =>
        Path.GetRelativePath(root, path).Replace('\\', '/');

    private static string KindOf(string prefix, string rel)
    {
        if (prefix == StationManifest.TrashFolder)
        {
            return "trash";
        }

        if (rel.Split('/').Contains("originals", StringComparer.OrdinalIgnoreCase))
        {
            return "original";
        }

        var name = rel[(rel.LastIndexOf('/') + 1)..];
        if (name.StartsWith("index.", StringComparison.OrdinalIgnoreCase)
            || name.Equals("manifest.json", StringComparison.OrdinalIgnoreCase))
        {
            return "index";
        }

        return ImageExtensions.Contains(Path.GetExtension(name), StringComparer.OrdinalIgnoreCase)
            ? "image"
            : "sidecar";
    }

    public static string Sha256Of(string path)
    {
        // Shared for writing: the Back up button hashes the database the open app holds for writing
        // (SPEC-SCANNER-2026-011). What a pack records is the hash of its own copy, and every pack
        // is re-verified after it is written, so a file changing here is caught there.
        using var stream = new FileStream(
            path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1 << 20, FileOptions.SequentialScan);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }
}
