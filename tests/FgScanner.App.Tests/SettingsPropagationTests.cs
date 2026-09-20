using System.IO;
using FgScanner.App.Services;
using FgScanner.App.Views;
using FgScanner.Data;
using FgScanner.Scanning;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FgScanner.App.Tests;

/// <summary>
/// A settings change has to reach the screen that uses it without the operator restarting
/// the program (SPEC-2026-004). Every test here mutates through <see cref="SettingsViewModel"/>
/// and then asserts on the SAME <see cref="GroupsViewModel"/> instance that existed before —
/// rebuilding the view model would prove nothing, because the section view models are
/// singletons that live for the whole session.
/// </summary>
public sealed class SettingsPropagationTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "fgscanner-tests", Guid.NewGuid().ToString("N"));
    private readonly ScanSessionService _sessionService;
    private readonly GroupService _groupService;
    private readonly ProfileService _profileService;
    private readonly IndexingService _indexingService;
    private readonly TrashService _trashService;
    private readonly ActiveGroupStore _activeGroup = new();
    private readonly string _dbPath;

    public SettingsPropagationTests()
    {
        Directory.CreateDirectory(_root);
        _dbPath = Path.Combine(_root, "test.db");
        using (var db = new FgScannerDbContext(DbBootstrapper.BuildOptions(_dbPath)))
        {
            db.Database.Migrate();
        }

        _sessionService = new ScanSessionService(Path.Combine(_root, "recovery"));
        var factory = new TestFactory(_dbPath);
        _groupService = new GroupService(factory);
        _profileService = new ProfileService(factory);
        _indexingService = new IndexingService(factory, _profileService, new FgScanner.Core.Index.IndexExporter());
        _trashService = new TrashService(factory, Path.Combine(_root, "trash"));
    }

    public void Dispose()
    {
        _sessionService.Dispose();
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

    private RetroProcessService CreateRetroService() => new(
        new TestFactory(_dbPath), _groupService, _trashService);

    private CaptureTriageService CreateTriageService() => new(
        new TestFactory(_dbPath), new AppSettingsService(new TestFactory(_dbPath)));

    private PageEditingToolset CreateToolset() => new(
        new FgScanner.Scanning.Editing.ImageEditor(),
        new FgScanner.Scanning.Export.PdfExportService(),
        new FgScanner.Scanning.Export.ImageExportService(),
        new FgScanner.Scanning.Import.FileImportService(),
        new ReorderService(new TestFactory(_dbPath)),
        new OcrQueueService(new TestFactory(_dbPath)),
        new AiQueueService(new TestFactory(_dbPath)),
        CreateRetroService(),
        new FgScanner.Ai.CredentialStore(Path.Combine(_root, "cred"), useCredentialManager: false),
        new AppSettingsService(new TestFactory(_dbPath)),
        CreateTriageService(),
        new DuplicateFinder(new TestFactory(_dbPath)));

    /// <summary>
    /// The shell is what wires Settings to the other sections, the same way it already wires the
    /// "scan into this group" round trip — so the wiring can be exercised without a window.
    /// </summary>
    private async Task<(GroupsViewModel Groups, SettingsViewModel Settings)> CreateWiredShellAsync()
    {
        var appSettings = new AppSettingsService(new TestFactory(_dbPath));
        var groups = new GroupsViewModel(
            _groupService, _profileService, _indexingService, _trashService, _activeGroup,
            CreateToolset(), CreateRetroService());
        var settings = new SettingsViewModel(
            _profileService, _trashService, appSettings,
            new FgScanner.Ocr.LanguageManager(Path.Combine(_root, "tessdata")),
            new FgScanner.Ai.CredentialStore(Path.Combine(_root, "cred"), useCredentialManager: false),
            _groupService);

        _ = new ShellViewModel(
            new ScanViewModel(
                new FakeScanService(), _sessionService, _groupService, _indexingService, _activeGroup,
                new ProfileOcrTrigger(_profileService, new OcrQueueService(new TestFactory(_dbPath))),
                CreateToolset(), _trashService),
            groups,
            new SearchViewModel(new SearchService(new TestFactory(_dbPath)), _groupService),
            new TrashViewModel(_trashService, _activeGroup),
            settings,
            appSettings);

        // The constructors kick off their own loads; settle them so the "before" state is known
        // and the assertions are about the change under test, not about startup timing.
        await _profileService.EnsureDefaultAsync();
        await groups.ReloadProfilesAsync();
        return (groups, settings);
    }

    [Fact]
    public async Task A_new_profile_reaches_the_Groups_list_without_rebuilding_the_view_model()
    {
        var (groups, settings) = await CreateWiredShellAsync();
        Assert.DoesNotContain(groups.Profiles, p => p.Name == "Cases");

        settings.NewProfileName = "Cases";
        await settings.CreateProfileCommand.ExecuteAsync(null);

        Assert.Contains(groups.Profiles, p => p.Name == "Cases");
    }

    [Fact]
    public async Task A_renamed_profile_shows_its_new_name_in_the_Groups_list()
    {
        var (groups, settings) = await CreateWiredShellAsync();
        settings.NewProfileName = "Cases";
        await settings.CreateProfileCommand.ExecuteAsync(null);

        settings.NewProfileName = "Case files";
        await settings.RenameProfileCommand.ExecuteAsync(null);

        Assert.Contains(groups.Profiles, p => p.Name == "Case files");
        Assert.DoesNotContain(groups.Profiles, p => p.Name == "Cases");
    }

    [Fact]
    public async Task The_Evidence_profile_is_selectable_in_Groups_as_soon_as_it_is_built()
    {
        var (groups, settings) = await CreateWiredShellAsync();

        await settings.CreateEvidenceProfileCommand.ExecuteAsync(null);

        Assert.Contains(groups.Profiles, p => p.Name == ProfileService.EvidenceProfileName);
    }

    /// <summary>
    /// Creating a group reads BaseDirectory off the Profile entity the Groups list is holding
    /// (GroupsViewModel.CreateGroupAsync). That entity came from a context disposed at startup, so
    /// a base folder changed in Settings was invisible until the next launch.
    /// </summary>
    [Fact]
    public async Task A_base_folder_changed_in_Settings_reaches_the_Groups_view_model()
    {
        var (groups, settings) = await CreateWiredShellAsync();
        settings.NewProfileName = "Cases";
        await settings.CreateProfileCommand.ExecuteAsync(null);

        var profileId = groups.Profiles.Single(p => p.Name == "Cases").Id;
        var folder = Path.Combine(_root, "case-work");
        await _profileService.UpdateBaseDirectoryAsync(
            profileId, folder, TestContext.Current.CancellationToken);
        await groups.ReloadProfilesAsync();
        Assert.Equal(folder, groups.Profiles.Single(p => p.Id == profileId).BaseDirectory);

        groups.SelectedProfile = groups.Profiles.Single(p => p.Id == profileId);
        settings.SelectedProfile = settings.Profiles.Single(p => p.Id == profileId);
        await settings.ClearBaseDirectoryCommand.ExecuteAsync(null);

        Assert.Equal("", groups.Profiles.Single(p => p.Id == profileId).BaseDirectory);
    }
}
