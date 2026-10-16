using System.Globalization;
using System.Text.Json;

namespace FgScanner.Core.Station;

/// <param name="StationRoot">The receiving station's folder: database, trash, recovery, freeze record.</param>
/// <param name="AppVersion">The version this station runs.</param>
/// <param name="LatestMigration">The newest migration this version knows.</param>
public sealed record StationApplyRequest(
    string PackFolder,
    string StationRoot,
    string EvidenceRoot,
    string AppVersion,
    string LatestMigration,
    DateTimeOffset At);

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
public sealed class StationApplier(Func<bool> isAppOpen, IStationDatabase database)
{
    private const string BackupPrefix = "apply-backup-";

    private const string UndonePrefix = "apply-undone-";

    private const string UndoPlanFileName = "undo.json";

    public StationApplyResult Apply(StationApplyRequest request)
    {
        var db = Path.Combine(request.StationRoot, StationManifest.DatabaseFileName);
        var trash = Path.Combine(request.StationRoot, StationManifest.TrashFolder);
        if (StationRest.Problem(isAppOpen, db, request.StationRoot, "install the update") is { } notAtRest)
        {
            return StationApplyResult.Refused(notAtRest);
        }

        var verified = StationManifestVerifier.Verify(request.PackFolder);
        if (verified.Status == StationVerifyStatus.StillArriving)
        {
            return new StationApplyResult(null, "the update is still arriving — wait for Drive, then try again", StillArriving: true);
        }

        if (verified.Status != StationVerifyStatus.Ok)
        {
            return StationApplyResult.Refused($"the update is damaged ({verified.Problems[0]}) — call Franz");
        }

        var manifestBytes = File.ReadAllBytes(Path.Combine(request.PackFolder, StationManifest.FileName));
        var manifest = StationManifest.Parse(manifestBytes);
        if (GateProblem(request, manifest, db, trash) is { } refusal)
        {
            return StationApplyResult.Refused(refusal);
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
        Commit(request, manifest, db, trash, staging, backup);

        var final = Hashes(request.EvidenceRoot, trash);
        var expected = manifest.Files.ToDictionary(f => f.Rel, f => f.Sha256, StringComparer.Ordinal);
        if (!final.OrderBy(p => p.Key, StringComparer.Ordinal).SequenceEqual(expected.OrderBy(p => p.Key, StringComparer.Ordinal)))
        {
            Undo(request.StationRoot);
            return StationApplyResult.Refused("this station did not match the update after installing it, so it has been put back — call Franz");
        }

        StationFreezeRecord.Write(request.StationRoot, new StationFreezeRecord(
            Path.GetFileName(Path.TrimEndingDirectorySeparator(request.PackFolder)), verified.Code!, manifest.SentAt,
            StationManifestBuilder.Sha256Of(db), StationManifest.HashOf(manifestBytes), database.Content(db)), manifestBytes);
        return new StationApplyResult(verified.Code, null, BackupFolder: backup);
    }

    /// <summary>Puts the station back as it was before the last apply.</summary>
    public StationApplyResult Undo(string stationRoot)
    {
        if (isAppOpen())
        {
            return StationApplyResult.Refused("FG Scanner is open — close it, then undo again");
        }

        var backup = Directory.Exists(stationRoot)
            ? Directory.EnumerateDirectories(stationRoot, BackupPrefix + "*")
                .Order(StringComparer.Ordinal).LastOrDefault()
            : null;
        if (backup is null)
        {
            return StationApplyResult.Refused("there is nothing to undo");
        }

        var plan = JsonSerializer.Deserialize<UndoPlan>(File.ReadAllBytes(Path.Combine(backup, UndoPlanFileName)))!;
        var evidenceRoot = plan.EvidenceRoot;
        var trash = Path.Combine(stationRoot, StationManifest.TrashFolder);
        foreach (var rel in plan.Placed)
        {
            var placed = TargetOf(rel, evidenceRoot, trash);
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
                var target = TargetOf(rel, evidenceRoot, trash);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Move(kept, target);
            }
        }

        var db = Path.Combine(stationRoot, StationManifest.DatabaseFileName);
        foreach (var name in new[] { StationManifest.DatabaseFileName, StationFreezeRecord.FileName, StationFreezeRecord.SentManifestFileName })
        {
            var kept = Path.Combine(backup, name);
            var live = Path.Combine(stationRoot, name);
            if (File.Exists(kept))
            {
                File.Copy(kept, live, overwrite: true);
            }
            else if (File.Exists(live))
            {
                File.Delete(live);
            }
        }

        Directory.Move(backup, Path.Combine(stationRoot, UndonePrefix + Path.GetFileName(backup)[BackupPrefix.Length..]));
        return new StationApplyResult(null, null, BackupFolder: backup);
    }

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

        if (manifest.LastMigration != request.LatestMigration)
        {
            return $"the update's database is at {manifest.LastMigration ?? "no migration"}; this station is at {request.LatestMigration} — call Franz";
        }

        var sentPath = Path.Combine(request.StationRoot, StationFreezeRecord.SentManifestFileName);
        if (!File.Exists(sentPath) || manifest.BasedOn != StationManifest.HashOf(File.ReadAllBytes(sentPath))
            || manifest.BasedOnContent is null)
        {
            return "the update was not made from this station's last pack — call Franz";
        }

        try
        {
            if (database.ContentSha256(db, manifest.BasedOnContent.Tables) != manifest.BasedOnContent.Sha256)
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

    /// <summary>Everything new goes into the staging folder first, so a failure here touches nothing live.</summary>
    private string? Stage(StationApplyRequest request, StationManifest manifest, string db, string trash, string staging)
    {
        var stagedDb = Path.Combine(staging, StationManifest.DatabaseFileName);
        File.Copy(Path.Combine(request.PackFolder, StationManifest.DatabaseFileName), stagedDb);
        database.BindToStation(stagedDb, File.Exists(db) ? db : null, trash);
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

        // The plan is written before anything moves, so an undo after a crash knows what to put back.
        foreach (var name in new[] { StationManifest.DatabaseFileName, StationFreezeRecord.FileName, StationFreezeRecord.SentManifestFileName })
        {
            var live = Path.Combine(request.StationRoot, name);
            if (File.Exists(live))
            {
                File.Copy(live, Path.Combine(backup, name));
            }
        }

        StationPacker.WriteAtomically(Path.Combine(backup, UndoPlanFileName),
            JsonSerializer.SerializeToUtf8Bytes(new UndoPlan(request.EvidenceRoot, placed, movedAside)));

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

    private sealed record UndoPlan(string EvidenceRoot, IReadOnlyList<string> Placed, IReadOnlyList<string> MovedAside);
}
