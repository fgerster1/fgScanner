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
