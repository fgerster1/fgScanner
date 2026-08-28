using FgScanner.Core.Index;
using FgScanner.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FgScanner.Data.Tests;

public sealed class BatchFieldColumnsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "fgscanner-tests", Guid.NewGuid().ToString("N"));

    public BatchFieldColumnsTests() => Directory.CreateDirectory(_root);

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

    /// <summary>
    /// Every field that existed before this phase must migrate as Row, or a schema the operator
    /// never touched would start answering from the group.
    /// </summary>
    [Fact]
    public void FieldDefinition_defaults_to_row_scope()
    {
        var field = new FieldDefinition { Name = "Title" };

        Assert.Equal(FieldScope.Row, field.Scope);
    }

    [Fact]
    public void Group_starts_with_an_empty_batch_bag()
    {
        var group = new Group { Name = "g", DirectoryPath = "d" };

        Assert.Equal("{}", group.BatchFieldsJson);
    }

    /// <summary>Null is "unknown provenance" and must stay distinguishable from an empty string.</summary>
    [Fact]
    public void Page_captured_by_starts_null()
    {
        var page = new Page { FileName = "a.jpg", Checksum = "abc" };

        Assert.Null(page.CapturedBy);
    }

    /// <summary>
    /// BatchFieldsJson is added to the pre-existing Groups table, so SQLite backfills every row
    /// that predates this migration from the column's SQL default — the CLR object-initializer
    /// default never runs for rows that already exist. A backfill of "" (the CLR default for
    /// string, absent an explicit HasDefaultValue) is invalid JSON, and Task 3 deserializes this
    /// column unconditionally: every group created before this phase would fail at export.
    /// </summary>
    [Fact]
    public void Pre_existing_group_backfills_batch_fields_as_valid_empty_json()
    {
        var dbPath = Path.Combine(_root, "premigration.db");
        var options = DbBootstrapper.BuildOptions(dbPath);

        using (var db = new FgScannerDbContext(options))
        {
            // Stop one migration short of this task's migration, so the Groups table on disk
            // still matches the schema a real pre-phase-19 installation would have.
            ((IInfrastructure<IServiceProvider>)db).Instance.GetRequiredService<IMigrator>()
                .Migrate("20260827134943_AddPageOriginalChecksum");
        }

        using (var connection = new SqliteConnection($"Data Source={dbPath}"))
        {
            connection.Open();
            using var insert = connection.CreateCommand();
            insert.CommandText = """
                INSERT INTO Groups (Id, Name, DirectoryPath, State, SchemaVersion, CreatedUtc, UpdatedUtc)
                VALUES ($id, 'PreMigration', 'C:\pre', 0, 0, '2026-01-01T00:00:00Z', '2026-01-01T00:00:00Z');
                """;
            insert.Parameters.AddWithValue("$id", Guid.NewGuid().ToString());
            insert.ExecuteNonQuery();
        }

        using (var db = new FgScannerDbContext(options))
        {
            db.Database.Migrate(); // applies this task's migration, backfilling the row above
        }

        using (var connection = new SqliteConnection($"Data Source={dbPath}"))
        {
            connection.Open();
            using var select = connection.CreateCommand();
            select.CommandText = "SELECT BatchFieldsJson FROM Groups WHERE Name = 'PreMigration';";
            Assert.Equal("{}", select.ExecuteScalar());
        }
    }
}
