using System.Globalization;

namespace FgScanner.Core.Station;

/// <summary>What to pack, and the station-state facts only the caller can read from the database.</summary>
/// <param name="BasedOnManifest">The manifest of the last pack the receiver applied or verified;
/// its files are what the receiver already holds. Null for a first pack, which ships everything.</param>
/// <param name="Kind"><c>send</c> (Jim to Franz), <c>return</c> (Franz to Jim) or <c>backup</c> (a
/// whole, checked copy to a USB stick or Franz's test folder, SPEC-SCANNER-2026-011) — names the folder.</param>
/// <param name="BasedOnContent">The fingerprint of the database the receiver holds — what its apply
/// compares its own rows against. Null for a first pack.</param>
/// <param name="LiveContent">This station's own fingerprint now, kept in the freeze record.</param>
public sealed record StationPackRequest(
    StationSource Source,
    string StationRoot,
    string Destination,
    string? BasedOnManifest,
    string Kind,
    int PendingReadingJobs,
    StationContent? BasedOnContent = null,
    StationContent? LiveContent = null,
    IReadOnlyList<StationPackReport>? Reports = null);

/// <summary>A report to put at the pack's top level; its name must be one of
/// <see cref="StationManifest.ReportNames"/>.</summary>
public sealed record StationPackReport(string Name, byte[] Content);

/// <summary>A finished pack (<see cref="Problem"/> null) or the reason none was made.</summary>
public sealed record StationPackResult(
    string? PackFolder,
    string? Code,
    int ShippedFiles,
    long ShippedBytes,
    string? Problem)
{
    public static StationPackResult Refused(string problem) => new(null, null, 0, 0, problem);
}

