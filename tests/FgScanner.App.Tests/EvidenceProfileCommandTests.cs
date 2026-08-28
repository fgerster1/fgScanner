using System.IO;
using FgScanner.App.Services;
using FgScanner.App.Views;
using FgScanner.Core.Evidence;
using FgScanner.Data;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FgScanner.App.Tests;

/// <summary>
/// The Evidence profile's thirteen field names are parsed by the JimsStuff
/// importer, which cannot tell a misspelled field from an absent one. Typing
/// them by hand made one typo a silent break in a legal pipeline, so the
/// operator gets a button instead.
/// </summary>
public sealed class EvidenceProfileCommandTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "fgscanner-tests", Guid.NewGuid().ToString("N"));
    private readonly string _dbPath;
    private readonly ProfileService _profileService;
    private readonly GroupService _groupService;
    private readonly TrashService _trashService;

    public EvidenceProfileCommandTests()
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
            // A held file handle must not fail a passing test.
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
    public async Task The_button_builds_the_whole_field_contract()
    {
        var settings = CreateSettings();

        await settings.CreateEvidenceProfileCommand.ExecuteAsync(null);

        var profiles = await _profileService.ListAsync(TestContext.Current.CancellationToken);
        var evidence = profiles.Single(p => p.Name == ProfileService.EvidenceProfileName);
        var schema = await _profileService.GetLatestSchemaAsync(
            evidence.Id, TestContext.Current.CancellationToken);
        Assert.Equal(
            EvidenceProfile.Fields.Select(f => f.Name),
            schema.Fields.OrderBy(f => f.Order).Select(f => f.Name));
    }

    [Fact]
    public async Task The_profile_is_selected_so_the_operator_can_see_it_worked()
    {
        var settings = CreateSettings();

        await settings.CreateEvidenceProfileCommand.ExecuteAsync(null);

        Assert.Equal(ProfileService.EvidenceProfileName, settings.SelectedProfile?.Name);
    }

    /// <summary>
    /// Pressing it again is how somebody repairs a profile they have edited by
    /// hand, so it must be safe -- and must not mint a schema version, which
    /// would leave every existing group a version behind for no change.
    /// </summary>
    [Fact]
    public async Task Pressing_it_twice_repairs_rather_than_duplicates()
    {
        var settings = CreateSettings();
        await settings.CreateEvidenceProfileCommand.ExecuteAsync(null);
        var first = await _profileService.GetLatestSchemaAsync(
            settings.SelectedProfile!.Id, TestContext.Current.CancellationToken);

        await settings.CreateEvidenceProfileCommand.ExecuteAsync(null);

        var profiles = await _profileService.ListAsync(TestContext.Current.CancellationToken);
        Assert.Single(profiles, p => p.Name == ProfileService.EvidenceProfileName);
        var second = await _profileService.GetLatestSchemaAsync(
            settings.SelectedProfile!.Id, TestContext.Current.CancellationToken);
        Assert.Equal(first.Version, second.Version);
    }
}
