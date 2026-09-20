using System.IO;
using FgScanner.App.Views;
using FgScanner.Data;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FgScanner.App.Tests;

/// <summary>
/// The retention box was never loaded from storage: it was hard-initialised to 30 and written
/// back on every save, so an operator who set 365 lost it the next time they pressed Save — and
/// a year of recoverable deletions quietly became a month (SPEC-2026-004 §04 row 10).
/// </summary>
public sealed class TrashRetentionSettingTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "fgscanner-tests", Guid.NewGuid().ToString("N"));
    private readonly string _dbPath;
    private readonly ProfileService _profileService;
    private readonly GroupService _groupService;
    private readonly TrashService _trashService;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public TrashRetentionSettingTests()
    {
        Directory.CreateDirectory(_root);
        _dbPath = Path.Combine(_root, "test.db");
        using (var db = new FgScannerDbContext(DbBootstrapper.BuildOptions(_dbPath)))
        {
            db.Database.Migrate();
        }

        var factory = new TestFactory(_dbPath);
        _profileService = new ProfileService(factory);
        _groupService = new GroupService(factory);
        _trashService = new TrashService(factory, Path.Combine(_root, "trash"));
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private sealed class TestFactory(string dbPath) : IDbContextFactory<FgScannerDbContext>
    {
        public FgScannerDbContext CreateDbContext() => new(DbBootstrapper.BuildOptions(dbPath));
    }

    private SettingsViewModel CreateSettings() => new(
        _profileService, _trashService,
        new AppSettingsService(new TestFactory(_dbPath)),
        new FgScanner.Ocr.LanguageManager(Path.Combine(_root, "tessdata")),
        new FgScanner.Ai.CredentialStore(Path.Combine(_root, "cred"), useCredentialManager: false),
        _groupService);

    [Fact]
    public async Task The_stored_retention_is_shown_when_Settings_opens()
    {
        await _trashService.SetRetentionDaysAsync(365, Ct);

        var settings = CreateSettings();
        await settings.LoadRetentionAsync();

        Assert.Equal(365, settings.RetentionDays);
    }

    [Fact]
    public async Task Saving_without_touching_retention_leaves_the_stored_value_alone()
    {
        await _trashService.SetRetentionDaysAsync(365, Ct);
        var settings = CreateSettings();
        await settings.LoadRetentionAsync();

        settings.NewProfileName = "Cases";
        await settings.CreateProfileCommand.ExecuteAsync(null);
        await settings.SaveCommand.ExecuteAsync(null);

        Assert.Equal(365, await _trashService.GetRetentionDaysAsync(Ct));
    }

    [Fact]
    public async Task Changing_the_retention_stores_the_new_value()
    {
        await _trashService.SetRetentionDaysAsync(365, Ct);
        var settings = CreateSettings();
        await settings.LoadRetentionAsync();

        settings.NewProfileName = "Cases";
        await settings.CreateProfileCommand.ExecuteAsync(null);
        settings.RetentionDays = 90;
        await settings.SaveCommand.ExecuteAsync(null);

        Assert.Equal(90, await _trashService.GetRetentionDaysAsync(Ct));
    }
}
