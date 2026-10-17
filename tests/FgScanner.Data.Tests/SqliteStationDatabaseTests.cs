using FgScanner.Core.Station;
using FgScanner.Data;
using Microsoft.Data.Sqlite;
using Xunit;

namespace FgScanner.Data.Tests;

/// <summary>
/// The database half of station pack/apply (SPEC-SCANNER-2026-010 §07, amendment 2026-10-16):
/// the content fingerprint survives a migration and an app start but not an evidence edit, and
/// an arriving database is made the receiving station's own (its settings, its trash folder).
/// </summary>
public sealed class SqliteStationDatabaseTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("fg-stationdb").FullName;

    private readonly SqliteStationDatabase _database = new();

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

    private string Fixture(string name)
    {
        var path = Path.Combine(_root, Guid.NewGuid().ToString("N") + ".db");
        File.Copy(Path.Combine(AppContext.BaseDirectory, "fixtures", "v2-exports-trash.db"), path);
        return name == "current" ? Seeded(Migrated(path)) : path;
    }

    /// <summary>The fixture has no profile and no trash; both carry station-owned values.</summary>
    private static string Seeded(string db)
    {
        new ProfileService(new Factory(db)).EnsureDefaultAsync().GetAwaiter().GetResult();
        SqliteConnection.ClearAllPools();
        Execute(db, """
            INSERT INTO TrashItems (Id, OriginalGroupId, GroupName, GroupDirectoryPath, DocumentSequence,
                PayloadJson, FilesJson, TrashFolderPath, DeletedUtc)
            VALUES ('A12BC3C9-3A95-42BB-99FC-FE3E3DFAA47F', 'CE7AD3B5-B2C7-490E-9A03-8B7E89220580', 'Toms', 'C:\Evidence-Scans\Toms',
                263, '{}', '["scan_00263.jpg"]', 'C:\Users\fgers\AppData\Roaming\FGScanner\trash\a12bc3c93a9542bb99fcfe3e3dfaa47f',
                '2026-09-24 19:54:43')
            """);
        return db;
    }

    private sealed class Factory(string dbPath) : Microsoft.EntityFrameworkCore.IDbContextFactory<FgScannerDbContext>
    {
        public FgScannerDbContext CreateDbContext() => new(DbBootstrapper.BuildOptions(dbPath));
    }

    private static string Migrated(string path)
    {
        DbBootstrapper.MigrateWithBackup(path, "test");
        SqliteConnection.ClearAllPools();
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

    private static string Scalar(string db, string sql)
    {
        using var connection = new SqliteConnection($"Data Source={db};Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToString(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture)!;
    }

    [Fact]
    public void A_migration_does_not_change_the_content()
    {
        var sent = Fixture("v2");
        var content = _database.Content(sent);
        var later = Migrated(Fixture("v2"));

        Assert.NotEqual(StationManifestBuilder.Sha256Of(sent), StationManifestBuilder.Sha256Of(later));
        Assert.Equal(content.Sha256, _database.ContentSha256(later, content.Tables));
    }

    [Fact]
    public void An_evidence_edit_changes_the_content()
    {
        var db = Fixture("current");
        var content = _database.Content(db);

        Execute(db, "UPDATE Groups SET Name = 'Renamed' WHERE Name = 'FixtureGroup'");

        Assert.NotEqual(content.Sha256, _database.ContentSha256(db, content.Tables));
    }

    [Fact]
    public void Station_owned_values_are_not_content()
    {
        var db = Fixture("current");
        var content = _database.Content(db);

        Execute(db, "INSERT OR REPLACE INTO Settings (Key, Value) VALUES ('Theme', 'Dark')");
        Execute(db, "UPDATE Profiles SET ScanSettingsJson = '{\"Dpi\":600}'");
        Execute(db, "UPDATE TrashItems SET TrashFolderPath = 'D:\\elsewhere\\trash\\x'");

        Assert.Equal(content.Sha256, _database.ContentSha256(db, content.Tables));
        Assert.DoesNotContain(content.Tables, t => t.Name == "Settings");
    }

    [Fact]
    public void A_column_the_receiver_lacks_cannot_be_compared()
    {
        var db = Fixture("v2");

        Assert.Throws<InvalidDataException>(() =>
            _database.ContentSha256(db, [new StationContentTable("Pages", ["Id", "OrientationSettled"])]));
    }

    [Fact]
    public void Binding_keeps_the_receivers_settings_and_scan_settings_and_rebinds_trash()
    {
        // Jim's station and the copy Franz sends back share their profiles' ids.
        var incoming = Fixture("current");
        var receiver = Path.Combine(_root, "jim.db");
        File.Copy(incoming, receiver);
        Execute(incoming, "INSERT OR REPLACE INTO Settings (Key, Value) VALUES ('Feature.AutoOrient', 'false')");
        Execute(incoming, "UPDATE Profiles SET ScanSettingsJson = '{\"from\":\"franz\"}'");
        Execute(receiver, "DELETE FROM Settings");
        Execute(receiver, "INSERT INTO Settings (Key, Value) VALUES ('Feature.AutoOrient', 'true')");
        Execute(receiver, "UPDATE Profiles SET ScanSettingsJson = '{\"from\":\"jim\"}'");
        var trash = Path.Combine(_root, "jim", "trash");
        var item = Scalar(incoming, "SELECT TrashFolderPath FROM TrashItems LIMIT 1");

        _database.BindToStation(incoming, receiver, trash);

        Assert.Equal("1", Scalar(incoming, "SELECT count(*) FROM Settings"));
        Assert.Equal("true", Scalar(incoming, "SELECT Value FROM Settings WHERE Key = 'Feature.AutoOrient'"));
        Assert.Equal("0", Scalar(incoming, "SELECT count(*) FROM Profiles WHERE ScanSettingsJson <> '{\"from\":\"jim\"}'"));
        Assert.Equal(Path.Combine(trash, Path.GetFileName(item.TrimEnd('\\', '/'))),
            Scalar(incoming, "SELECT TrashFolderPath FROM TrashItems LIMIT 1"));
        Assert.True(_database.IntegrityOk(incoming));
    }

    /// <summary>The arriving database is untrusted: a trash path ending in <c>..</c> would re-root
    /// the item outside the station's trash, where a later restore or purge would act.</summary>
    [Theory]
    [InlineData(@"C:\Users\x\trash\..")]
    [InlineData(@"C:\Users\x\trash\.")]
    [InlineData(@"C:\Users\x\trash\...")]
    [InlineData(@"C:\Users\x\trash\a12bc ")]
    [InlineData(@"\\")]
    public void A_trash_path_that_would_leave_the_trash_folder_is_refused(string hostile)
    {
        var incoming = Fixture("current");
        Execute(incoming, $"UPDATE TrashItems SET TrashFolderPath = '{hostile}'");

        Assert.Throws<InvalidDataException>(() =>
            _database.BindToStation(incoming, null, Path.Combine(_root, "jim", "trash")));
    }

    /// <summary>Revision D, D1: nothing in a pack configures the machine that opens it — the
    /// sender's Settings include the commit-hook command line FG Scanner runs.</summary>
    [Fact]
    public void Binding_into_an_empty_station_takes_none_of_the_senders_settings()
    {
        var incoming = Fixture("current");
        Execute(incoming, "INSERT OR REPLACE INTO Settings (Key, Value) VALUES ('Hook.CommandLine', 'powershell -c evil')");

        _database.BindToStation(incoming, null, Path.Combine(_root, "franz", "trash"));

        Assert.Equal("0", Scalar(incoming, "SELECT count(*) FROM Settings"));
        Assert.StartsWith(Path.Combine(_root, "franz", "trash"), Scalar(incoming, "SELECT TrashFolderPath FROM TrashItems LIMIT 1"));
    }

    /// <summary>SPEC-SCANNER-2026-011 Revision C, CR-3 (Franz): a restore from Jim's own backup keeps
    /// his settings — keep-originals and his mail choice among them — but never one that runs a
    /// command or calls out.</summary>
    [Fact]
    public void A_restore_keeps_the_backups_settings_except_the_ones_that_run_or_call_out()
    {
        var incoming = Fixture("current");
        Execute(incoming, "INSERT OR REPLACE INTO Settings (Key, Value) VALUES ('Feature.PreserveOriginals', 'true')");
        Execute(incoming, "INSERT OR REPLACE INTO Settings (Key, Value) VALUES ('Email.SendWith', 'Yahoo')");
        Execute(incoming, "INSERT OR REPLACE INTO Settings (Key, Value) VALUES ('Hook.CommandLine', 'powershell -c evil')");
        Execute(incoming, "INSERT OR REPLACE INTO Settings (Key, Value) VALUES ('hook.WebhookUrl', 'https://attacker')");

        _database.BindToStation(incoming, null, Path.Combine(_root, "jim", "trash"), restore: true);

        Assert.Equal("true", Scalar(incoming, "SELECT Value FROM Settings WHERE Key = 'Feature.PreserveOriginals'"));
        Assert.Equal("Yahoo", Scalar(incoming, "SELECT Value FROM Settings WHERE Key = 'Email.SendWith'"));
        Assert.Equal("0", Scalar(incoming, "SELECT count(*) FROM Settings WHERE lower(substr(Key, 1, 5)) = 'hook.'"));
    }

    /// <summary>Revision D, P10-2: a path in the arriving database decides where future scans and
    /// exports are written; one outside the evidence folder, or on a share, refuses the update.</summary>
    [Theory]
    [InlineData("UPDATE Profiles SET BaseDirectory = '\\\\attacker@SSL\\x'", "Profiles.BaseDirectory")]
    [InlineData("UPDATE Groups SET DirectoryPath = 'C:\\Users\\Jim\\Startup'", "Groups.DirectoryPath")]
    [InlineData("UPDATE TrashItems SET GroupDirectoryPath = 'D:\\elsewhere'", "TrashItems.GroupDirectoryPath")]
    // SPEC-SCANNER-2026-011 Revision C, SR-7: a file name is joined to its group's folder, so one
    // that climbs out or carries a drive reaches any file on the machine.
    [InlineData("UPDATE Pages SET FileName = '..\\..\\Users\\fgers\\Documents\\x.jpg'", "Pages.FileName")]
    [InlineData("UPDATE Pages SET FileName = 'C:\\Windows\\win.ini'", "Pages.FileName")]
    [InlineData("UPDATE Pages SET FileName = '.. \\.. \\Users\\x.jpg'", "Pages.FileName")]
    [InlineData("UPDATE Pages SET FileName = '...\\x.jpg'", "Pages.FileName")]
    [InlineData("UPDATE TrashItems SET FilesJson = '[\"..\\\\..\\\\x.jpg\"]'", "TrashItems.FilesJson")]
    [InlineData("UPDATE TrashItems SET PayloadJson = '{\"pages\":[{\"fileName\":\"..\\\\x.jpg\"}]}'", "TrashItems.PayloadJson")]
    public void A_path_outside_the_evidence_folder_is_named(string sql, string column)
    {
        var db = Fixture("current");
        var evidence = Path.Combine(_root, "Evidence-Scans");
        Execute(db, $"UPDATE Profiles SET BaseDirectory = '{evidence}'");
        Execute(db, $"UPDATE Groups SET DirectoryPath = '{Path.Combine(evidence, "Dental")}'");
        Execute(db, $"UPDATE TrashItems SET GroupDirectoryPath = '{Path.Combine(evidence, "Toms")}'");
        Assert.Null(_database.PathOutside(db, evidence));

        Execute(db, sql);

        Assert.StartsWith(column, _database.PathOutside(db, evidence));
    }

    /// <summary>Revision D, D2 (refined): tables and indexes must be exactly this version's;
    /// triggers and views are never taken from a pack but rebuilt from the build's own.</summary>
    [Fact]
    public void A_matching_schema_passes_and_an_extra_table_or_changed_index_does_not()
    {
        var receiver = Fixture("current");
        var incoming = Path.Combine(_root, "incoming.db");
        File.Copy(receiver, incoming);
        Assert.Null(_database.SchemaProblem(incoming, receiver, null));

        Execute(incoming, "CREATE TABLE Smuggled (Id TEXT)");
        Assert.Contains("Smuggled", _database.SchemaProblem(incoming, receiver, null));

        // LIKE's '_' is a wildcard: a table named like an internal one must not slip past.
        Execute(incoming, "DROP TABLE Smuggled");
        Execute(incoming, "CREATE TABLE sqliteXsmuggled (Id TEXT)");
        Assert.Contains("sqliteXsmuggled", _database.SchemaProblem(incoming, receiver, null));

        Execute(incoming, "DROP TABLE sqliteXsmuggled");
        var index = Scalar(incoming, "SELECT name FROM sqlite_master WHERE type = 'index' AND sql IS NOT NULL LIMIT 1");
        Execute(incoming, $"DROP INDEX \"{index}\"");
        Assert.Contains(index, _database.SchemaProblem(incoming, receiver, null));
    }

    [Fact]
    public void An_empty_station_compares_with_a_fresh_database_at_the_packs_version()
    {
        var shipped = Path.Combine(_root, "v0.5.4.db");
        File.Copy(Path.Combine(AppContext.BaseDirectory, "fixtures", "ladder", "v0.5.4.db"), shipped);

        Assert.Null(_database.SchemaProblem(shipped, null, "20260914210756_AddFieldLengthAndMemo"));
        Assert.NotNull(_database.SchemaProblem(shipped, null, "20261005135716_AddPageOrientationSettled"));
    }

    [Fact]
    public void An_unknown_version_cannot_be_compared()
    {
        var shipped = Path.Combine(_root, "v0.5.4.db");
        File.Copy(Path.Combine(AppContext.BaseDirectory, "fixtures", "ladder", "v0.5.4.db"), shipped);

        Assert.Contains("not a version", _database.SchemaProblem(shipped, null, "20990101000000_FromTheFuture"));
    }

    [Fact]
    public void Binding_drops_a_smuggled_trigger_and_rebuilds_the_builds_own()
    {
        var incoming = Fixture("current");
        var receiver = Path.Combine(_root, "jim.db");
        File.Copy(incoming, receiver);
        Execute(incoming, "CREATE TRIGGER Rewrite AFTER INSERT ON Pages BEGIN UPDATE Pages SET Checksum = 'forged'; END");
        Execute(incoming, "DROP VIEW v_index");
        Execute(incoming, "CREATE VIEW v_index AS SELECT 'lie' AS GroupName");

        _database.BindToStation(incoming, receiver, Path.Combine(_root, "jim", "trash"));

        Assert.Equal("Pages_fts_delete,Pages_fts_insert,Pages_fts_update",
            Scalar(incoming, "SELECT group_concat(name, ',') FROM (SELECT name FROM sqlite_master WHERE type = 'trigger' ORDER BY name)"));
        Assert.Equal(
            string.Join(" ", RawSchemaSql.CreateViewIndex.Split((char[])null!, StringSplitOptions.RemoveEmptyEntries)).TrimEnd(';'),
            string.Join(" ", Scalar(incoming, "SELECT sql FROM sqlite_master WHERE name = 'v_index'").Split((char[])null!, StringSplitOptions.RemoveEmptyEntries)));
    }

    /// <summary>Revision D, P10-7: table and column names come from an untrusted database or
    /// manifest and are spliced into SQL; anything but a plain identifier is refused.</summary>
    [Fact]
    public void A_table_name_that_is_not_an_identifier_is_refused()
    {
        var db = Fixture("current");

        Assert.Throws<InvalidDataException>(() => _database.ContentSha256(db,
            [new Core.Station.StationContentTable("Pages\" FROM Pages; SELECT \"x", ["Id"])]));
    }
}
