using FgScanner.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FgScanner.Data.Tests;

public sealed class SchemaTests : IDisposable
{
    private readonly TestDb _db = new();

    public void Dispose() => _db.Dispose();

    private static Group NewGroup(string dir) => new()
    {
        Id = Guid.NewGuid(),
        Name = Path.GetFileName(dir),
        DirectoryPath = dir,
        CreatedUtc = DateTime.UtcNow,
        UpdatedUtc = DateTime.UtcNow,
    };

    private (Group Group, Document Doc, Page Page) SeedOnePage(string? ocrText = null)
    {
        using var db = _db.CreateContext();
        var group = NewGroup(Path.Combine(_db.Root, "Invoices"));
        var doc = new Document { Id = Guid.NewGuid(), GroupId = group.Id, Sequence = 1, CreatedUtc = DateTime.UtcNow };
        var page = new Page
        {
            Id = Guid.NewGuid(),
            DocumentId = doc.Id,
            FileName = "scan_00001.png",
            Checksum = "abc123",
            Sequence = 1,
            OcrText = ocrText,
            CreatedUtc = DateTime.UtcNow,
        };
        db.AddRange(group, doc, page);
        db.SaveChanges();
        return (group, doc, page);
    }

    [Fact]
    public void Migrated_database_passes_integrity_check()
    {
        using var connection = new SqliteConnection($"Data Source={_db.DbPath}");
        connection.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "PRAGMA integrity_check;";
        Assert.Equal("ok", cmd.ExecuteScalar());
    }

    [Fact]
    public void Views_exist_and_return_seeded_data_via_raw_sqlite()
    {
        SeedOnePage(ocrText: "hello invoice 4711");

        using var connection = new SqliteConnection($"Data Source={_db.DbPath}");
        connection.Open();
        using var cmd = connection.CreateCommand();

        cmd.CommandText = "SELECT GroupName, FirstImage, PageCount FROM v_index;";
        using (var reader = cmd.ExecuteReader())
        {
            Assert.True(reader.Read());
            Assert.Equal("Invoices", reader.GetString(0));
            Assert.Equal("scan_00001.png", reader.GetString(1));
            Assert.Equal(1, reader.GetInt32(2));
        }

        cmd.CommandText = "SELECT COUNT(*) FROM v_pages;";
        Assert.Equal(1L, cmd.ExecuteScalar());

        cmd.CommandText = "SELECT OcrText FROM v_ocr_text WHERE GroupName = 'Invoices';";
        Assert.Equal("hello invoice 4711", cmd.ExecuteScalar());
    }

    [Fact]
    public void Fts_finds_pages_by_ocr_text_and_tracks_updates()
    {
        var (_, _, page) = SeedOnePage(ocrText: "quarterly report alpha");

        using var connection = new SqliteConnection($"Data Source={_db.DbPath}");
        connection.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM PagesFts WHERE PagesFts MATCH 'quarterly';";
        Assert.Equal(1L, cmd.ExecuteScalar());

        // Update through EF; triggers must keep the index in sync.
        using (var db = _db.CreateContext())
        {
            var tracked = db.Pages.Single(p => p.Id == page.Id);
            tracked.OcrText = "replaced content beta";
            db.SaveChanges();
        }

        cmd.CommandText = "SELECT COUNT(*) FROM PagesFts WHERE PagesFts MATCH 'quarterly';";
        Assert.Equal(0L, cmd.ExecuteScalar());
        cmd.CommandText = "SELECT COUNT(*) FROM PagesFts WHERE PagesFts MATCH 'beta';";
        Assert.Equal(1L, cmd.ExecuteScalar());
    }

    [Fact]
    public void Duplicate_directory_path_is_rejected()
    {
        var dir = Path.Combine(_db.Root, "Same");
        using var db = _db.CreateContext();
        db.Groups.Add(NewGroup(dir));
        db.SaveChanges();
        db.Groups.Add(NewGroup(dir));
        Assert.Throws<DbUpdateException>(() => db.SaveChanges());
    }

    [Fact]
    public void Custom_fields_json_is_queryable_with_json_extract()
    {
        var (_, doc, _) = SeedOnePage();
        using (var db = _db.CreateContext())
        {
            var tracked = db.Documents.Single(d => d.Id == doc.Id);
            tracked.CustomFieldsJson = """{"Vendor":"Acme","Amount":42.5}""";
            db.SaveChanges();
        }

        using var connection = new SqliteConnection($"Data Source={_db.DbPath}");
        connection.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT json_extract(CustomFieldsJson, '$.Vendor') FROM Documents;";
        Assert.Equal("Acme", cmd.ExecuteScalar());
    }

    [Fact]
    public void Backup_before_migrate_copies_db_only_when_migrations_pend()
    {
        // Fresh DB is fully migrated → no backup expected.
        var backup = DbBootstrapper.MigrateWithBackup(_db.DbPath, "9.9.9");
        Assert.Null(backup);
    }

    [Fact]
    public void Online_backup_produces_an_openable_copy()
    {
        SeedOnePage();
        var backupPath = Path.Combine(_db.Root, "backup.db");

        DbBootstrapper.BackupDatabase(_db.DbPath, backupPath);

        using var connection = new SqliteConnection($"Data Source={backupPath}");
        connection.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM Pages;";
        Assert.Equal(1L, cmd.ExecuteScalar());
    }
}
