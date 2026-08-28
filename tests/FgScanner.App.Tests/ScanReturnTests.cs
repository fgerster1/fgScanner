using System.IO;
using FgScanner.App.Services;
using FgScanner.App.Views;
using FgScanner.Data;
using FgScanner.Scanning;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FgScanner.App.Tests;

/// <summary>
/// "Scan into this group" was a one-way jump: the shell switched to the Scan section and nothing
/// ever switched back, so a gesture that started in Groups ended somewhere else with the pages
/// still unsaved. The return has to be conditional — a scan begun from the Scan section itself
/// must stay put — and it has to hang off the SAVE, not the scan, because pages are not in the
/// group until they are saved.
/// </summary>
public sealed class ScanReturnTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "fgscanner-tests", Guid.NewGuid().ToString("N"));
    private readonly ScanSessionService _sessionService;
    private readonly GroupService _groupService;
    private readonly ProfileService _profileService;
    private readonly IndexingService _indexingService;
    private readonly TrashService _trashService;
    private readonly ActiveGroupStore _activeGroup = new();
    private readonly string _dbPath;

    public ScanReturnTests()
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

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

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

    private ShellViewModel CreateShell(IScanService? scanService = null)
    {
        var settings = new AppSettingsService(new TestFactory(_dbPath));
        return new ShellViewModel(
            new ScanViewModel(
                scanService ?? new FakeScanService(), _sessionService, _groupService, _indexingService,
                _activeGroup,
                new ProfileOcrTrigger(_profileService, new OcrQueueService(new TestFactory(_dbPath))),
                CreateToolset(), _trashService),
            new GroupsViewModel(
                _groupService, _profileService, _indexingService, _trashService, _activeGroup,
                CreateToolset(), CreateRetroService()),
            new SearchViewModel(new SearchService(new TestFactory(_dbPath)), _groupService),
            new TrashViewModel(_trashService, _activeGroup),
            new SettingsViewModel(
                _profileService, _trashService, settings,
                new FgScanner.Ocr.LanguageManager(Path.Combine(_root, "tessdata")),
                new FgScanner.Ai.CredentialStore(Path.Combine(_root, "cred"), useCredentialManager: false),
                _groupService),
            settings);
    }

    /// <summary>Creates a group and selects it, as right-clicking one in the list does.</summary>
    private async Task<Group> SelectAGroupAsync(ShellViewModel shell)
    {
        var group = await _groupService.CreateGroupAsync(_root, "Batch1", null, Ct);
        await shell.GroupsViewModel.RefreshCommand.ExecuteAsync(null);
        shell.GroupsViewModel.TrySelectGroup(group.Id);
        return group;
    }

    private async Task<int> PagesInAsync(Guid groupId) =>
        (await _groupService.GetPagesAsync(groupId, Ct)).Count;

    [Fact]
    public async Task Scanning_into_a_group_lands_back_on_groups_with_the_pages_saved()
    {
        var shell = CreateShell();
        var group = await SelectAGroupAsync(shell);

        shell.GroupsViewModel.ScanIntoGroupCommand.Execute(null);
        Assert.Equal("Scan", shell.SelectedSection);
        await shell.ScanViewModel.ScanCommand.ExecuteAsync(null);

        Assert.Equal("Groups", shell.SelectedSection);
        Assert.True(await PagesInAsync(group.Id) > 0, "the scanned pages should have been saved");
        Assert.Empty(shell.ScanViewModel.Pages);
    }

    [Fact]
    public async Task An_ordinary_scan_stays_on_the_scan_screen()
    {
        // The same save runs, and must navigate nowhere: only a group-initiated scan returns.
        var shell = CreateShell();
        var group = await SelectAGroupAsync(shell);
        shell.SelectedSection = "Scan";

        await shell.ScanViewModel.ScanCommand.ExecuteAsync(null);
        await shell.ScanViewModel.SaveToGroupCommand.ExecuteAsync(null);

        Assert.Equal("Scan", shell.SelectedSection);
        Assert.True(await PagesInAsync(group.Id) > 0);
    }

    [Fact]
    public async Task An_ordinary_scan_does_not_save_itself()
    {
        // Auto-save is the group-initiated gesture's contract, not the Scan screen's: pages there
        // stay reviewable until the user saves them.
        var shell = CreateShell();
        var group = await SelectAGroupAsync(shell);
        shell.SelectedSection = "Scan";

        await shell.ScanViewModel.ScanCommand.ExecuteAsync(null);

        Assert.Equal(0, await PagesInAsync(group.Id));
        Assert.NotEmpty(shell.ScanViewModel.Pages);
    }

    [Fact]
    public async Task A_failed_scan_keeps_the_user_on_the_scan_screen()
    {
        // The error text lives in the Scan screen's status line and would be invisible from Groups.
        var shell = CreateShell(new ThrowingScanService());
        var group = await SelectAGroupAsync(shell);

        shell.GroupsViewModel.ScanIntoGroupCommand.Execute(null);
        await shell.ScanViewModel.ScanCommand.ExecuteAsync(null);

        Assert.Equal("Scan", shell.SelectedSection);
        Assert.Equal(0, await PagesInAsync(group.Id));
    }

    [Fact]
    public async Task Leaving_the_scan_screen_by_hand_abandons_the_return()
    {
        // Otherwise a save made much later, during an ordinary visit to Scan, yanks the user away.
        var shell = CreateShell();
        var group = await SelectAGroupAsync(shell);
        shell.GroupsViewModel.ScanIntoGroupCommand.Execute(null);

        shell.SelectedSection = "Settings";
        shell.SelectedSection = "Scan";
        await shell.ScanViewModel.ScanCommand.ExecuteAsync(null);

        Assert.Equal("Scan", shell.SelectedSection);
        Assert.Equal(0, await PagesInAsync(group.Id));
    }

    [Fact]
    public void Scanning_into_a_group_with_nothing_selected_does_nothing()
    {
        var shell = CreateShell();

        shell.GroupsViewModel.ScanIntoGroupCommand.Execute(null);

        Assert.Equal("Scan", shell.SelectedSection == "Groups" ? "Groups" : shell.SelectedSection);
        Assert.False(shell.ScanViewModel.AutoSaveAfterScan);
    }

    /// <summary>A scanner that fails mid-run, as an empty feeder or a paper jam does.</summary>
    private sealed class ThrowingScanService : IScanService
    {
        public IReadOnlyList<ScanDriver> AvailableDrivers { get; } = [ScanDriver.Wia];

        public Task<IReadOnlyList<ScanDeviceInfo>> ListDevicesAsync(
            ScanDriver driver, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ScanDeviceInfo>>([]);

        public async IAsyncEnumerable<ScannedPage> ScanAsync(
            ScanProfileOptions options,
            IPageStorage storage,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            throw new InvalidOperationException("The feeder is empty.");
#pragma warning disable CS0162 // unreachable, but makes this a valid iterator
            yield break;
#pragma warning restore CS0162
        }
    }
}
