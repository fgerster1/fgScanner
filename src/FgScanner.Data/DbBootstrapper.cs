using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace FgScanner.Data;

/// <summary>
/// Startup database lifecycle: migrations applied automatically, with a file backup taken first
/// whenever a schema change is pending (unattended desktop upgrades have no DBA watching).
/// </summary>
public static class DbBootstrapper
{
    public static string DefaultDbPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "FGScanner", "fgscanner.db");

    public static DbContextOptions<FgScannerDbContext> BuildOptions(string dbPath) =>
        new DbContextOptionsBuilder<FgScannerDbContext>()
            .UseSqlite($"Data Source={dbPath}")
            .Options;

    /// <summary>Returns the backup path if a pre-migration backup was taken, else null.</summary>
    public static string? MigrateWithBackup(string dbPath, string appVersion)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);
        using var db = new FgScannerDbContext(BuildOptions(dbPath));
        db.Database.SetCommandTimeout(TimeSpan.FromMinutes(2));

        string? backupPath = null;
        if (File.Exists(dbPath) && db.Database.GetPendingMigrations().Any())
        {
            backupPath = $"{dbPath}.bak-{appVersion}";
            File.Copy(dbPath, backupPath, overwrite: true);
        }

        db.Database.Migrate();
        return backupPath;
    }

    /// <summary>Online backup via the SQLite backup API — safe while the app is running.</summary>
    public static void BackupDatabase(string dbPath, string destinationPath)
    {
        using var source = new SqliteConnection($"Data Source={dbPath}");
        using var destination = new SqliteConnection($"Data Source={destinationPath}");
        source.Open();
        destination.Open();
        source.BackupDatabase(destination);
    }
}
