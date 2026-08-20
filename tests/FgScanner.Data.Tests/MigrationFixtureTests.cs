using FgScanner.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FgScanner.Data.Tests;

/// <summary>
/// Every shipped schema version leaves a fixture .db in fixtures/. Each fixture must migrate
/// cleanly to the current schema with data intact — the test that catches a library-corrupting
/// upgrade before a user does (PLAN research: delivery §testing).
/// </summary>
public sealed class MigrationFixtureTests
{
    public static TheoryData<string> FixtureFiles()
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "fixtures");
        var data = new TheoryData<string>();
        foreach (var file in Directory.EnumerateFiles(dir, "*.db"))
        {
            data.Add(Path.GetFileName(file));
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(FixtureFiles))]
    public void Fixture_migrates_to_current_schema_with_data_intact(string fixtureName)
    {
        var work = Path.Combine(Path.GetTempPath(), "fgscanner-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        var dbPath = Path.Combine(work, fixtureName);
        File.Copy(Path.Combine(AppContext.BaseDirectory, "fixtures", fixtureName), dbPath);

        try
        {
            DbBootstrapper.MigrateWithBackup(dbPath, "test");

            using var db = new FgScannerDbContext(DbBootstrapper.BuildOptions(dbPath));
            Assert.Empty(db.Database.GetPendingMigrations());

            using var connection = new SqliteConnection($"Data Source={dbPath}");
            connection.Open();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "PRAGMA integrity_check;";
            Assert.Equal("ok", cmd.ExecuteScalar());

            // Seeded marker data from fixture creation must survive every future migration.
            cmd.CommandText = "SELECT COUNT(*) FROM Groups WHERE Name = 'FixtureGroup';";
            Assert.Equal(1L, cmd.ExecuteScalar());
            cmd.CommandText = "SELECT COUNT(*) FROM v_pages;";
            Assert.Equal(1L, cmd.ExecuteScalar());
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(work, recursive: true);
        }
    }
}
