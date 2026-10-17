using System.Globalization;
using Microsoft.Data.Sqlite;

namespace FgScanner.Data;

/// <summary>
/// What packing or backing up a station needs from its database, read without migrating it:
/// neither may change the station it is copying. Shared by the CLI's <c>station</c> verbs and the
/// app's Back up button, so both find the evidence folder the same way.
/// </summary>
public sealed record StationFacts(
    string? LastMigration,
    int PendingReadingJobs,
    IReadOnlyList<string> BaseDirectories,
    IReadOnlyList<string> GroupDirectories)
{
    public static StationFacts Read(string database)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = database,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false,
        }.ToString());
        connection.Open();
        return new StationFacts(
            Strings(connection, "SELECT MigrationId FROM __EFMigrationsHistory ORDER BY MigrationId DESC LIMIT 1")
                .FirstOrDefault(),
            // JobType.Ocr = 0; JobState Pending = 0, InFlight = 1 (stored as integers).
            int.Parse(Strings(connection, "SELECT count(*) FROM Jobs WHERE Type = 0 AND State IN (0, 1)")[0],
                CultureInfo.InvariantCulture),
            Strings(connection, "SELECT DISTINCT BaseDirectory FROM Profiles WHERE BaseDirectory <> ''"),
            Strings(connection, "SELECT DirectoryPath FROM Groups"));
    }

    /// <summary>The evidence folder: the one given, else the one folder the profiles name.</summary>
    public string? EvidenceRoot(string? given) =>
        given ?? (BaseDirectories.Count == 1 ? BaseDirectories[0] : null);

    /// <summary>A group outside the evidence folder would not travel, and a restored database
    /// would point at a folder that is not there.</summary>
    public string? GroupOutside(string evidence)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(evidence)) + Path.DirectorySeparatorChar;
        return GroupDirectories.FirstOrDefault(d =>
            !(Path.TrimEndingDirectorySeparator(Path.GetFullPath(d)) + Path.DirectorySeparatorChar)
                .StartsWith(root, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// A page the pack's database names whose file is on the station now but not in the pack: it
    /// was saved while the station was copied (SPEC-SCANNER-2026-011 Revision C, CR-5). A page whose
    /// file is missing from the station too went missing long before, and is not this copy's fault.
    /// </summary>
    public static string? PageMissingFromPack(string packFolder, string evidenceRoot)
    {
        var manifest = Core.Station.StationManifest.Parse(File.ReadAllBytes(Path.Combine(packFolder, Core.Station.StationManifest.FileName)));
        var packed = manifest.Files.Select(f => f.Rel).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(evidenceRoot)) + Path.DirectorySeparatorChar;

        // Read from a copy: opening the pack's own database (WAL mode, as EF made it) leaves -wal and
        // -shm files in the pack, which its verification then refuses as files it does not list.
        var copy = Path.Combine(Path.GetTempPath(), $"fgscanner-check-{Guid.NewGuid():N}.db");
        File.Copy(Path.Combine(packFolder, Core.Station.StationManifest.DatabaseFileName), copy);
        try
        {
            return MissingIn(copy, packed, root);
        }
        finally
        {
            foreach (var file in new[] { copy, copy + "-wal", copy + "-shm" })
            {
                File.Delete(file);
            }
        }
    }

    private static string? MissingIn(string database, HashSet<string> packed, string root)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = database,
            Pooling = false,
        }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT g.DirectoryPath, p.FileName FROM Pages p JOIN Documents d ON p.DocumentId = d.Id JOIN Groups g ON d.GroupId = g.Id";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var file = Path.GetFullPath(Path.Combine(reader.GetString(0), reader.GetString(1)));
            if (!file.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var rel = Core.Station.StationManifest.EvidenceFolder + "/" + file[root.Length..].Replace(Path.DirectorySeparatorChar, '/');
            if (!packed.Contains(rel) && File.Exists(file))
            {
                return file;
            }
        }

        return null;
    }

    /// <summary>SQLite's backup API: a consistent copy even of a database the app has open. No
    /// pooling, so no handle on either file outlives the copy — a pooled one kept the copy locked
    /// against the check and the clean-up that follow.</summary>
    public static void CopyDatabase(string from, string to)
    {
        using var source = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = from,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false,
        }.ToString());
        using var target = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = to,
            Pooling = false,
        }.ToString());
        source.Open();
        target.Open();
        source.BackupDatabase(target);
    }

    private static List<string> Strings(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        using var reader = command.ExecuteReader();
        var values = new List<string>();
        while (reader.Read())
        {
            values.Add(Convert.ToString(reader.GetValue(0), CultureInfo.InvariantCulture)!);
        }

        return values;
    }
}
