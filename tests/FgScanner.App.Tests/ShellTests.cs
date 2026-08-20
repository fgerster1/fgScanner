using System.IO;
using FgScanner.App.Services;
using FgScanner.App.Views;
using FgScanner.Data;
using FgScanner.Scanning;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FgScanner.App.Tests;

public sealed class ShellTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "fgscanner-tests", Guid.NewGuid().ToString("N"));
    private readonly ScanSessionService _sessionService;
    private readonly GroupService _groupService;
    private readonly ProfileService _profileService;
    private readonly IndexingService _indexingService;
    private readonly TrashService _trashService;
    private readonly ActiveGroupStore _activeGroup = new();
    private readonly string _dbPath;

    public ShellTests()
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

    private PageEditingToolset CreateToolset() => new(
        new FgScanner.Scanning.Editing.ImageEditor(),
        new FgScanner.Scanning.Export.PdfExportService(),
        new FgScanner.Scanning.Export.ImageExportService(),
        new FgScanner.Scanning.Import.FileImportService(),
        new ReorderService(new TestFactory(_dbPath)),
        new OcrQueueService(new TestFactory(_dbPath)));

    private ScanViewModel CreateScanViewModel(FakeScanService? service = null) =>
        new(service ?? new FakeScanService(), _sessionService, _groupService, _indexingService, _activeGroup,
            new ProfileOcrTrigger(_profileService, new OcrQueueService(new TestFactory(_dbPath))));

    [Fact]
    public void Shell_starts_on_scan_section()
    {
        var shell = new ShellViewModel(
            CreateScanViewModel(),
            new GroupsViewModel(_groupService, _profileService, _indexingService, _trashService, _activeGroup, CreateToolset()),
            new TrashViewModel(_trashService, _activeGroup),
            new SettingsViewModel(
                _profileService, _trashService,
                new AppSettingsService(new TestFactory(_dbPath)),
                new FgScanner.Ocr.LanguageManager(Path.Combine(_root, "tessdata"))));
        Assert.Equal(["Scan", "Groups", "Trash", "Settings"], shell.Sections);
        Assert.Equal("Scan", shell.SelectedSection);
    }

    [Fact]
    public async Task Scan_command_streams_pages_into_the_thumbnail_list()
    {
        var vm = CreateScanViewModel(new FakeScanService { PageCount = 3 });
        await vm.RefreshDevicesCommand.ExecuteAsync(null);
        Assert.NotNull(vm.SelectedDevice);

        vm.Source = ScanSource.Feeder;
        await vm.ScanCommand.ExecuteAsync(null);

        Assert.Equal(3, vm.Pages.Count);
        Assert.All(vm.Pages, p => Assert.True(File.Exists(p.FilePath)));
        Assert.False(vm.IsScanning);
        Assert.Contains("complete", vm.StatusText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Driver_failure_keeps_partial_pages_and_reports_error()
    {
        var vm = CreateScanViewModel(new FakeScanService
        {
            PageCount = 5,
            Error = new IOException("paper jam"),
            ErrorAfterPages = 2,
        });
        await vm.RefreshDevicesCommand.ExecuteAsync(null);
        vm.Source = ScanSource.Feeder;

        await vm.ScanCommand.ExecuteAsync(null);

        Assert.Equal(2, vm.Pages.Count);
        Assert.Contains("failed", vm.StatusText, StringComparison.OrdinalIgnoreCase);
        Assert.False(vm.IsScanning);
    }

    [Fact]
    public async Task Scan_then_save_to_group_moves_pages_into_group_and_survives_reload()
    {
        var group = await _groupService.CreateGroupAsync(_root, "Taxes", null, TestContext.Current.CancellationToken);
        _activeGroup.Current = group;
        var vm = CreateScanViewModel(new FakeScanService { PageCount = 2 });
        await vm.RefreshDevicesCommand.ExecuteAsync(null);
        vm.Source = ScanSource.Feeder;
        await vm.ScanCommand.ExecuteAsync(null);
        var sessionFolder = _sessionService.Session.FolderPath;

        await vm.SaveToGroupCommand.ExecuteAsync(null);

        Assert.Empty(vm.Pages);
        Assert.NotEqual(sessionFolder, _sessionService.Session.FolderPath); // fresh session
        Assert.True(File.Exists(Path.Combine(group.DirectoryPath, "scan_00001.png")));
        Assert.True(File.Exists(Path.Combine(group.DirectoryPath, "scan_00002.png")));

        // "Restart": a brand-new service over the same DB sees the group and its pages.
        var reloaded = new GroupService(new TestFactory(_dbPath));
        var groups = await reloaded.ListGroupsAsync(TestContext.Current.CancellationToken);
        var pages = await reloaded.GetPagesAsync(Assert.Single(groups).Id, TestContext.Current.CancellationToken);
        Assert.Equal(2, pages.Count);
    }

    [Fact]
    public void Recovered_pages_appear_in_the_list_at_startup()
    {
        var crashed = FgScanner.Scanning.Recovery.RecoverySession.Create(Path.Combine(_root, "recovery"));
        File.WriteAllBytes(crashed.ReserveNextPagePath("png"), [1]);
        crashed.CommitPage(new ScannedPage(Path.Combine(crashed.FolderPath, "page-00001.png"), 1));
        crashed.Flush();
        crashed.Dispose();

        var orphan = Assert.Single(_sessionService.FindOrphanedSessions());
        var recovered = _sessionService.RecoverInto(orphan);

        var vm = CreateScanViewModel();
        Assert.Single(recovered);
        Assert.Single(vm.Pages);
    }
}
