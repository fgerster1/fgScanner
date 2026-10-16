using System.Globalization;
using System.Text.Json;

namespace FgScanner.Core.Station;

/// <param name="StationRoot">The receiving station's folder: database, trash, recovery, freeze record.</param>
/// <param name="AppVersion">The version this station runs.</param>
/// <param name="LatestMigration">The newest migration this version knows.</param>
/// <param name="ReceiverMigration">The receiving database's own last migration; behind
/// <paramref name="LatestMigration"/> when the new version was installed but never started.</param>
public sealed record StationApplyRequest(
    string PackFolder,
    string StationRoot,
    string EvidenceRoot,
    string AppVersion,
    string LatestMigration,
    DateTimeOffset At,
    string? ReceiverMigration = null);

/// <summary>Applied (<see cref="Problem"/> null), still downloading, or refused with nothing changed.</summary>
public sealed record StationApplyResult(
    string? Code,
    string? Problem,
    bool StillArriving = false,
    string? BackupFolder = null)
{
    public static StationApplyResult Refused(string problem) => new(null, problem);
}

/// <summary>
/// Brings a station to the final state a pack's manifest describes (SPEC-SCANNER-2026-010 AC-5,
/// AC-6). Every gate runs before anything is touched; the new database and files are staged
/// beside the station first; replaced and deleted files are moved into <c>apply-backup-…</c>,
/// never erased, so <see cref="Undo"/> can put the station back byte for byte.
/// </summary>
public sealed class StationApplier(Func<bool> isAppOpen, IStationDatabase database, Func<string, long>? freeBytes = null)
{
    private const string BackupPrefix = "apply-backup-";

    private const string UndonePrefix = "apply-undone-";

    private const string UndoPlanFileName = "undo.json";

    /// <summary>The database an undo replaced, kept in the <c>apply-undone-</c> folder.</summary>
    private const string ReplacedDatabaseFileName = "fgscanner.replaced.db";

    private const long Headroom = 1L << 30;

    private static readonly string[] StationFiles =
    [
        StationManifest.DatabaseFileName, StationManifest.DatabaseFileName + "-wal", StationManifest.DatabaseFileName + "-shm",
        StationFreezeRecord.FileName, StationFreezeRecord.SentManifestFileName,
    ];

    public StationApplyResult Apply(StationApplyRequest request)
    {
        var db = Path.Combine(request.StationRoot, StationManifest.DatabaseFileName);
        var trash = Path.Combine(request.StationRoot, StationManifest.TrashFolder);
        if (StationRest.Problem(isAppOpen, db, request.StationRoot, "install the update") is { } notAtRest)
        {
            return StationApplyResult.Refused(notAtRest);
        }

        var verified = StationManifestVerifier.Verify(request.PackFolder, out var manifestBytes);
        if (verified.Status == StationVerifyStatus.StillArriving)
        {
            return new StationApplyResult(null, "the update is still arriving — wait for Drive, then try again", StillArriving: true);
        }

        if (verified.Status != StationVerifyStatus.Ok)
        {
            return StationApplyResult.Refused($"the update is damaged ({verified.Problems[0]}) — call Franz");
        }

        var manifest = StationManifest.Parse(manifestBytes!);
        if (GateProblem(request, manifest, db, trash) is { } refusal)
        {
            return StationApplyResult.Refused(refusal);
        }

        if (SpaceProblem(request, manifest) is { } noRoom)
        {
            return StationApplyResult.Refused(noRoom);
        }

        var stamp = request.At.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        var staging = UniqueFolder(request.StationRoot, "apply-staging-" + stamp);
        try
        {
            if (Stage(request, manifest, db, trash, staging) is { } stagingProblem)
            {
                Directory.Delete(staging, recursive: true);
                return StationApplyResult.Refused(stagingProblem);
            }
        }
        catch
        {
            Directory.Delete(staging, recursive: true);
            throw;
        }

        var backup = UniqueFolder(request.StationRoot, BackupPrefix + stamp);
        try
        {
            Commit(request, manifest, db, trash, staging, backup);
            if (!SameFiles(Hashes(request.EvidenceRoot, trash), manifest.Files))
            {
                throw new InvalidDataException("the station did not match the update after installing it");
            }

            StationFreezeRecord.Write(request.StationRoot, new StationFreezeRecord(
                Path.GetFileName(Path.TrimEndingDirectorySeparator(request.PackFolder)), verified.Code!, manifest.SentAt,
                StationManifestBuilder.Sha256Of(db), StationManifest.HashOf(manifestBytes!), database.Content(db)), manifestBytes!);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            // Whatever stopped it - a file another program holds, a full disk - Jim is left with
            // the station he had, never half of each.
            Restore(request.StationRoot, backup);
            Directory.Move(backup, Path.Combine(request.StationRoot, UndonePrefix + Path.GetFileName(backup)[BackupPrefix.Length..]));
            if (Directory.Exists(staging))
            {
                Directory.Delete(staging, recursive: true);
            }

            return StationApplyResult.Refused($"the update could not be installed ({ex.Message}), so the station has been put back — call Franz");
        }

        return new StationApplyResult(verified.Code, null, BackupFolder: backup);
    }

