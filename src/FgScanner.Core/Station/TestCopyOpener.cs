using System.Text.Json;

namespace FgScanner.Core.Station;

/// <summary>
/// Marks a workspace as a test copy of another station (<c>test-copy.json</c> in its folder): its
/// title says TEST COPY, and it can never pack a send or a return, so nothing done in it — scans
/// included — ever reaches the station it came from (SPEC-SCANNER-2026-011 Q5, Q6).
/// </summary>
public sealed record TestCopyMarker(string From, string Code, string EvidenceRoot, DateTimeOffset At)
{
    public const string FileName = "test-copy.json";

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    public static bool IsTestCopy(string stationRoot) => File.Exists(Path.Combine(stationRoot, FileName));

    public static TestCopyMarker? Read(string stationRoot)
    {
        var path = Path.Combine(stationRoot, FileName);
        try
        {
            return File.Exists(path) ? JsonSerializer.Deserialize<TestCopyMarker>(File.ReadAllBytes(path), Json) : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static void Write(string stationRoot, TestCopyMarker marker) =>
        StationPacker.WriteAtomically(Path.Combine(stationRoot, FileName), JsonSerializer.SerializeToUtf8Bytes(marker, Json));
}

/// <summary>
/// Opens a whole pack (a backup Jim sent for testing) into an empty workspace with its evidence in
/// a folder Franz names. A pack's evidence folder is fixed, and on Franz's PC Jim's path is already
/// the round-trip workspace's, so <see cref="StationApplier"/> — whose rule is that a pack never
/// chooses where files land — cannot open it there. This takes the same untrusted-database checks
/// as apply, proves every stored path lies in the pack's own evidence folder, and only then moves
/// those paths to the named one.
/// </summary>
/// <param name="onPlaced">Observes each file as it lands (tests).</param>
public sealed class TestCopyOpener(IStationDatabase database, Action<string>? onPlaced = null)
{
    /// <param name="otherEvidenceRoots">The evidence folders this PC's stations really use. The
    /// pack's own evidence path is only text it chose, so it cannot be the whole guard
    /// (Revision C, SR-3).</param>
    /// <param name="expectedCode">The code Franz read out; the copy must still be the one with it.</param>
    public StationApplyResult Open(
        string packFolder, string stationRoot, string evidenceRoot, DateTimeOffset at,
        IReadOnlyList<string>? otherEvidenceRoots = null, string? expectedCode = null)
    {
        var verified = StationManifestVerifier.Verify(packFolder, out var manifestBytes);
        if (verified.Status == StationVerifyStatus.StillArriving)
        {
            return new StationApplyResult(null, "the copy is still arriving — wait for Drive, then try again", StillArriving: true);
        }

        if (verified.Status != StationVerifyStatus.Ok)
        {
            return StationApplyResult.Refused($"the copy is damaged ({verified.Problems[0]})");
        }

        if (expectedCode is not null && expectedCode != verified.Code)
        {
            return StationApplyResult.Refused(
                $"the copy is not the one whose code you checked ({expectedCode}; it is now {verified.Code}) — nothing was changed");
        }

        var manifest = StationManifest.Parse(manifestBytes!);
        var workspace = Full(stationRoot);
        var target = Full(evidenceRoot);
        var db = Path.Combine(workspace, StationManifest.DatabaseFileName);
        var trash = Path.Combine(workspace, StationManifest.TrashFolder);

        // A -wal or -shm left beside the database is replayed into it by SQLite when it opens, after
        // every check here (Revision C, SR-1).
        if (new[] { db, db + "-wal", db + "-shm", db + "-journal" }.Any(File.Exists))
        {
            return StationApplyResult.Refused($"{stationRoot} already has a station — a test copy opens only into an empty workspace");
        }

        if (manifest.Files.Any(f => f.From is not null))
        {
            return StationApplyResult.Refused("this pack only fills in a station that already has the earlier data — a test copy needs a whole backup");
        }

        // The scans' folder and the workspace apart, both ways: a file placed beside the database
        // could take a station file's name (Revision C, SR-1).
        if (Overlap(target, workspace))
        {
            return StationApplyResult.Refused($"the test copy's scans folder {evidenceRoot} must be apart from the workspace {stationRoot}");
        }

        if (Overlap(target, Full(manifest.EvidenceRoot)))
        {
            return StationApplyResult.Refused($"the test copy's folder must be apart from {manifest.EvidenceRoot}, where the station it came from keeps its scans");
        }

        if ((otherEvidenceRoots ?? []).FirstOrDefault(other => Overlap(target, Full(other))) is { } taken)
        {
            return StationApplyResult.Refused($"the test copy's folder {evidenceRoot} overlaps {taken}, where another station on this PC keeps its scans");
        }

        if (Holds(target) || Holds(trash))
        {
            return StationApplyResult.Refused($"the folder {evidenceRoot} already holds files, and a test copy must start empty");
        }

        Directory.CreateDirectory(workspace);
        var staging = Path.Combine(workspace, "test-copy-staging");
        if (Directory.Exists(staging))
        {
            Directory.Delete(staging, recursive: true);
        }

        Directory.CreateDirectory(staging);
        var placed = new List<string>();
        try
        {
            if (Stage(packFolder, manifest, staging, target, trash) is { } problem)
            {
                Directory.Delete(staging, recursive: true);
                return StationApplyResult.Refused(problem);
            }

            // The marker first: whatever lands after it is already a test copy, so a failure part-way
            // can never leave a station that sends (Revision C, CR-4).
            TestCopyMarker.Write(workspace, new TestCopyMarker(Path.GetFileName(Path.TrimEndingDirectorySeparator(packFolder)), verified.Code!, target, at));
            foreach (var file in manifest.Files)
            {
                var place = PlaceOf(file.Rel, target, trash);
                Directory.CreateDirectory(Path.GetDirectoryName(place)!);
                File.Move(Path.Combine(staging, ToPath(file.Rel)), place);
                placed.Add(place);
                onPlaced?.Invoke(place);
            }

            // A move across drives is a copy; read every placed file back before the station exists.
            if (manifest.Files.FirstOrDefault(f => StationManifestBuilder.Sha256Of(PlaceOf(f.Rel, target, trash)) != f.Sha256) is { } bad)
            {
                throw new IOException($"{bad.Rel} did not land intact");
            }

            File.Move(Path.Combine(staging, StationManifest.DatabaseFileName), db);
            Directory.Delete(staging, recursive: true);
            return new StationApplyResult(verified.Code, null);
        }
        catch
        {
            // Leave nothing that looks like a station: the folders were empty before.
            foreach (var file in placed.Append(db).Append(Path.Combine(workspace, TestCopyMarker.FileName)))
            {
                File.Delete(file);
            }

            if (Directory.Exists(staging))
            {
                Directory.Delete(staging, recursive: true);
            }

            throw;
        }
    }

    private static string Full(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

    /// <summary>The same folder, or one inside the other.</summary>
    private static bool Overlap(string a, string b) =>
        a.Equals(b, StringComparison.OrdinalIgnoreCase)
        || a.StartsWith(b + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
        || b.StartsWith(a + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    private string? Stage(string packFolder, StationManifest manifest, string staging, string target, string trash)
    {
        var stagedDb = Path.Combine(staging, StationManifest.DatabaseFileName);
        File.Copy(Path.Combine(packFolder, StationManifest.DatabaseFileName), stagedDb);
        if (StationManifestBuilder.Sha256Of(stagedDb) != manifest.DbSha256)
        {
            return "the copy's database did not copy intact";
        }

        // Untrusted, exactly as a send is: whole, this version's tables, no settings carried over,
        // and every stored folder inside the pack's own evidence folder before any path is moved.
        if (!database.IntegrityOk(stagedDb))
        {
            return "the copy's database failed its integrity check";
        }

        if (database.SchemaProblem(stagedDb, null, manifest.LastMigration) is { } schema)
        {
            return schema;
        }

        try
        {
            database.BindToStation(stagedDb, null, trash);
        }
        catch (InvalidDataException ex)
        {
            return $"the copy's database cannot be made this workspace's own ({ex.Message})";
        }

        if (database.PathOutside(stagedDb, manifest.EvidenceRoot) is { } outside)
        {
            return $"the copy names a folder outside its own evidence folder ({outside})";
        }

        database.RerootEvidence(stagedDb, manifest.EvidenceRoot, target);
        if (database.PathOutside(stagedDb, target) is { } stray || !database.IntegrityOk(stagedDb))
        {
            return "the copy's folders could not all be moved to the test folder";
        }

        foreach (var file in manifest.Files)
        {
            var staged = Path.Combine(staging, ToPath(file.Rel));
            Directory.CreateDirectory(Path.GetDirectoryName(staged)!);
            File.Copy(Path.Combine(packFolder, ToPath(file.Rel)), staged);
            if (StationManifestBuilder.Sha256Of(staged) != file.Sha256)
            {
                return $"{file.Rel} did not copy intact";
            }

            // The verifier already refuses a rel that climbs out; this is the last word on where it lands.
            if (!Within(PlaceOf(file.Rel, target, trash), file.Rel.StartsWith(StationManifest.TrashFolder + "/", StringComparison.Ordinal) ? trash : target))
            {
                return $"{file.Rel} would land outside the test copy";
            }
        }

        return null;
    }

    private static string PlaceOf(string rel, string evidenceRoot, string trash)
    {
        var slash = rel.IndexOf('/', StringComparison.Ordinal);
        var root = rel[..slash] == StationManifest.TrashFolder ? trash : evidenceRoot;
        return Path.GetFullPath(Path.Combine(root, ToPath(rel[(slash + 1)..])));
    }

    private static bool Within(string path, string root) =>
        path.StartsWith(Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    private static bool Holds(string folder) =>
        Directory.Exists(folder) && Directory.EnumerateFileSystemEntries(folder, "*", SearchOption.AllDirectories).Any();

    private static string ToPath(string rel) => rel.Replace('/', Path.DirectorySeparatorChar);
}
