using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace FgScanner.Core.Station;

/// <summary>A finished backup (or the packer's refusal), the old backups it cleared away, and the
/// damaged ones it left for Franz.</summary>
public sealed record StationBackupResult(StationPackResult Pack, IReadOnlyList<string> Removed, IReadOnlyList<string> Damaged)
{
    public StationBackupResult(StationPackResult pack, IReadOnlyList<string> removed)
        : this(pack, removed, [])
    {
    }
}

/// <summary>
/// A whole, checked copy of a station on Jim's USB stick (SPEC-SCANNER-2026-011): a
/// <see cref="StationPacker.BackupKind"/> pack, then the oldest of this station's backups beyond
/// <c>keep</c> removed — only once the new one is written, so a failed backup never costs a
/// good one.
/// </summary>
/// <param name="driveFormat">The file system of the target's drive ("FAT32", "exFAT", "NTFS"), or null.</param>
/// <param name="fat32Limit">The largest file FAT32 holds; a parameter only so a test need not write 4 GB.</param>
/// <param name="check">A last word on the written backup, before any old one is cleared; its
/// reason refuses the backup, which is then removed.</param>
public sealed partial class StationBackup(
    StationPacker packer,
    Func<string, string?> driveFormat,
    long fat32Limit = StationBackup.Fat32FileLimit,
    Func<string, string?>? check = null)
{
    public const long Fat32FileLimit = uint.MaxValue;

    public const int DefaultKeep = 3;

    /// <param name="recordAsLast">False for a test copy sent to Franz: "Last backup" speaks only of
    /// Jim's own stick.</param>
    public StationBackupResult Run(StationPackRequest request, int keep = DefaultKeep, bool recordAsLast = true)
    {
        request = request with { Kind = StationPacker.BackupKind };
        if (TooBigForFat32(request) is { } refusal)
        {
            return new StationBackupResult(StationPackResult.Refused(refusal), []);
        }

        var pack = packer.Pack(request);
        if (pack.Problem is not null)
        {
            return new StationBackupResult(pack, []);
        }

        string? refused;
        try
        {
            refused = LateFile(request.Source, pack.PackFolder!) ?? check?.Invoke(pack.PackFolder!);
        }
        catch (Exception ex)
        {
            // A check that could not run is no check: the pack would later count as a good backup
            // (Revision D, RR-6).
            refused = $"the backup could not be checked ({ex.Message}) — back up again";
        }

        if (refused is not null)
        {
            Directory.Delete(pack.PackFolder!, recursive: true);
            return new StationBackupResult(StationPackResult.Refused(refused), []);
        }

        if (recordAsLast)
        {
            LastBackupRecord.Write(request.StationRoot, new LastBackupRecord(pack.PackFolder!, pack.Code!, request.Source.SentAt));
        }

        var damaged = new List<string>();
        var removed = RemoveOld(request.Destination, request.Source.Station, pack.PackFolder!, Math.Max(1, keep), damaged);
        return new StationBackupResult(pack, removed, damaged);
    }

    /// <summary>
    /// A file on the station now that the backup lacks: written while it was copied — a commit's
    /// index, an archived original, a page (Revision D, RR-2). The manifest lists the station as it
    /// was before copying, so anything newer is missing from the backup, and restoring it would
    /// bring back a station without that file.
    /// </summary>
    private static string? LateFile(StationSource source, string pack)
    {
        var packed = StationManifest.Parse(File.ReadAllBytes(Path.Combine(pack, StationManifest.FileName)))
            .Files.Select(f => f.Rel).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var (root, prefix) in new[] { (source.EvidenceRoot, StationManifest.EvidenceFolder), (source.Trash, StationManifest.TrashFolder) })
        {
            if (!Directory.Exists(root))
            {
                continue;
            }

            foreach (var path in StationManifestBuilder.FilesUnder(root))
            {
                if (!packed.Contains(prefix + "/" + StationManifestBuilder.RelativeOf(root, path)))
                {
                    return $"the station changed while it was copied ({path}) — back up again";
                }
            }
        }

        return null;
    }

    /// <summary>
    /// A stick formatted FAT32 cannot hold a file of 4 GB or more; finding that out half-way
    /// through leaves a copy that is not a backup, so it is checked before anything is copied.
    /// </summary>
    private string? TooBigForFat32(StationPackRequest request)
    {
        if (!string.Equals(driveFormat(request.Destination), "FAT32", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var source = request.Source;
        var files = new[] { source.Database }
            .Concat(Directory.Exists(source.EvidenceRoot) ? Directory.EnumerateFiles(source.EvidenceRoot, "*", SearchOption.AllDirectories) : [])
            .Concat(Directory.Exists(source.Trash) ? Directory.EnumerateFiles(source.Trash, "*", SearchOption.AllDirectories) : []);
        var big = files.Select(f => new FileInfo(f)).FirstOrDefault(f => f.Exists && f.Length > fat32Limit);
        return big is null
            ? null
            : $"the stick is formatted FAT32, which cannot hold {big.FullName} " +
              $"({(big.Length / (double)(1L << 30)).ToString("N1", CultureInfo.InvariantCulture)} GB) — reformat it as exFAT";
    }

    /// <summary>
    /// Counts only this station's complete backups. A folder with no manifest was cut short (the
    /// stick was pulled) and is removed; one whose manifest names files that are missing is
    /// damaged — kept for Franz to look at, but never counted as one of the backups.
    /// </summary>
    private static List<string> RemoveOld(string destination, string station, string made, int keep, List<string> damaged)
    {
        var madeAt = Directory.GetCreationTimeUtc(made);
        var complete = new List<(string Folder, string SentAt)>();
        var removed = new List<string>();
        foreach (var folder in Directory.EnumerateDirectories(destination))
        {
            var name = Path.GetFileName(folder);
            if (!name.StartsWith(station + "-", StringComparison.Ordinal) || !BackupName().IsMatch(name[(station.Length + 1)..])
                || string.Equals(Path.GetFullPath(folder), Path.GetFullPath(made), StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var manifestPath = Path.Combine(folder, StationManifest.FileName);
            if (!File.Exists(manifestPath))
            {
                // Only one cut short before this backup began; anything newer is not ours to judge.
                if (Directory.GetCreationTimeUtc(folder) <= madeAt && TryDelete(folder))
                {
                    removed.Add(folder);
                }

                continue;
            }

            if (CompleteSentAt(folder, manifestPath) is { } sentAt)
            {
                complete.Add((folder, sentAt));
            }
            else
            {
                damaged.Add(folder);
            }
        }

        // The backup just made is one of the ones kept.
        foreach (var (folder, _) in complete
                     .OrderByDescending(b => b.SentAt, StringComparer.Ordinal)
                     .ThenByDescending(b => b.Folder, StringComparer.Ordinal)
                     .Skip(keep - 1))
        {
            if (TryDelete(folder))
            {
                removed.Add(folder);
            }
        }

        return removed;
    }

    /// <summary>
    /// Whether every file the manifest names is there at its size — not re-hashed: reading back
    /// three old backups on every run would take minutes on a stick, and each was hash-checked
    /// when it was written.
    /// </summary>
    private static string? CompleteSentAt(string folder, string manifestPath)
    {
        try
        {
            var manifest = StationManifest.Parse(File.ReadAllBytes(manifestPath));
            var db = new FileInfo(Path.Combine(folder, StationManifest.DatabaseFileName));
            return db.Exists && manifest.Files.All(f =>
                f.From is null && new FileInfo(Path.Combine(folder, f.Rel)) is { Exists: true } file && file.Length == f.Size)
                ? manifest.SentAt
                : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    private static bool TryDelete(string folder)
    {
        try
        {
            Directory.Delete(folder, recursive: true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Left for the next backup to clear; the new backup is already safely written.
            return false;
        }
    }

    [GeneratedRegex(@"^\d{4}-\d{2}-\d{2}-\d{4}(-\d+)?-backup$", RegexOptions.CultureInvariant)]
    private static partial Regex BackupName();
}

/// <summary>
/// When this station last made a backup and where (<c>last-backup.json</c> in the station
/// folder), so the "Last backup" line works with the stick unplugged. Information only: nothing
/// restores or decides anything from it.
/// </summary>
public sealed record LastBackupRecord(string Folder, string Code, DateTimeOffset At)
{
    public const string FileName = "last-backup.json";

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    public static LastBackupRecord? Read(string stationRoot)
    {
        var path = Path.Combine(stationRoot, FileName);
        try
        {
            return File.Exists(path) ? JsonSerializer.Deserialize<LastBackupRecord>(File.ReadAllBytes(path), Json) : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static void Write(string stationRoot, LastBackupRecord record) =>
        StationPacker.WriteAtomically(Path.Combine(stationRoot, FileName), JsonSerializer.SerializeToUtf8Bytes(record, Json));
}
