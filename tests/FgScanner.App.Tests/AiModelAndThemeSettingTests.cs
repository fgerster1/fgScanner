using System.IO;
using FgScanner.App.Services;
using FgScanner.App.Views;
using FgScanner.Data;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FgScanner.App.Tests;

/// <summary>
/// Two settings that were not restart-gated so much as broken: the AI model was written only
/// inside the "validate and store the API key" path, so changing the model and pressing Save
/// persisted nothing; and the theme had no control at all, being written once by the first-run
/// wizard and never again (SPEC-2026-004 §04 rows 11 and 12).
/// </summary>
public sealed class AiModelAndThemeSettingTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "fgscanner-tests", Guid.NewGuid().ToString("N"));
    private readonly string _dbPath;
    private readonly ProfileService _profileService;
    private readonly GroupService _groupService;
    private readonly TrashService _trashService;
    private readonly AppSettingsService _appSettings;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public AiModelAndThemeSettingTests()
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
        _appSettings = new AppSettingsService(new TestFactory(_dbPath));
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
        _profileService, _trashService, _appSettings,
        new FgScanner.Ocr.LanguageManager(Path.Combine(_root, "tessdata")),
        new FgScanner.Ai.CredentialStore(Path.Combine(_root, "cred"), useCredentialManager: false),
        _groupService);

    private async Task<SettingsViewModel> SettingsWithAProfileAsync()
    {
        var settings = CreateSettings();
        settings.NewProfileName = "Cases";
        await settings.CreateProfileCommand.ExecuteAsync(null);
        return settings;
    }

    /// <summary>No API key is pasted here — that is the whole point of the bug.</summary>
    [Fact]
    public async Task The_AI_model_is_stored_by_the_Save_button()
    {
        var settings = await SettingsWithAProfileAsync();

        settings.AiModel = "gemini-flash-latest";
        await settings.SaveCommand.ExecuteAsync(null);

        Assert.Equal(
            "gemini-flash-latest",
            await _appSettings.GetAsync(AiWorker.ModelSettingKey, "", Ct));
    }

    [Fact]
    public async Task The_theme_is_stored_by_the_Save_button()
    {
        var settings = await SettingsWithAProfileAsync();

        settings.Theme = "dark";
        await settings.SaveCommand.ExecuteAsync(null);

        Assert.Equal("dark", await _appSettings.GetAsync(ThemeSetting.Key, "system", Ct));
    }

    [Fact]
    public async Task The_stored_theme_is_shown_when_Settings_opens()
    {
        await _appSettings.SetAsync(ThemeSetting.Key, "light", Ct);

        var settings = CreateSettings();
        await settings.LoadThemeAsync();

        Assert.Equal("light", settings.Theme);
    }
}
