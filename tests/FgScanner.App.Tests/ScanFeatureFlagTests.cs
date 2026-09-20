using System.IO;
using FgScanner.App.Services;
using FgScanner.App.Views;
using FgScanner.Data;
using FgScanner.Scanning;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FgScanner.App.Tests;

/// <summary>
/// The Scan page reads its feature flags once, in the constructor. Patch-T detection itself is
/// read fresh at triage time, so only the separator-sheet button was frozen — which meant turning
/// the feature on left the operator with no way to print the sheet the feature needs
/// (SPEC-2026-004 §04 row 2).
/// </summary>
public sealed class ScanFeatureFlagTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "fgscanner-tests", Guid.NewGuid().ToString("N"));
    private readonly ScanSessionService _sessionService;
    private readonly GroupService _groupService;
    private readonly ProfileService _profileService;
    private readonly IndexingService _indexingService;
    private readonly TrashService _trashService;
    private readonly ActiveGroupStore _activeGroup = new();
    private readonly string _dbPath;

    public ScanFeatureFlagTests()
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

    [Fact]
    public async Task The_separator_sheet_button_follows_the_flag_without_a_restart()
    {
        var appSettings = new AppSettingsService(new TestFactory(_dbPath));
        await appSettings.SetAsync(
            FeatureFlags.PatchT, "false", TestContext.Current.CancellationToken);

        var scan = new ScanViewModel(
            new FakeScanService(), _sessionService, _groupService, _indexingService, _activeGroup,
            new ProfileOcrTrigger(_profileService, new OcrQueueService(new TestFactory(_dbPath))),
            CreateToolset(), _trashService);
        var settings = new SettingsViewModel(
            _profileService, _trashService, appSettings,
            new FgScanner.Ocr.LanguageManager(Path.Combine(_root, "tessdata")),
            new FgScanner.Ai.CredentialStore(Path.Combine(_root, "cred"), useCredentialManager: false),
            _groupService);
        _ = new ShellViewModel(
            scan,
            new GroupsViewModel(_groupService, _profileService, _indexingService, _trashService, _activeGroup, CreateToolset(), CreateRetroService()),
            new SearchViewModel(new SearchService(new TestFactory(_dbPath)), _groupService),
            new TrashViewModel(_trashService, _activeGroup),
            settings,
            appSettings);

        // The constructor starts its own load; settle it so the "before" state is not a race.
        await scan.LoadFeatureFlagsAsync();
        Assert.False(scan.SeparatorSheetVisible);

        settings.NewProfileName = "Cases";
        await settings.CreateProfileCommand.ExecuteAsync(null);
        settings.FeaturePatchT = true;
        await settings.SaveCommand.ExecuteAsync(null);

        Assert.True(scan.SeparatorSheetVisible);
    }
}
