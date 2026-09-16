using FgScanner.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
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

    /// <summary>
    /// The first migration since 0.4.0 shipped, and neither fixture is that shape. A field written by
    /// 0.4.0 must read "no limit, not memo" afterwards: anything else would change what validates in
    /// groups nobody touched.
    /// </summary>
    [Fact]
    public void A_field_written_before_lengths_existed_reads_no_limit_and_not_memo()
    {
        var work = Path.Combine(Path.GetTempPath(), "fgscanner-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        var dbPath = Path.Combine(work, "v0.4.0.db");

        try
        {
            using (var db = new FgScannerDbContext(DbBootstrapper.BuildOptions(dbPath)))
            {
                db.GetService<IMigrator>().Migrate("20260828162943_AddFieldScopeAndGroupBatchFields");
            }

            using (var connection = new SqliteConnection($"Data Source={dbPath}"))
            {
                connection.Open();
                using var insert = connection.CreateCommand();
                // Only the field row is under test, so the schema and profile it would belong to are not created.
                insert.CommandText = """
                    PRAGMA foreign_keys = OFF;
                    INSERT INTO FieldDefinitions (Id, SchemaId, "Order", Name, Type, Required, Sticky, DefaultValue, ListChoicesJson, Scope)
                    VALUES ('00000000-0000-0000-0000-000000000001', '00000000-0000-0000-0000-000000000002', 0, 'Title', 0, 0, 0, NULL, NULL, 0);
                    """;
                insert.ExecuteNonQuery();
            }

            DbBootstrapper.MigrateWithBackup(dbPath, "test");

            using var check = new SqliteConnection($"Data Source={dbPath}");
            check.Open();
            using var select = check.CreateCommand();
            select.CommandText = "SELECT MaxLength, Memo FROM FieldDefinitions WHERE Name = 'Title';";
            using var reader = select.ExecuteReader();
            Assert.True(reader.Read());
            Assert.True(reader.IsDBNull(0));
            Assert.Equal(0L, reader.GetInt64(1));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(work, recursive: true);
        }
    }
}