/// <summary>
/// Packs a station into a new folder of the transport (SPEC-SCANNER-2026-010 §08): the database,
/// the files the receiver lacks, and a manifest of the whole final state, written last. A pack is
/// only taken from a station at rest — the pack <em>is</em> Jim's backup, so a state with a scan
/// in hand or a transaction half-written would be a backup of something that never existed.
/// </summary>
/// <param name="isAppOpen">FG Scanner's single-instance lock is held.</param>
/// <param name="copyDatabase">Copies the database consistently (SQLite's backup API in the CLI).</param>
/// <param name="freeBytes">Free space on the destination's drive.</param>
/// <param name="onFileWritten">Observes each evidence or manifest file as it lands (tests).</param>
/// <param name="restProblem">Replaces the closed-station check for a caller that is the open app
/// itself (the Back up button, SPEC-SCANNER-2026-011): its database is open in WAL mode and its
/// live scan session keeps an index, so "closed cleanly" can never hold — it answers instead.</param>
public sealed class StationPacker(
    Func<bool> isAppOpen,
    Action<string, string> copyDatabase,
    Func<string, long> freeBytes,
    Action<string>? onFileWritten = null,
    Func<string?>? restProblem = null)
{
    public const string BackupKind = "backup";

    /// <summary>Drive's local cache holds the pack until it uploads, so it needs this much spare.</summary>
    private const long Headroom = 1L << 30;

    public StationPackResult Pack(StationPackRequest request)
    {
        var source = request.Source;
        if (RefusalOf(request) is { } refusal)
        {
            return StationPackResult.Refused(refusal);
        }

        string? basedOnHash = null;
        StationManifest? basedOn = null;
        if (request.BasedOnManifest is not null)
        {
            var bytes = File.ReadAllBytes(request.BasedOnManifest);
            basedOn = StationManifest.Parse(bytes);
            basedOnHash = StationManifest.HashOf(bytes);
        }

        var liveDbSha256 = StationManifestBuilder.Sha256Of(source.Database);
        StationManifest full;
        try
        {
            full = StationManifestBuilder.Build(source with { BasedOn = basedOnHash });
        }
        catch (InvalidDataException ex)
        {
            return StationPackResult.Refused(ex.Message);
        }

        var manifest = full with { Files = Plan(full.Files, basedOn), BasedOnContent = request.BasedOnContent };
        var shipped = manifest.Files.Where(f => f.From is null).ToList();
        var bytesNeeded = new FileInfo(source.Database).Length + shipped.Sum(f => f.Size);
        var free = freeBytes(request.Destination);
        if (free < bytesNeeded + Headroom)
        {
            return StationPackResult.Refused(
                $"not enough free space at {request.Destination} (the pack needs {Megabytes(bytesNeeded + Headroom)}, " +
                $"there is {Megabytes(free)})");
        }

        var pack = CreatePackFolder(request.Destination, source.Station, source.SentAt, request.Kind);
        try
        {
            var packedDb = Path.Combine(pack, StationManifest.DatabaseFileName);
            copyDatabase(source.Database, packedDb);
            foreach (var file in shipped)
            {
                var target = Path.Combine(pack, file.Rel);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(SourcePathOf(source, file.Rel), target);
                onFileWritten?.Invoke(target);
            }

            var reports = new List<StationManifestReport>();
            foreach (var report in request.Reports ?? [])
            {
                var path = Path.Combine(pack, report.Name);
                WriteAtomically(path, report.Content);
                onFileWritten?.Invoke(path);
                reports.Add(new StationManifestReport(report.Name, report.Content.Length, StationManifestBuilder.Sha256Of(path)));
            }

            // The backup API writes its own bytes, so the manifest names the copy that travels;
            // the live database's hash goes into the freeze record instead.
            manifest = manifest with
            {
                DbSha256 = StationManifestBuilder.Sha256Of(packedDb),
                Reports = reports.Count == 0 ? null : reports,
            };
            var manifestBytes = manifest.ToJsonBytes();
            var manifestPath = Path.Combine(pack, StationManifest.FileName);
            WriteAtomically(manifestPath, manifestBytes);
            onFileWritten?.Invoke(manifestPath);

            var verified = StationManifestVerifier.Verify(pack);
            if (verified.Status != StationVerifyStatus.Ok)
            {
                Directory.Delete(pack, recursive: true);
                return StationPackResult.Refused(
                    $"the pack did not verify after copying ({string.Join("; ", verified.Problems)}) — pack again");
            }

            // A backup is never what the other station holds: recording it as the baseline would
            // refuse Franz's next return as "not made from this station's last pack".
            if (request.Kind != BackupKind)
            {
                StationFreezeRecord.Write(request.StationRoot, new StationFreezeRecord(
                    Path.GetFileName(pack), verified.Code!, manifest.SentAt, liveDbSha256,
                    StationManifest.HashOf(manifestBytes), request.LiveContent), manifestBytes);
            }

            return new StationPackResult(pack, verified.Code, shipped.Count, shipped.Sum(f => f.Size), null);
        }
        catch
        {
            // A half-copied pack with no manifest would read as "still arriving" forever.
            Directory.Delete(pack, recursive: true);
            throw;
        }
    }

    private string? RefusalOf(StationPackRequest request)
    {
        var source = request.Source;
        if ((restProblem is null ? StationRest.Problem(isAppOpen, source.Database, request.StationRoot, "pack") : restProblem()) is { } notAtRest)
        {
            return notAtRest;
        }

        // Nothing done in a test copy may reach the station it came from (SPEC-SCANNER-2026-011 Q6).
        if (request.Kind != BackupKind && TestCopyMarker.IsTestCopy(request.StationRoot))
        {
            return "this is a test copy — it can never be sent or returned";
        }

        // A backup must restore on its own; a delta needs the pack it was based on, which may be
        // the one that is lost.
        if (request.Kind == BackupKind && request.BasedOnManifest is not null)
        {
            return "a backup is always the whole station, never based on another pack";
        }

        if ((request.Reports ?? []).FirstOrDefault(r => !StationManifest.ReportNames.Contains(r.Name, StringComparer.Ordinal)) is { } unknown)
        {
            return $"\"{unknown.Name}\" is not a report a pack carries";
        }

        if (request.PendingReadingJobs > 0)
        {
            return request.PendingReadingJobs == 1
                ? "1 page is still waiting to be read — let reading finish, then pack again"
                : $"{request.PendingReadingJobs.ToString(CultureInfo.InvariantCulture)} pages are still waiting to be read — let reading finish, then pack again";
        }

        // A pack inside a live folder would be packed into the next pack, and a sync client
        // watching it could carry a deletion back into the station (§03 C2).
        // Links resolved first: a folder that is a junction into the evidence is inside it, whatever
        // its name says (SPEC-SCANNER-2026-011 Revision C, SR-6).
        var destination = Path.GetFullPath(request.Destination);
        var resolved = Resolved(destination);
        foreach (var live in new[] { request.StationRoot, source.EvidenceRoot })
        {
            if (IsInside(destination, Path.GetFullPath(live)) || IsInside(resolved, Resolved(Path.GetFullPath(live))))
            {
                return $"the pack destination is inside the station's own folders: {destination}";
            }
        }

        return Directory.Exists(destination) ? null : $"the folder \"{destination}\" does not exist";
    }

    /// <summary>
    /// Marks every file the receiver already holds — at the same rel, or under another name — so
    /// only new and changed bytes travel. The same rel wins over a rename so an unchanged file is
    /// never reported as moved.
    /// </summary>
    internal static List<StationManifestFile> Plan(
        IReadOnlyList<StationManifestFile> files, StationManifest? basedOn)
    {
        if (basedOn is null)
        {
            return [.. files];
        }

        var held = basedOn.Files.ToDictionary(f => f.Rel, f => f.Sha256, StringComparer.Ordinal);
        var byHash = basedOn.Files
            .GroupBy(f => f.Sha256, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Select(f => f.Rel).Order(StringComparer.Ordinal).First(),
                StringComparer.Ordinal);
        return [.. files.Select(f =>
            held.TryGetValue(f.Rel, out var sha) && sha == f.Sha256 ? f with { From = f.Rel }
            : byHash.TryGetValue(f.Sha256, out var from) ? f with { From = from }
            : f)];
    }

    private static string CreatePackFolder(string destination, string station, DateTimeOffset sentAt, string kind)
    {
        var stamp = sentAt.ToString("yyyy-MM-dd-HHmm", CultureInfo.InvariantCulture);
        for (var n = 1; ; n++)
        {
            var name = n == 1 ? $"{station}-{stamp}-{kind}" : $"{station}-{stamp}-{n.ToString(CultureInfo.InvariantCulture)}-{kind}";
            var path = Path.Combine(destination, name);
            if (!Directory.Exists(path) && !File.Exists(path))
            {
                // Never written twice: Drive turns a re-sent name into "name (1)" (§04).
                Directory.CreateDirectory(path);
                return path;
            }
        }
    }

    private static string SourcePathOf(StationSource source, string rel)
    {
        var slash = rel.IndexOf('/', StringComparison.Ordinal);
        var root = rel[..slash] == StationManifest.TrashFolder ? source.Trash : source.EvidenceRoot;
        return Path.Combine(root, rel[(slash + 1)..].Replace('/', Path.DirectorySeparatorChar));
    }

    /// <summary>The path with every junction or symbolic link along it followed to its target.</summary>
    private static string Resolved(string path, int depth = 0)
    {
        var tail = new Stack<string>();
        for (var current = new DirectoryInfo(Path.GetFullPath(path)); current is not null; current = current.Parent)
        {
            if (depth < 16 && current.Exists && current.LinkTarget is not null
                && current.ResolveLinkTarget(returnFinalTarget: true) is { } target)
            {
                return Path.Combine([Resolved(target.FullName, depth + 1), .. tail]);
            }

            tail.Push(current.Name);
        }

        return Path.GetFullPath(path);
    }

    private static bool IsInside(string path, string folder)
    {
        var root = Path.TrimEndingDirectorySeparator(folder) + Path.DirectorySeparatorChar;
        return string.Equals(Path.TrimEndingDirectorySeparator(path), Path.TrimEndingDirectorySeparator(folder),
                StringComparison.OrdinalIgnoreCase)
            || path.StartsWith(root, StringComparison.OrdinalIgnoreCase);
    }

    internal static void WriteAtomically(string path, byte[] bytes)
    {
        var temp = path + ".tmp";
        File.WriteAllBytes(temp, bytes);
        File.Move(temp, path, overwrite: true);
    }

    private static string Megabytes(long bytes) =>
        (bytes / (double)(1L << 20)).ToString("N0", CultureInfo.InvariantCulture) + " MB";
}
