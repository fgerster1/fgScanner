using FgScanner.Data;
using Microsoft.EntityFrameworkCore;

namespace FgScanner.Data.Tests;

/// <summary>A migrated file-backed database in a throwaway temp folder (FTS5/views need a real file).</summary>
public sealed class TestDb : IDisposable
{
    public TestDb()
    {
        Root = Path.Combine(Path.GetTempPath(), "fgscanner-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
        DbPath = Path.Combine(Root, "test.db");
        using var db = CreateContext();
        db.Database.Migrate();
    }

    public string Root { get; }

    public string DbPath { get; }

    public FgScannerDbContext CreateContext() => new(DbBootstrapper.BuildOptions(DbPath));

    public PooledFactory Factory => new(DbPath);

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(Root, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}

public sealed class PooledFactory(string dbPath) : IDbContextFactory<FgScannerDbContext>
{
    public FgScannerDbContext CreateDbContext() => new(DbBootstrapper.BuildOptions(dbPath));
}
