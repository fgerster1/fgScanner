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

    [Fact]
    public void Binding_into_an_empty_station_keeps_the_senders_settings()
    {
        var incoming = Fixture("current");
        Execute(incoming, "INSERT OR REPLACE INTO Settings (Key, Value) VALUES ('Theme', 'Dark')");

        _database.BindToStation(incoming, null, Path.Combine(_root, "franz", "trash"));

        Assert.Equal("Dark", Scalar(incoming, "SELECT Value FROM Settings WHERE Key = 'Theme'"));
        Assert.StartsWith(Path.Combine(_root, "franz", "trash"), Scalar(incoming, "SELECT TrashFolderPath FROM TrashItems LIMIT 1"));
    }
}
