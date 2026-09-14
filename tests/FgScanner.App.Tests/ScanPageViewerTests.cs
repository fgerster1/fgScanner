using System.IO;
using FgScanner.App.Services;
using FgScanner.App.Views;
using FgScanner.Data;
using FgScanner.Scanning;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FgScanner.App.Tests;

/// <summary>
/// Scanned pages can be checked full size before they are saved to a group, so a crooked or
/// double-fed page is caught while it can still be rescanned (SPEC-2026-003 AC-1).
/// </summary>
public sealed class ScanPageViewerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "fgscanner-tests", Guid.NewGuid().ToString("N"));
    private readonly string _dbPath;
    private readonly ScanSessionService _sessionService;
    private readonly GroupService _groupService;
    private readonly ProfileService _profileService;
    private readonly IndexingService _indexingService;
    private readonly TrashService _trashService;

    public ScanPageViewerTests()
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
        new OcrQueueService(new TestFactory(_dbPath)),
        new AiQueueService(new TestFactory(_dbPath)),
        new RetroProcessService(new TestFactory(_dbPath), _groupService, _trashService),
        new FgScanner.Ai.CredentialStore(Path.Combine(_root, "cred"), useCredentialManager: false),
        new AppSettingsService(new TestFactory(_dbPath)),
        new CaptureTriageService(new TestFactory(_dbPath), new AppSettingsService(new TestFactory(_dbPath))),
        new DuplicateFinder(new TestFactory(_dbPath)));

    private async Task<ScanViewModel> ScanFivePagesAsync()
    {
        var vm = new ScanViewModel(
            new FakeScanService { PageCount = 5 }, _sessionService, _groupService, _indexingService,
            new ActiveGroupStore(),
            new ProfileOcrTrigger(_profileService, new OcrQueueService(new TestFactory(_dbPath))),
            CreateToolset(), _trashService);
        await vm.RefreshDevicesCommand.ExecuteAsync(null);
        vm.Source = ScanSource.Feeder;
        await vm.ScanCommand.ExecuteAsync(null);
        Assert.Equal(5, vm.Pages.Count);
        return vm;
    }

    [Fact]
    public async Task Viewer_opens_at_the_chosen_staged_page()
    {
        var vm = await ScanFivePagesAsync();
        IReadOnlyList<string>? shown = null;
        var shownStart = -1;
        vm.ShowPageViewer = (paths, start) =>
        {
            shown = paths;
            shownStart = start;
            return start;
        };

        vm.OpenPageViewerCommand.Execute(vm.Pages[2]);

        Assert.Equal(vm.Pages.OrderBy(p => p.SequenceNumber).Select(p => p.FilePath), shown);
        Assert.Equal(2, shownStart);
    }

    [Fact]
    public async Task Enter_with_no_page_named_opens_at_the_first_selected_page()
    {
        var vm = await ScanFivePagesAsync();
        vm.SelectedPages.Add(vm.Pages[3]);
        var shownStart = -1;
        vm.ShowPageViewer = (_, start) => shownStart = start;

        vm.OpenPageViewerCommand.Execute(null);

        Assert.Equal(3, shownStart);
    }

    [Fact]
    public void There_is_nothing_to_view_before_anything_is_scanned()
    {
        var vm = new ScanViewModel(
            new FakeScanService(), _sessionService, _groupService, _indexingService, new ActiveGroupStore(),
            new ProfileOcrTrigger(_profileService, new OcrQueueService(new TestFactory(_dbPath))),
            CreateToolset(), _trashService);

        Assert.False(vm.OpenPageViewerCommand.CanExecute(null));
    }
}
