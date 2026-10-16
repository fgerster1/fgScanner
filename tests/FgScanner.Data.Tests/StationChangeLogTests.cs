using System.Text.Json;
using FgScanner.Core.Evidence;
using FgScanner.Data;
using Microsoft.Data.Sqlite;
using Xunit;

namespace FgScanner.Data.Tests;

/// <summary>
/// What a return tells Jim and the case file (SPEC-SCANNER-2026-010 §07, AC-9, Q-B8): every cell
/// Franz changed in the evidence rows, with his name and the time — the chain of custody for
/// work done away from the station that captured it — and, in plain words, which groups are
/// still open and what to look for on the paper.
/// </summary>
public sealed class StationChangeLogTests : IDisposable
{
    private static readonly DateTimeOffset At = new(2026, 10, 16, 18, 0, 0, TimeSpan.Zero);

    private readonly string _root = Directory.CreateTempSubdirectory("fg-changelog").FullName;

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    /// <summary>The v2 fixture (one group, document and page), migrated as Franz's workspace is.</summary>
    private string Station(string name)
    {
        var path = Path.Combine(_root, name + ".db");
        File.Copy(Path.Combine(AppContext.BaseDirectory, "fixtures", "v2-exports-trash.db"), path);
        DbBootstrapper.MigrateWithBackup(path, "test");
        SqliteConnection.ClearAllPools();
        return path;
    }

    private string Copy(string from, string name)
    {
        var path = Path.Combine(_root, name + ".db");
        File.Copy(from, path);
        return path;
    }

    private static void Execute(string db, string sql)
    {
        using var connection = new SqliteConnection($"Data Source={db};Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static IReadOnlyList<JsonElement> Diff(string received, string processed) =>
        [.. StationChangeLog.Diff(received, processed, "Franz Gerster", At)
            .Select(line => JsonDocument.Parse(line).RootElement)];

    [Fact]
    public void An_unchanged_database_gives_an_empty_log()
    {
        var received = Station("received");

        Assert.Empty(Diff(received, Copy(received, "processed")));
    }

    [Fact]
    public void A_changed_cell_is_one_row_with_old_new_name_and_time()
    {
        var received = Station("received");
        var processed = Copy(received, "processed");
        Execute(processed, "UPDATE Groups SET BatchFieldsJson = '{\"Box\":\"7\"}'");

        var row = Assert.Single(Diff(received, processed));

        Assert.Equal("changed", row.GetProperty("change").GetString());
        Assert.Equal("Groups", row.GetProperty("table").GetString());
        Assert.Equal("BatchFieldsJson", row.GetProperty("column").GetString());
        Assert.Equal("{}", row.GetProperty("old").GetString());
        Assert.Equal("{\"Box\":\"7\"}", row.GetProperty("new").GetString());
        Assert.Equal("Franz Gerster", row.GetProperty("by").GetString());
        Assert.Equal("2026-10-16T18:00:00Z", row.GetProperty("at").GetString());
        Assert.False(string.IsNullOrEmpty(row.GetProperty("key").GetString()));
    }

    [Fact]
    public void Added_and_removed_rows_are_listed_whole()
    {
        var received = Station("received");
        var processed = Copy(received, "processed");
        Execute(processed, """
            INSERT INTO Groups (Id, Name, DirectoryPath, State, ProfileId, SchemaVersion, CreatedUtc, UpdatedUtc)
            SELECT 'NEW-GROUP', 'Added', DirectoryPath || '-2', State, ProfileId, SchemaVersion, CreatedUtc, UpdatedUtc FROM Groups
            """);
        Execute(processed, "DELETE FROM Pages");

        var rows = Diff(received, processed);

        var added = Assert.Single(rows, r => r.GetProperty("change").GetString() == "added");
        Assert.Equal("NEW-GROUP", added.GetProperty("key").GetString());
        Assert.Equal("Added", added.GetProperty("new").GetProperty("Name").GetString());
        var removed = Assert.Single(rows, r => r.GetProperty("change").GetString() == "removed");
        Assert.Equal("Pages", removed.GetProperty("table").GetString());
        Assert.Equal(JsonValueKind.Object, removed.GetProperty("old").ValueKind);
    }

    [Fact]
    public void A_migration_and_station_owned_values_are_not_Franzs_changes()
    {
        var received = Path.Combine(_root, "received.db");
        File.Copy(Path.Combine(AppContext.BaseDirectory, "fixtures", "v2-exports-trash.db"), received);
        var processed = Copy(received, "processed");
        DbBootstrapper.MigrateWithBackup(processed, "test");
        SqliteConnection.ClearAllPools();
        Execute(processed, "INSERT OR REPLACE INTO Settings (Key, Value) VALUES ('Feature.AutoOrient', 'false')");

        Assert.Empty(Diff(received, processed));
    }

    [Fact]
    public void The_needs_Jim_sheet_names_each_open_group_why_and_what_to_look_for()
    {
        var db = Station("workspace");
        Execute(db, $$"""
            UPDATE Documents SET CustomFieldsJson = '{"DocNo":"7","NoteState":"{{AnnotatedCaptureSequence.AsFound}}","NoteAuthor":"Jim"}'
            """);
        Execute(db, """
            INSERT INTO Groups (Id, Name, DirectoryPath, State, ProfileId, SchemaVersion, CreatedUtc, UpdatedUtc)
            SELECT 'DONE-GROUP', 'Finished box', DirectoryPath || '-done', 2, ProfileId, SchemaVersion, CreatedUtc, UpdatedUtc FROM Groups
            """);

        var sheet = NeedsJimSheet.Write(db, At);

        Assert.Contains("FixtureGroup", sheet);
        Assert.Contains("with no clean sheet after it in DocNo 7", sheet);
        Assert.Contains("a clean copy", sheet);
        Assert.Contains("Which box", sheet);
        Assert.DoesNotContain("Finished box", sheet);
    }

    [Fact]
    public void The_needs_Jim_sheet_says_so_when_nothing_is_open()
    {
        var db = Station("workspace");
        Execute(db, "UPDATE Groups SET State = 2");

        Assert.Contains("Nothing needs you", NeedsJimSheet.Write(db, At));
    }
}