    /// <summary>
    /// Puts the station back as it was before the last apply - only while it is still exactly as
    /// that apply left it: undoing over work Jim has done since would destroy that work.
    /// </summary>
    public StationApplyResult Undo(string stationRoot)
    {
        var db = Path.Combine(stationRoot, StationManifest.DatabaseFileName);
        if (StationRest.Problem(isAppOpen, db, stationRoot, "undo") is { } notAtRest)
        {
            return StationApplyResult.Refused(notAtRest);
        }

        // A backup with no plan is one an interrupted apply never finished starting; the last
        // one with a plan is the apply to undo.
        var backup = Directory.Exists(stationRoot)
            ? Directory.EnumerateDirectories(stationRoot, BackupPrefix + "*")
                .Where(d => File.Exists(Path.Combine(d, UndoPlanFileName)))
                .Order(StringComparer.Ordinal).LastOrDefault()
            : null;
        if (backup is null)
        {
            return StationApplyResult.Refused("there is nothing to undo");
        }

        var plan = ReadPlan(backup);
        if (ChangedSinceApply(stationRoot, plan.EvidenceRoot, db) is { } changed)
        {
            return StationApplyResult.Refused(changed);
        }

        if (File.Exists(db))
        {
            File.Copy(db, Path.Combine(backup, ReplacedDatabaseFileName));
        }

        Restore(stationRoot, backup);
        Directory.Move(backup, Path.Combine(stationRoot, UndonePrefix + Path.GetFileName(backup)[BackupPrefix.Length..]));
        return new StationApplyResult(null, null, BackupFolder: backup);
    }

    private string? ChangedSinceApply(string stationRoot, string evidenceRoot, string db)
    {
        const string Changed = "this station has changed since the update was installed — call Franz";
        var freeze = StationFreezeRecord.Read(stationRoot);
        var sentPath = Path.Combine(stationRoot, StationFreezeRecord.SentManifestFileName);
        if (freeze?.Content is null || !File.Exists(sentPath) || !File.Exists(db))
        {
            return Changed;
        }

        try
        {
            if (database.ContentSha256(db, freeze.Content.Tables) != freeze.Content.Sha256)
            {
                return Changed;
            }
        }
        catch (InvalidDataException)
        {
            return Changed;
        }

        var applied = StationManifest.Parse(File.ReadAllBytes(sentPath)).Files;
        return SameFiles(Hashes(evidenceRoot, Path.Combine(stationRoot, StationManifest.TrashFolder)), applied) ? null : Changed;
    }

