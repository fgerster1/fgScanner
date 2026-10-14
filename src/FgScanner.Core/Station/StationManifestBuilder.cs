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
        foreach (var path in Directory.EnumerateFiles(root, "*", AllFiles))
        {
            var rel = prefix + "/" + RelativeOf(root, path);
            yield return new StationManifestFile(rel, new FileInfo(path).Length, Sha256Of(path), KindOf(prefix, rel));
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

    internal static string Sha256Of(string path)
    {
        using var stream = new FileStream(
            path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20, FileOptions.SequentialScan);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }
}
