using FgScanner.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FgScanner.Data.Tests;

/// <summary>
/// SPEC-SCANNER-2026-010 AC-8: a station on any shipped release must upgrade to this build with
/// its rows intact — Jim jumps from 0.5.4 straight to 0.8.0, and whatever a station runs when a
/// later release reaches it is the schema that release must migrate. Each fixture in
/// <c>fixtures/ladder/</c> was written by that release's own CLI (two groups, six pages); every
/// release adds its own (docs/release.md).
/// </summary>
public sealed class MigrationLadderTests
{
    private static List<string> RungNames()
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "fixtures", "ladder");
        return Directory.Exists(dir)
            ? [.. Directory.EnumerateFiles(dir, "*.db").Select(f => Path.GetFileName(f)).Order(StringComparer.Ordinal)]
            : [];
    }

    public static TheoryData<string> Rungs() => [.. RungNames()];

    [Fact]
    public void The_ladder_starts_at_the_release_Jim_runs()
    {
        Assert.Contains("v0.5.4.db", RungNames());
    }

    [Theory]
    [MemberData(nameof(Rungs))]
    public void A_shipped_database_migrates_to_this_build_with_every_row(string rung)
    {
        var work = Directory.CreateTempSubdirectory("fg-ladder").FullName;
        var db = Path.Combine(work, rung);
        File.Copy(Path.Combine(AppContext.BaseDirectory, "fixtures", "ladder", rung), db);
        try
        {
            var before = Rows(db);
            var hadPending = PendingMigrations(db) > 0;

            var backup = DbBootstrapper.MigrateWithBackup(db, "ladder");

            Assert.Equal(0, PendingMigrations(db));
            Assert.Equal(hadPending, backup is not null && File.Exists(backup));
            Assert.Equal("ok", Scalar(db, "PRAGMA integrity_check"));
            Assert.Equal(before, Rows(db));
            using var context = new FgScannerDbContext(DbBootstrapper.BuildOptions(db));
            Assert.Equal(2, context.Groups.Count());
            Assert.Equal(6, context.Pages.Count());
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(work, recursive: true);
        }
    }

    private static int PendingMigrations(string db)
    {
        using var context = new FgScannerDbContext(DbBootstrapper.BuildOptions(db));
        return context.Database.GetPendingMigrations().Count();
    }

    /// <summary>Every group and page by id, with what identifies its content.</summary>
    private static List<string> Rows(string db)
    {
        using var connection = new SqliteConnection($"Data Source={db};Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT 'G|' || Id || '|' || Name || '|' || DirectoryPath FROM Groups
            UNION ALL
            SELECT 'P|' || Id || '|' || FileName || '|' || Checksum FROM Pages
            ORDER BY 1
            """;
        using var reader = command.ExecuteReader();
        var rows = new List<string>();
        while (reader.Read())
        {
            rows.Add(reader.GetString(0));
        }

        return rows;
    }

    private static string Scalar(string db, string sql)
    {
        using var connection = new SqliteConnection($"Data Source={db};Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToString(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture)!;
    }
}