    /// <summary>
    /// Undoes one apply from its backup. A placed file is removed only when the original it
    /// replaced is safely in the backup, or when it replaced nothing: an apply stopped part-way
    /// may not have moved the original yet, and that original is then still the live file.
    /// </summary>
    private static void Restore(string stationRoot, string backup)
    {
        var plan = ReadPlan(backup);
        var trash = Path.Combine(stationRoot, StationManifest.TrashFolder);
        var movedAside = plan.MovedAside.ToHashSet(StringComparer.Ordinal);
        foreach (var rel in plan.Placed)
        {
            if (movedAside.Contains(rel) && !File.Exists(Path.Combine(backup, ToPath(rel))))
            {
                continue;
            }

            var placed = TargetOf(rel, plan.EvidenceRoot, trash);
            if (File.Exists(placed))
            {
                File.Delete(placed);
            }
        }

        foreach (var rel in plan.MovedAside)
        {
            var kept = Path.Combine(backup, ToPath(rel));
            if (File.Exists(kept))
            {
                var target = TargetOf(rel, plan.EvidenceRoot, trash);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Move(kept, target);
            }
        }

        foreach (var name in StationFiles)
        {
            var kept = Path.Combine(backup, name);
            var live = Path.Combine(stationRoot, name);
            if (File.Exists(kept))
            {
                File.Copy(kept, live + ".tmp", overwrite: true);
                File.Move(live + ".tmp", live, overwrite: true);
            }
            else if (!plan.Existed.Contains(name, StringComparer.Ordinal) && File.Exists(live))
            {
                // Not there before the apply, so the apply made it. A file that was there but has
                // no copy in the backup was never touched - the apply stopped before reaching it.
                File.Delete(live);
            }
        }
    }

    private static UndoPlan ReadPlan(string backup) =>
        JsonSerializer.Deserialize<UndoPlan>(File.ReadAllBytes(Path.Combine(backup, UndoPlanFileName)))!;

    private static bool SameFiles(Dictionary<string, string> station, IReadOnlyList<StationManifestFile> manifest) =>
        station.Count == manifest.Count
        && manifest.All(f => station.TryGetValue(f.Rel, out var sha) && sha == f.Sha256);

    private string? SpaceProblem(StationApplyRequest request, StationManifest manifest)
    {
        // Staging sits in the station folder; moving into an evidence folder on another drive
        // copies, so each drive must hold the whole update with room to spare.
        var needed = new FileInfo(Path.Combine(request.PackFolder, StationManifest.DatabaseFileName)).Length
            + manifest.Files.Where(f => f.From != f.Rel).Sum(f => f.Size) + Headroom;
        foreach (var drive in new[] { request.StationRoot, request.EvidenceRoot }
                     .Select(p => Path.GetPathRoot(Path.GetFullPath(p))!).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var free = (freeBytes ?? (root => new DriveInfo(root).AvailableFreeSpace))(drive);
            if (free < needed)
            {
                return $"not enough free space on {drive} (the update needs {Megabytes(needed)}, there is {Megabytes(free)}) — call Franz";
            }
        }

        return null;
    }

    private static string Megabytes(long bytes) =>
        (bytes / (double)(1L << 20)).ToString("N0", CultureInfo.InvariantCulture) + " MB";

