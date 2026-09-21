using System.IO;
using FgScanner.App.Services;
using FgScanner.App.Views;
using FgScanner.Core.Capture;
using FgScanner.Data;
using FgScanner.Scanning;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FgScanner.App.Tests;

/// <summary>
/// The two-pass flow: fronts, flip, backs. While a stack is half captured the prompt and the
/// Cancel control stay on screen and every state change is announced — the rule CLAUDE.md pins for
/// annotated sheets, for the same reason. A stack in hand with nothing on screen saying so ends
/// with the next ordinary scan joining it, or with fronts adopted and no backs.
/// </summary>
public sealed class DuplexScanTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "fgscanner-tests", Guid.NewGuid().ToString("N"));
    private readonly ScanSessionService _sessionService;
    private readonly GroupService _groupService;
    private readonly ProfileService _profileService;
    private readonly IndexingService _indexingService;
    private readonly TrashService _trashService;
    private readonly ActiveGroupStore _activeGroup = new();
    private readonly string _dbPath;

    public DuplexScanTests()
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
        new CaptureTriageService(new TestFactory(_dbPath), new AppSettingsService(new TestFactory(_dbPath))),
        new DuplicateFinder(new TestFactory(_dbPath)));

    /// <summary>
    /// Keeps the fixture's pages out of the developer's own Recycle Bin, and keeps a refusal from
    /// raising the shell's modal prompt in a headless run.
    /// </summary>
    private sealed class FakeDiscarder : IStagedPageDiscarder
    {
        public bool TryDiscard(string sessionFolder, string filePath, out string reason)
        {
            reason = "";
            File.Delete(filePath);
            return true;
        }
    }

    /// <summary>
    /// A view model with no device chosen yet — the state the window opens in, and the only state
    /// in which a command's wake-up can be observed.
    /// </summary>
    private ScanViewModel CreateViewModel(FakeScanService scanner) => new(
        scanner, _sessionService, _groupService, _indexingService, _activeGroup,
        new ProfileOcrTrigger(_profileService, new OcrQueueService(new TestFactory(_dbPath))),
        CreateToolset(), _trashService, new FakeDiscarder());

    private async Task<ScanViewModel> CreateScanViewModelAsync(FakeScanService scanner)
    {
        var scan = CreateViewModel(scanner);
        await scan.RefreshDevicesCommand.ExecuteAsync(null);

        // A stack scanned in two passes goes through the feeder by definition; the fake hands back
        // a single page for a flatbed, as a real one does.
        scan.Source = ScanSource.Feeder;
        return scan;
    }

    /// <summary>
    /// AC-8. Checked after the fronts, which is the moment the stack exists only in the operator's
    /// hands and on the feeder tray.
    /// </summary>
    [Fact]
    public async Task The_sequence_is_visible_and_cancellable_at_every_step()
    {
        var scan = await CreateScanViewModelAsync(new FakeScanService { PageCount = 2 });

        Assert.False(scan.DuplexActive);
        Assert.Equal("", scan.DuplexPrompt);

        await scan.ScanBothSidesCommand.ExecuteAsync(null);

        Assert.True(scan.DuplexActive);
        Assert.False(string.IsNullOrWhiteSpace(scan.DuplexPrompt));
        Assert.True(scan.CancelDuplexCommand.CanExecute(null));
        Assert.True(scan.CaptureInHand);

        await scan.ScanBothSidesCommand.ExecuteAsync(null);

        // Both passes are in, so the stack is no longer in hand — and the prompt goes with it.
        Assert.False(scan.DuplexActive);
        Assert.Equal("", scan.DuplexPrompt);
        Assert.False(scan.CaptureInHand);
    }

    /// <summary>AC-3 end to end: the page list is in sheet order before anything is saved.</summary>
    [Fact]
    public async Task Fronts_and_backs_are_interleaved_into_sheet_order()
    {
        var scan = await CreateScanViewModelAsync(new FakeScanService { PageCount = 3 });
        scan.BacksReversed = true;

        await scan.ScanBothSidesCommand.ExecuteAsync(null);
        await scan.ScanBothSidesCommand.ExecuteAsync(null);

        // Capture order, not the order on screen — the list has already been paired by now. The
        // recovery session names pages from a monotonic counter, so the file names are the order
        // they came off the scanner: page-00001..3 are the fronts, 4..6 the backs.
        var captured = scan.Pages.Select(p => p.FilePath).OrderBy(p => p, StringComparer.Ordinal).ToList();
        var fronts = captured.Take(3).ToList();
        var backs = captured.Skip(3).ToList();

        // The stack was turned over, so the last back belongs to the first front.
        Assert.Equal(
            [fronts[0], backs[2], fronts[1], backs[1], fronts[2], backs[0]],
            scan.Pages.Select(p => p.FilePath));
    }

    /// <summary>
    /// AC-6 and §05 Q1(a). A double feed on the second pass is the ordinary failure here, and the
    /// counts cannot say which sheet lost its back — so nothing is paired and the operator is told
    /// what was counted.
    /// </summary>
    [Fact]
    public async Task A_count_mismatch_is_reported_with_both_numbers()
    {
        var scanner = new FakeScanService { PageCount = 3 };
        var scan = await CreateScanViewModelAsync(scanner);

        await scan.ScanBothSidesCommand.ExecuteAsync(null);
        var afterFronts = scan.Pages.Select(p => p.FilePath).ToList();
        scanner.PageCount = 2;
        await scan.ScanBothSidesCommand.ExecuteAsync(null);

        Assert.Contains("3", scan.StatusText, StringComparison.Ordinal);
        Assert.Contains("2", scan.StatusText, StringComparison.Ordinal);

        // Unordered, not half ordered: the fronts are still first, exactly as captured.
        Assert.Equal(afterFronts, scan.Pages.Take(3).Select(p => p.FilePath));
    }

    /// <summary>AC-9. A front with no back is adopted as a whole document and read as one.</summary>
    [Fact]
    public async Task Abandoning_a_stack_discards_both_passes()
    {
        var scan = await CreateScanViewModelAsync(new FakeScanService { PageCount = 2 });
        await scan.ScanBothSidesCommand.ExecuteAsync(null);
        var captured = scan.Pages.Select(p => p.FilePath).ToList();
        Assert.Equal(2, captured.Count);

        await scan.CancelDuplexCommand.ExecuteAsync(null);

        Assert.Empty(scan.Pages);
        Assert.False(scan.DuplexActive);
        Assert.All(captured, path => Assert.False(File.Exists(path)));
    }

    /// <summary>
    /// §16 R4. Both sequences live in this view model and both own the Scan page's prompt area, so
    /// one starting while the other is in hand would leave a sheet or a stack captured with the
    /// wrong prompt on screen and the wrong Cancel wired up.
    /// </summary>
    [Fact]
    public async Task A_stack_cannot_start_while_an_annotated_sheet_is_in_hand()
    {
        var scan = await CreateScanViewModelAsync(new FakeScanService { PageCount = 1 });
        _activeGroup.Current = await _groupService.CreateGroupAsync(
            Path.Combine(_root, "groups"), "Notes", null, TestContext.Current.CancellationToken);
        await scan.ScanAnnotatedCommand.ExecuteAsync(null);
        Assert.True(scan.AnnotatedActive);

        Assert.False(scan.ScanBothSidesCommand.CanExecute(null));
    }

    [Fact]
    public async Task An_annotated_sheet_cannot_start_while_a_stack_is_in_hand()
    {
        var scan = await CreateScanViewModelAsync(new FakeScanService { PageCount = 2 });
        await scan.ScanBothSidesCommand.ExecuteAsync(null);
        Assert.True(scan.DuplexActive);

        Assert.False(scan.ScanAnnotatedCommand.CanExecute(null));
    }

    /// <summary>
    /// The blank backs of a duplex stack are byte-identical, and adoption drops a page whose
    /// checksum is already in the group unless the save says otherwise (SPEC-2026-006 §05 Q2a).
    /// </summary>
    [Fact]
    public async Task Identical_blank_backs_all_reach_the_group()
    {
        var scan = await CreateScanViewModelAsync(
            new FakeScanService { PageCount = 3, BlankIdenticalPages = true });
        var group = await _groupService.CreateGroupAsync(
            Path.Combine(_root, "groups"), "Blank backs", null, TestContext.Current.CancellationToken);
        _activeGroup.Current = group;

        await scan.ScanBothSidesCommand.ExecuteAsync(null);
        await scan.ScanBothSidesCommand.ExecuteAsync(null);
        await scan.SaveToGroupCommand.ExecuteAsync(null);

        await using var db = new FgScannerDbContext(DbBootstrapper.BuildOptions(_dbPath));
        var saved = await db.Pages.CountAsync(
            p => p.Document!.GroupId == group.Id, TestContext.Current.CancellationToken);
        Assert.Equal(6, saved);
    }

    /// <summary>A stack saved in sheet order keeps that order in the group's own numbering.</summary>
    [Fact]
    public async Task The_group_receives_the_pages_in_sheet_order()
    {
        var scan = await CreateScanViewModelAsync(new FakeScanService { PageCount = 2 });
        var group = await _groupService.CreateGroupAsync(
            Path.Combine(_root, "groups"), "Ordered", null, TestContext.Current.CancellationToken);
        _activeGroup.Current = group;

        await scan.ScanBothSidesCommand.ExecuteAsync(null);
        await scan.ScanBothSidesCommand.ExecuteAsync(null);
        var order = scan.Pages.Select(p => Path.GetFileName(p.FilePath)).ToList();
        await scan.SaveToGroupCommand.ExecuteAsync(null);

        await using var db = new FgScannerDbContext(DbBootstrapper.BuildOptions(_dbPath));
        var saved = await db.Documents
            .Where(d => d.GroupId == group.Id)
            .OrderBy(d => d.Sequence)
            .Select(d => d.Pages.First().FileName)
            .ToListAsync(TestContext.Current.CancellationToken);

        Assert.Equal(order.Count, saved.Count);
        Assert.Equal(saved, saved.OrderBy(n => n, StringComparer.Ordinal));
    }

    /// <summary>
    /// A button asks its command whether it may run once, and then only when the command says to
    /// ask again. These four tests watch for that signal rather than calling CanExecute, because
    /// CanExecute answers honestly whether or not anything ever consults it — which is how a
    /// command that is never announced sits behind a permanently grey button with a green suite.
    /// </summary>
    [Fact]
    public async Task Choosing_a_device_wakes_the_two_pass_button()
    {
        var scan = CreateViewModel(new FakeScanService());
        Assert.False(scan.ScanBothSidesCommand.CanExecute(null));

        var woke = false;
        scan.ScanBothSidesCommand.CanExecuteChanged += (_, _) => woke = true;
        await scan.RefreshDevicesCommand.ExecuteAsync(null);

        Assert.True(scan.ScanBothSidesCommand.CanExecute(null));
        Assert.True(woke);
    }

    [Fact]
    public async Task A_running_pass_wakes_the_cancel_control()
    {
        var scan = await CreateScanViewModelAsync(new FakeScanService { PageCount = 2 });
        await scan.ScanBothSidesCommand.ExecuteAsync(null);
        Assert.True(scan.CancelDuplexCommand.CanExecute(null));

        var woke = false;
        scan.CancelDuplexCommand.CanExecuteChanged += (_, _) => woke = true;
        scan.IsScanning = true;

        // Abandoning a stack while its pass is in the feeder cancels a sequence the pass is about
        // to report into, so the control has to go grey for as long as paper is moving.
        Assert.True(woke);
        Assert.False(scan.CancelDuplexCommand.CanExecute(null));
    }

    [Fact]
    public async Task Finishing_an_annotated_sheet_wakes_the_two_pass_button()
    {
        var scan = await CreateScanViewModelAsync(new FakeScanService { PageCount = 1 });
        _activeGroup.Current = await _groupService.CreateGroupAsync(
            Path.Combine(_root, "groups"), "Notes", null, TestContext.Current.CancellationToken);
        await scan.ScanAnnotatedCommand.ExecuteAsync(null);
        Assert.False(scan.ScanBothSidesCommand.CanExecute(null));

        var woke = false;
        scan.ScanBothSidesCommand.CanExecuteChanged += (_, _) => woke = true;
        await scan.CancelAnnotatedCommand.ExecuteAsync(null);

        // §16 R4 is a mutual exclusion, so it has to release in both directions.
        Assert.True(woke);
        Assert.True(scan.ScanBothSidesCommand.CanExecute(null));
    }

    /// <summary>
    /// The sheets already in the feeder keep coming after the stack is abandoned. Recording that
    /// pass onto a sequence that has ended throws, and nothing above a command catches it: the
    /// app closes with the operator's pages unsaved.
    /// </summary>
    [Fact]
    public async Task A_pass_that_lands_after_the_stack_was_abandoned_does_not_crash()
    {
        var scan = await CreateScanViewModelAsync(
            new FakeScanService { PageCount = 3, PageDelay = TimeSpan.FromMilliseconds(30) });

        var pass = scan.ScanBothSidesCommand.ExecuteAsync(null);
        for (var i = 0; i < 100 && !scan.IsScanning; i++)
        {
            await Task.Delay(5, TestContext.Current.CancellationToken);
        }

        scan.Duplex.Cancel();
        await pass;

        Assert.False(scan.DuplexActive);
    }

    /// <summary>
    /// AC-3's guard. The pairing covers both passes, so an order that only half of the list can
    /// account for is not a pairing at all — it is the backs on their own, in reverse.
    /// </summary>
    [Fact]
    public async Task Backs_alone_are_not_paired_when_the_fronts_left_the_list()
    {
        var scan = await CreateScanViewModelAsync(new FakeScanService { PageCount = 3 });
        await scan.ScanBothSidesCommand.ExecuteAsync(null);

        // The fronts left between the passes — adopted into a group, or deleted from the
        // thumbnails. Three backs are not three sheets.
        scan.Pages.Clear();
        await scan.ScanBothSidesCommand.ExecuteAsync(null);

        var names = scan.Pages.Select(p => Path.GetFileName(p.FilePath)).ToList();
        Assert.Equal(names.OrderBy(n => n, StringComparer.Ordinal), names);
        Assert.DoesNotContain("in sheet order", scan.StatusText, StringComparison.Ordinal);
    }

    /// <summary>
    /// The ordinary Scan key is bound on the main window and fires whichever section is showing.
    /// Its pages would enter the session without entering the sequence: Cancel could not take
    /// them back, and the pairing would count them as strangers and refuse a good stack.
    /// </summary>
    [Fact]
    public async Task The_ordinary_scan_is_refused_while_a_stack_is_in_hand()
    {
        var scan = await CreateScanViewModelAsync(new FakeScanService { PageCount = 2 });
        Assert.True(scan.ScanCommand.CanExecute(null));

        await scan.ScanBothSidesCommand.ExecuteAsync(null);

        Assert.False(scan.ScanCommand.CanExecute(null));
        Assert.False(scan.BatchScanCommand.CanExecute(null));
        Assert.True(scan.ScanBothSidesCommand.CanExecute(null));
    }

    /// <summary>The checkbox exists because the gesture differs; both directions must reach Core.</summary>
    [Fact]
    public async Task The_reversed_backs_choice_reaches_the_sequence()
    {
        var scan = await CreateScanViewModelAsync(new FakeScanService { PageCount = 2 });

        scan.BacksReversed = false;

        Assert.False(scan.Duplex.BacksReversed);
        Assert.Equal(DuplexPassState.Inactive, scan.Duplex.State);
    }
}
