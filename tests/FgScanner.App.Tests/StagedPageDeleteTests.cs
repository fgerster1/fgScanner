using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Text.Json;
using FgScanner.App.Services;
using FgScanner.App.Views;
using FgScanner.Data;
using FgScanner.Scanning;
using FgScanner.Scanning.Recovery;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FgScanner.App.Tests;

/// <summary>
/// A bad scan can be deleted on the Scan page before it is saved into a group, instead of being
/// trashed from the group afterwards (SPEC-2026-003 AC-3..AC-5, AC-7).
/// </summary>
public sealed class StagedPageDeleteTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "fgscanner-tests", Guid.NewGuid().ToString("N"));
    private readonly string _dbPath;
    private readonly ScanSessionService _sessionService;
    private readonly GroupService _groupService;
    private readonly ProfileService _profileService;
    private readonly IndexingService _indexingService;
    private readonly TrashService _trashService;
    private readonly ActiveGroupStore _activeGroup = new();
    private readonly FakeDiscarder _discarder = new();

    public StagedPageDeleteTests()
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

    private sealed class FakeDiscarder : IStagedPageDiscarder
    {
        public List<string> Discarded { get; } = [];

        public HashSet<string> Refused { get; } = new(StringComparer.OrdinalIgnoreCase);

        public bool TryDiscard(string sessionFolder, string filePath, out string reason)
        {
            if (Refused.Contains(filePath))
            {
                reason = "The file is in use by another process.";
                return false;
            }

            Discarded.Add(filePath);
            reason = "";
            return true;
        }
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
            new FakeScanService { PageCount = 5 }, _sessionService, _groupService, _indexingService, _activeGroup,
            new ProfileOcrTrigger(_profileService, new OcrQueueService(new TestFactory(_dbPath))),
            CreateToolset(), _trashService, _discarder)
        {
            ConfirmDelete = _ => true,
        };
        await vm.RefreshDevicesCommand.ExecuteAsync(null);
        vm.Source = ScanSource.Feeder;
        await vm.ScanCommand.ExecuteAsync(null);
        Assert.Equal(5, vm.Pages.Count);
        return vm;
    }

    private RecoveryIndex ReadRecoveryIndex() =>
        JsonSerializer.Deserialize<RecoveryIndex>(
            File.ReadAllText(Path.Combine(_sessionService.Session.FolderPath, RecoverySession.IndexFileName)))!;

    [Fact]
    public async Task Deleting_two_of_five_leaves_three_on_screen_and_in_the_recovery_index()
    {
        var vm = await ScanFivePagesAsync();
        ScannedPage[] doomed = [vm.Pages[1], vm.Pages[3]];
        foreach (var page in doomed)
        {
            vm.SelectedPages.Add(page);
        }

        string? asked = null;
        vm.ConfirmDelete = message =>
        {
            asked = message;
            return true;
        };

        vm.DeleteSelectedPagesCommand.Execute(null);

        Assert.Contains("2 scanned pages", asked);
        Assert.Equal(3, vm.Pages.Count);
        Assert.All(doomed, page => Assert.DoesNotContain(page, vm.Pages));
        Assert.Equal(doomed.Select(p => p.FilePath), _discarder.Discarded);
        Assert.Equal(
            vm.Pages.Select(p => Path.GetFileName(p.FilePath)),
            ReadRecoveryIndex().Pages.Select(p => p.FileName));
        Assert.Empty(vm.SelectedPages);
    }

    [Fact]
    public async Task Declining_the_confirmation_changes_nothing()
    {
        var vm = await ScanFivePagesAsync();
        vm.SelectedPages.Add(vm.Pages[0]);
        vm.ConfirmDelete = _ => false;

        vm.DeleteSelectedPagesCommand.Execute(null);

        Assert.Equal(5, vm.Pages.Count);
        Assert.Empty(_discarder.Discarded);
        Assert.Equal(5, ReadRecoveryIndex().Pages.Count);
    }

    [Fact]
    public async Task Delete_is_unavailable_with_nothing_selected()
    {
        var vm = await ScanFivePagesAsync();

        Assert.False(vm.DeleteSelectedPagesCommand.CanExecute(null));
    }

    [Fact]
    public async Task Delete_is_unavailable_while_scanning()
    {
        var vm = await ScanFivePagesAsync();
        vm.SelectedPages.Add(vm.Pages[0]);
        Assert.True(vm.DeleteSelectedPagesCommand.CanExecute(null));

        vm.IsScanning = true;

        Assert.False(vm.DeleteSelectedPagesCommand.CanExecute(null));
    }

    [Fact]
    public async Task A_page_the_recycle_bin_refuses_is_named_and_the_others_still_go()
    {
        var vm = await ScanFivePagesAsync();
        var refused = vm.Pages[1];
        _discarder.Refused.Add(refused.FilePath);
        vm.SelectedPages.Add(vm.Pages[0]);
        vm.SelectedPages.Add(refused);
        vm.SelectedPages.Add(vm.Pages[2]);

        vm.DeleteSelectedPagesCommand.Execute(null);

        Assert.Equal(2, _discarder.Discarded.Count);
        Assert.Equal(2, vm.Pages.Count);
        Assert.DoesNotContain(refused, vm.Pages);
        Assert.Contains(Path.GetFileName(refused.FilePath), vm.StatusText);
        Assert.Equal(2, ReadRecoveryIndex().Pages.Count);
    }

    [Fact]
    public async Task Save_to_group_after_a_delete_adopts_only_the_remaining_pages()
    {
        var ct = TestContext.Current.CancellationToken;
        _activeGroup.Current = await _groupService.CreateGroupAsync(_root, "Box 12", null, ct);
        var vm = await ScanFivePagesAsync();
        vm.SelectedPages.Add(vm.Pages[4]);
        vm.DeleteSelectedPagesCommand.Execute(null);

        await vm.SaveToGroupCommand.ExecuteAsync(null);

        var pages = await _groupService.GetPagesAsync(_activeGroup.Current.Id, ct);
        Assert.Equal(4, pages.Count);
    }

    /// <summary>
    /// Adoption moves the files into the group, so a delete that lands mid-save recycles nothing and
    /// the page the operator deleted is in the group anyway. The save after "Scan into this group"
    /// runs with IsScanning already false, so the scanning guard alone does not cover it.
    /// </summary>
    [Fact]
    public async Task Delete_is_unavailable_while_pages_are_being_saved_to_a_group()
    {
        var ct = TestContext.Current.CancellationToken;
        _activeGroup.Current = await _groupService.CreateGroupAsync(_root, "Box 12", null, ct);
        var vm = await ScanFivePagesAsync();
        vm.SelectedPages.Add(vm.Pages[0]);
        bool? canDeleteMidSave = null;
        _activeGroup.GroupContentChanged += () => canDeleteMidSave = vm.DeleteSelectedPagesCommand.CanExecute(null);

        await vm.SaveToGroupCommand.ExecuteAsync(null);

        Assert.False(canDeleteMidSave);
        Assert.True(vm.DeleteSelectedPagesCommand.CanExecute(null)); // the guard lifts once the save is over
    }

    [Fact]
    public async Task A_recovery_index_that_cannot_be_written_deletes_nothing_and_says_so()
    {
        var vm = await ScanFivePagesAsync();
        vm.SelectedPages.Add(vm.Pages[0]);
        var indexPath = Path.Combine(_sessionService.Session.FolderPath, RecoverySession.IndexFileName);

        using (new FileStream(indexPath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            vm.DeleteSelectedPagesCommand.Execute(null);
        }

        Assert.Equal(5, vm.Pages.Count);
        Assert.Empty(_discarder.Discarded);
        Assert.Contains("Nothing was deleted", vm.StatusText);
    }

    [Fact]
    public void Page_labels_close_the_gap_after_a_delete()
    {
        var first = new ScannedPage(@"C:\session\page-00001.png", 1);
        var second = new ScannedPage(@"C:\session\page-00002.png", 2);
        var pages = new ObservableCollection<ScannedPage> { first, second };

        Assert.Equal("Page 2", Label(second, pages));

        pages.Remove(first);

        Assert.Equal("Page 1", Label(second, pages));
    }

    private static object Label(ScannedPage page, ObservableCollection<ScannedPage> pages) =>
        PagePositionConverter.Instance.Convert([page, pages, pages.Count], typeof(string), null, CultureInfo.InvariantCulture);
}