    private string? GateProblem(StationApplyRequest request, StationManifest manifest, string db, string trash)
    {
        if (!string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(manifest.EvidenceRoot)),
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(request.EvidenceRoot)), StringComparison.OrdinalIgnoreCase))
        {
            // Every group in the database names its folder by full path; a different root would
            // leave the database pointing at folders that are not there.
            return $"the update keeps its evidence in {manifest.EvidenceRoot}; this station keeps it in {request.EvidenceRoot} — call Franz";
        }

        if (!File.Exists(db))
        {
            if (manifest.Files.Any(f => f.From is not null))
            {
                return "this update only fills in a station that already has the earlier data — call Franz";
            }

            return HoldsFiles(request.EvidenceRoot) || HoldsFiles(trash)
                ? $"the evidence folder {request.EvidenceRoot} already holds files, and a first update must start empty"
                : null;
        }

        if (manifest.AppVersion != request.AppVersion)
        {
            return $"the update was made with FG Scanner {manifest.AppVersion}; this station runs {request.AppVersion} — call Franz";
        }

        if (request.ReceiverMigration is { } here && here != request.LatestMigration)
        {
            return "FG Scanner has not been started since it was updated — start it once, close it, then try again";
        }

        if (manifest.LastMigration != request.LatestMigration)
        {
            return $"the update's database is at {manifest.LastMigration ?? "no migration"}; this station is at {request.LatestMigration} — call Franz";
        }

        var sentPath = Path.Combine(request.StationRoot, StationFreezeRecord.SentManifestFileName);
        if (!File.Exists(sentPath) || manifest.BasedOn != StationManifest.HashOf(File.ReadAllBytes(sentPath)))
        {
            return "the update was not made from this station's last pack — call Franz";
        }

        // The rows are compared as this station recorded them when it sent, never as the pack
        // describes them: a pack choosing its own tables could pick ones Jim did not touch.
        var own = OwnContent(request.StationRoot);
        if (own is null || manifest.BasedOnContent is null || !own.Equals(manifest.BasedOnContent))
        {
            return "the update was not made from this station's last pack — call Franz";
        }

        try
        {
            if (database.ContentSha256(db, own.Tables) != own.Sha256)
            {
                return "this station's data has changed since it was sent (something was scanned or edited) — call Franz";
            }
        }
        catch (InvalidDataException ex)
        {
            return $"this station's database cannot be compared with the update ({ex.Message}) — call Franz";
        }

        var sent = StationManifest.Parse(File.ReadAllBytes(sentPath)).Files
            .ToDictionary(f => f.Rel, f => f.Sha256, StringComparer.Ordinal);
        var now = Hashes(request.EvidenceRoot, trash);
        var drift = now.Where(p => !sent.TryGetValue(p.Key, out var sha) || sha != p.Value).Select(p => p.Key)
            .Concat(sent.Keys.Where(rel => !now.ContainsKey(rel)))
            .Order(StringComparer.Ordinal)
            .ToList();
        return drift.Count == 0
            ? null
            : $"this station's files have changed since they were sent ({string.Join(", ", drift.Take(3))}" +
              (drift.Count > 3 ? $" and {(drift.Count - 3).ToString(CultureInfo.InvariantCulture)} more" : "") + ") — call Franz";
    }

    /// <summary>
    /// What this station's rows looked like when it last sent: kept in the freeze record by the
    /// station verb, or — after a send by the 0.5.4 PowerShell sender, which cannot read SQLite —
    /// worked out from the copy of the sent database that sender kept beside it.
    /// </summary>
    private StationContent? OwnContent(string stationRoot)
    {
        var freeze = StationFreezeRecord.Read(stationRoot);
        if (freeze?.Content is not null)
        {
            return freeze.Content;
        }

        var sentDb = Path.Combine(stationRoot, StationFreezeRecord.SentDatabaseFileName);
        return freeze is not null && File.Exists(sentDb) && StationManifestBuilder.Sha256Of(sentDb) == freeze.LiveDbSha256
            ? database.Content(sentDb)
            : null;
    }

    /// <summary>Everything new goes into the staging folder first, so a failure here touches nothing live.</summary>
    private string? Stage(StationApplyRequest request, StationManifest manifest, string db, string trash, string staging)
    {
        var stagedDb = Path.Combine(staging, StationManifest.DatabaseFileName);
        File.Copy(Path.Combine(request.PackFolder, StationManifest.DatabaseFileName), stagedDb);
        if (StationManifestBuilder.Sha256Of(stagedDb) != manifest.DbSha256)
        {
            return "the update's database did not copy intact — call Franz";
        }

        // The arriving database is untrusted (Revision D): checked whole before it is touched,
        // its tables and indexes must be this version's, and every folder it names must lie in
        // this station's evidence folder once it is bound.
        if (!database.IntegrityOk(stagedDb))
        {
            return "the update's database failed its integrity check — call Franz";
        }

        if (database.SchemaProblem(stagedDb, File.Exists(db) ? db : null, manifest.LastMigration) is { } schema)
        {
            return $"{schema} — call Franz";
        }

        try
        {
            database.BindToStation(stagedDb, File.Exists(db) ? db : null, trash);
        }
        catch (InvalidDataException ex)
        {
            return $"the update's database cannot be made this station's own ({ex.Message}) — call Franz";
        }

        if (database.PathOutside(stagedDb, request.EvidenceRoot) is { } outside)
        {
            return $"the update names a folder outside this station's evidence folder ({outside}) — call Franz";
        }

        if (!database.IntegrityOk(stagedDb))
        {
            return "the update's database failed its integrity check — call Franz";
        }

        foreach (var file in manifest.Files.Where(f => f.From != f.Rel))
        {
            var target = Path.Combine(staging, ToPath(file.Rel));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(
                file.From is null ? Path.Combine(request.PackFolder, ToPath(file.Rel)) : TargetOf(file.From, request.EvidenceRoot, trash),
                target);
            if (StationManifestBuilder.Sha256Of(target) != file.Sha256)
            {
                return $"{file.Rel} did not copy intact — call Franz";
            }
        }

        return null;
    }

    private static void Commit(StationApplyRequest request, StationManifest manifest, string db, string trash, string staging, string backup)
    {
        var keptInPlace = manifest.Files.Where(f => f.From == f.Rel).Select(f => f.Rel).ToHashSet(StringComparer.Ordinal);
        var placed = manifest.Files.Where(f => f.From != f.Rel).Select(f => f.Rel).ToList();
        var movedAside = Hashes(request.EvidenceRoot, trash).Keys.Where(rel => !keptInPlace.Contains(rel))
            .Order(StringComparer.Ordinal).ToList();

        // The plan is written before anything is copied or moved, so a restore after any failure
        // knows what to put back - and which station files existed, so it never deletes one that
        // the apply simply had not reached yet.
        var existed = StationFiles.Where(name => File.Exists(Path.Combine(request.StationRoot, name))).ToList();
        StationPacker.WriteAtomically(Path.Combine(backup, UndoPlanFileName),
            JsonSerializer.SerializeToUtf8Bytes(new UndoPlan(request.EvidenceRoot, placed, movedAside, existed)));
        foreach (var name in new[] { StationManifest.DatabaseFileName, StationFreezeRecord.FileName, StationFreezeRecord.SentManifestFileName })
        {
            var live = Path.Combine(request.StationRoot, name);
            if (File.Exists(live))
            {
                File.Copy(live, Path.Combine(backup, name));
            }
        }

        foreach (var rel in movedAside)
        {
            var kept = Path.Combine(backup, ToPath(rel));
            Directory.CreateDirectory(Path.GetDirectoryName(kept)!);
            File.Move(TargetOf(rel, request.EvidenceRoot, trash), kept);
        }

        foreach (var rel in placed)
        {
            var target = TargetOf(rel, request.EvidenceRoot, trash);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Move(Path.Combine(staging, ToPath(rel)), target);
        }

        foreach (var sidecar in new[] { db + "-wal", db + "-shm" })
        {
            if (File.Exists(sidecar))
            {
                File.Move(sidecar, Path.Combine(backup, Path.GetFileName(sidecar)));
            }
        }

        Directory.CreateDirectory(request.StationRoot);
        File.Move(Path.Combine(staging, StationManifest.DatabaseFileName), db, overwrite: true);
        Directory.Delete(staging, recursive: true);
    }

    private static Dictionary<string, string> Hashes(string evidenceRoot, string trash)
    {
        var all = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (root, prefix) in new[] { (evidenceRoot, StationManifest.EvidenceFolder), (trash, StationManifest.TrashFolder) })
        {
            if (!Directory.Exists(root))
            {
                continue;
            }

            foreach (var path in Directory.EnumerateFiles(root, "*", StationManifestBuilder.AllFiles))
            {
                all[prefix + "/" + StationManifestBuilder.RelativeOf(root, path)] = StationManifestBuilder.Sha256Of(path);
            }
        }

        return all;
    }

    private static bool HoldsFiles(string folder) =>
        Directory.Exists(folder) && Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories).Any();

    private static string TargetOf(string rel, string evidenceRoot, string trash)
    {
        var slash = rel.IndexOf('/', StringComparison.Ordinal);
        return Path.Combine(rel[..slash] == StationManifest.TrashFolder ? trash : evidenceRoot, ToPath(rel[(slash + 1)..]));
    }

    private static string ToPath(string rel) => rel.Replace('/', Path.DirectorySeparatorChar);

    private static string UniqueFolder(string parent, string name)
    {
        for (var n = 1; ; n++)
        {
            var path = Path.Combine(parent, n == 1 ? name : $"{name}-{n.ToString(CultureInfo.InvariantCulture)}");
            if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
                return path;
            }
        }
    }

    private sealed record UndoPlan(
        string EvidenceRoot, IReadOnlyList<string> Placed, IReadOnlyList<string> MovedAside, IReadOnlyList<string> Existed);
}
