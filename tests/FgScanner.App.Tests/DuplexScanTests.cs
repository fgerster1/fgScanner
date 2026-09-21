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

    /// <summary>
    /// Calls every page blank — a stack whose backs are all blank, which is what a duplex run of
    /// one-sided documents produces and the case the Drop policy acts on.
    /// </summary>
    private sealed class BlankClassifier : FgScanner.Core.Capture.IPageClassifier
    {
        public FgScanner.Core.Capture.PageKind Classify(
            string imagePath, FgScanner.Core.Capture.CapturePolicy policy) =>
            FgScanner.Core.Capture.PageKind.Blank;
    }

    private PageEditingToolset CreateToolset(FgScanner.Core.Capture.IPageClassifier? classifier = null) => new(
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
        new CaptureTriageService(
            new TestFactory(_dbPath), new AppSettingsService(new TestFactory(_dbPath)), classifier),
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
    private ScanViewModel CreateViewModel(
        FakeScanService scanner, FgScanner.Core.Capture.IPageClassifier? classifier = null) => new(
        scanner, _sessionService, _groupService, _indexingService, _activeGroup,
        new ProfileOcrTrigger(_profileService, new OcrQueueService(new TestFactory(_dbPath))),
        CreateToolset(classifier), _trashService, new FakeDiscarder());

    private async Task<ScanViewModel> CreateScanViewModelAsync(
        FakeScanService scanner, FgScanner.Core.Capture.IPageClassifier? classifier = null)
    {
        var scan = CreateViewModel(scanner, classifier);
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

    /// <summary>
    /// AC-7 end to end, through the half of the save the checksum flag does not reach. Triage runs
    /// before adoption, so a profile whose blank-page policy is Drop — an ordinary batch-scanning
    /// setting the operator already has — deletes every blank back off the disk outright, not to
    /// the Recycle Bin, before adoption is ever told to keep the stack's identical pages. Ten
    /// sheets would save as ten pages, not twenty, with every pairing after the first shifted.
    /// </summary>
    [Fact]
    public async Task A_profile_that_drops_blank_pages_does_not_eat_the_backs_of_a_stack()
    {
        var ct = TestContext.Current.CancellationToken;
        await new AppSettingsService(new TestFactory(_dbPath)).SetAsync(FeatureFlags.BlankPolicy, "true", ct);
        var profile = await _profileService.CreateAsync("Drops blanks", ct);
        await _profileService.UpdateCapturePolicyAsync(
            profile.Id, false, false, BlankPagePolicy.Drop, ct);
        var group = await _groupService.CreateGroupAsync(
            Path.Combine(_root, "groups"), "Blank backs", (profile.Id, 1), ct);
        _activeGroup.Current = group;

        var scan = await CreateScanViewModelAsync(
            new FakeScanService { PageCount = 3, BlankIdenticalPages = true }, new BlankClassifier());

        await scan.ScanBothSidesCommand.ExecuteAsync(null);
        await scan.ScanBothSidesCommand.ExecuteAsync(null);
        await scan.SaveToGroupCommand.ExecuteAsync(null);

        Assert.Equal(6, await PagesInAsync(group.Id));
    }

    /// <summary>
    /// A save that could not take every page leaves the rest staged and asks the operator to try
    /// again. The retry is still that stack's save, so it still needs the checksum skip — the
    /// locked page's checksum was never registered, so a blank back that comes back on the second
    /// attempt matches a blank already in the group and is dropped as a duplicate.
    /// </summary>
    [Fact]
    public async Task A_retry_after_a_partial_save_still_keeps_the_stack_together()
    {
        var scan = await CreateScanViewModelAsync(
            new FakeScanService { PageCount = 2, BlankIdenticalPages = true });
        var group = await GroupAsync("Partial save");
        _activeGroup.Current = group;

        await scan.ScanBothSidesCommand.ExecuteAsync(null);
        await scan.ScanBothSidesCommand.ExecuteAsync(null);
        Assert.Equal(4, scan.Pages.Count);

        // One page held open, the way a virus scanner or the shell's thumbnailer holds a scan
        // written milliseconds ago — the case RetryOnLockAsync exists for.
        var locked = scan.Pages[2].FilePath;
        using (new FileStream(locked, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            await scan.SaveToGroupCommand.ExecuteAsync(null);
        }

        Assert.Equal(3, await PagesInAsync(group.Id));
        Assert.Single(scan.Pages);

        await scan.SaveToGroupCommand.ExecuteAsync(null);

        Assert.Equal(4, await PagesInAsync(group.Id));
        Assert.Empty(scan.Pages);
    }

    /// <summary>
    /// The skip is granted to a stack's pages, not to the view model, so it lasts exactly as long
    /// as those pages are staged. Left standing it turns de-duplication off for the next, unrelated
    /// save — the protection GroupService gives every other path against a folder adopted twice.
    /// </summary>
    [Fact]
    public async Task The_stacks_protection_does_not_outlive_its_pages()
    {
        var scan = await CreateScanViewModelAsync(
            new FakeScanService { PageCount = 2, BlankIdenticalPages = true });
        var group = await GroupAsync("Leak");
        _activeGroup.Current = group;
        scan.ConfirmDelete = _ => true;

        await scan.ScanBothSidesCommand.ExecuteAsync(null);
        await scan.ScanBothSidesCommand.ExecuteAsync(null);

        // Thrown away rather than saved, so nothing is left that the skip was granted for.
        foreach (var page in scan.Pages.ToList())
        {
            scan.SelectedPages.Add(page);
        }

        scan.DeleteSelectedPagesCommand.Execute(null);
        Assert.Empty(scan.Pages);

        await scan.ScanCommand.ExecuteAsync(null);
        await scan.SaveToGroupCommand.ExecuteAsync(null);

        // Two identical pages from an ordinary scan: one is adopted, the other reported.
        Assert.Equal(1, await PagesInAsync(group.Id));
    }

    /// <summary>
    /// CLAUDE.md, hard rules: never clear an ObservableCollection bound to a Selector. Pages is
    /// the ItemsSource of the thumbnail ListBox, whose SelectionChanged writes back into this view
    /// model. A Reset also costs a hundred-sheet stack two hundred Add events, a re-decode of
    /// every thumbnail and a quadratic run through PagePositionConverter, on the UI thread.
    /// </summary>
    [Fact]
    public async Task The_pairing_does_not_reset_the_bound_page_list()
    {
        var scan = await CreateScanViewModelAsync(new FakeScanService { PageCount = 3 });
        await scan.ScanBothSidesCommand.ExecuteAsync(null);

        var actions = new List<System.Collections.Specialized.NotifyCollectionChangedAction>();
        scan.Pages.CollectionChanged += (_, e) => actions.Add(e.Action);
        await scan.ScanBothSidesCommand.ExecuteAsync(null);

        Assert.DoesNotContain(System.Collections.Specialized.NotifyCollectionChangedAction.Reset, actions);
    }

    /// <summary>
    /// The pairing mints new records, because the sequence numbers are the order adoption reads.
    /// A page the operator picked between the passes is one of those records, so left alone the
    /// selection stops matching anything in the list.
    /// </summary>
    [Fact]
    public async Task A_page_picked_between_the_passes_is_still_the_page_selected()
    {
        var scan = await CreateScanViewModelAsync(new FakeScanService { PageCount = 2 });
        await scan.ScanBothSidesCommand.ExecuteAsync(null);

        // The second front moves from sequence 2 to sequence 3 when the backs are paired in.
        var chosen = scan.Pages[1].FilePath;
        scan.SelectedPages.Add(scan.Pages[1]);

        await scan.ScanBothSidesCommand.ExecuteAsync(null);

        var selected = Assert.Single(scan.SelectedPages);
        Assert.Equal(chosen, selected.FilePath);
        Assert.Contains(selected, scan.Pages);
    }

    /// <summary>
    /// The visible consequence: Delete is enabled because something is selected, and removes
    /// nothing because the selection matches no page. The operator is asked to move 0 pages.
    /// </summary>
    [Fact]
    public async Task Delete_after_a_pairing_offers_the_page_that_is_selected()
    {
        var scan = await CreateScanViewModelAsync(new FakeScanService { PageCount = 2 });
        await scan.ScanBothSidesCommand.ExecuteAsync(null);
        scan.SelectedPages.Add(scan.Pages[1]);

        await scan.ScanBothSidesCommand.ExecuteAsync(null);

        string? asked = null;
        scan.ConfirmDelete = message =>
        {
            asked = message;
            return false;
        };
        scan.DeleteSelectedPagesCommand.Execute(null);

        Assert.NotNull(asked);
        Assert.StartsWith("Move 1 scanned page ", asked, StringComparison.Ordinal);
    }

    /// <summary>A stack saved in sheet order keeps that order in the group's own numbering.</summary>
    [Fact]
    public async Task The_group_receives_the_pages_in_sheet_order()
    {
        var scan = await CreateScanViewModelAsync(new FakeScanService { PageCount = 3 });
        var group = await _groupService.CreateGroupAsync(
            Path.Combine(_root, "groups"), "Ordered", null, TestContext.Current.CancellationToken);
        _activeGroup.Current = group;

        await scan.ScanBothSidesCommand.ExecuteAsync(null);
        await scan.ScanBothSidesCommand.ExecuteAsync(null);

        // Worked out from capture order, never from the list the pairing just produced — an
        // expectation read back off scan.Pages agrees with any pairing at all, including none.
        // The session names pages from a monotonic counter, so ordinal order is the order they
        // came off the scanner: three fronts, then three backs, last back first.
        var captured = scan.Pages
            .Select(p => p.FilePath)
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToList();
        var order = new List<string>
        {
            Checksum(captured[0]), Checksum(captured[5]),
            Checksum(captured[1]), Checksum(captured[4]),
            Checksum(captured[2]), Checksum(captured[3]),
        };
        await scan.SaveToGroupCommand.ExecuteAsync(null);

        await using var db = new FgScannerDbContext(DbBootstrapper.BuildOptions(_dbPath));
        var saved = await db.Documents
            .Where(d => d.GroupId == group.Id)
            .OrderBy(d => d.Sequence)
            .Select(d => d.Pages.First().Checksum)
            .ToListAsync(TestContext.Current.CancellationToken);

        Assert.Equal(order, saved);
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
    /// A stack turned over by hand is a feeder run by definition. On the flatbed each pass is one
    /// sheet, so the "stack" is a single sheet scanned twice; on a one-pass duplex scanner each
    /// pass already returns both sides, so two passes return 4N images and every sheet is paired
    /// with the wrong back while the counts match and every message reads as success.
    ///
    /// It refuses rather than correcting the source: changing a control the operator set is a
    /// guess, and this one decides what the scanner does to the evidence.
    /// </summary>
    [Theory]
    [InlineData(ScanSource.Flatbed)]
    [InlineData(ScanSource.Duplex)]
    public async Task A_stack_is_refused_unless_the_source_is_the_feeder(ScanSource source)
    {
        var scan = await CreateScanViewModelAsync(new FakeScanService { PageCount = 3 });
        scan.Source = source;

        await scan.ScanBothSidesCommand.ExecuteAsync(null);

        Assert.False(scan.DuplexActive);
        Assert.Empty(scan.Pages);
        Assert.Contains("Feeder (one side)", scan.StatusText, StringComparison.Ordinal);
    }

    /// <summary>
    /// On a scanner with no feeder the entry the refusal names is itself greyed out, so sending
    /// the operator to it is a dead end. The probe can be wrong about this (§16 R5), which is why
    /// it says what the scanner reported rather than that two passes are impossible.
    /// </summary>
    [Fact]
    public async Task A_scanner_with_no_feeder_is_not_sent_to_choose_one()
    {
        var scan = CreateViewModel(new FakeScanService
        {
            PageCount = 3,
            Capabilities = new ScanCapabilities(Flatbed: true, Feeder: false, Duplex: false),
        });
        await scan.RefreshDevicesCommand.ExecuteAsync(null);
        scan.Source = ScanSource.Flatbed;

        await scan.ScanBothSidesCommand.ExecuteAsync(null);

        Assert.False(scan.DuplexActive);
        Assert.Empty(scan.Pages);
        Assert.DoesNotContain("Choose", scan.StatusText, StringComparison.Ordinal);
        Assert.Contains("does not report", scan.StatusText, StringComparison.Ordinal);
    }

    /// <summary>
    /// The session is not empty just because the stack is new: an earlier scan, or a session
    /// restored from crash recovery, leaves pages staged. They are not part of the stack, so they
    /// keep their place ahead of it — and their presence must not be read as the stack having
    /// lost pages, which refuses a correctly fed run.
    /// </summary>
    [Fact]
    public async Task Pages_captured_before_the_stack_keep_their_place()
    {
        var scan = await CreateScanViewModelAsync(new FakeScanService { PageCount = 2 });
        await scan.ScanCommand.ExecuteAsync(null);
        var earlier = scan.Pages.Select(p => p.FilePath).ToList();
        Assert.Equal(2, earlier.Count);

        await scan.ScanBothSidesCommand.ExecuteAsync(null);
        await scan.ScanBothSidesCommand.ExecuteAsync(null);

        Assert.Equal(6, scan.Pages.Count);
        Assert.Equal(earlier, scan.Pages.Take(2).Select(p => p.FilePath));

        var stack = scan.Pages.Skip(2).Select(p => p.FilePath).ToList();
        var captured = stack.OrderBy(p => p, StringComparer.Ordinal).ToList();
        Assert.Equal([captured[0], captured[3], captured[1], captured[2]], stack);
        Assert.Contains("in sheet order", scan.StatusText, StringComparison.Ordinal);

        // And the order on screen is the order adoption is handed. The pages keep the sequence
        // numbers they were captured under — a paired stack is deliberately no longer in capture
        // order — so the list itself has to be what the save reads.
        var group = await GroupAsync("Order reaches the group");
        _activeGroup.Current = group;
        var onScreen = scan.Pages.Select(p => Checksum(p.FilePath)).ToList();
        await scan.SaveToGroupCommand.ExecuteAsync(null);

        await using var db = new FgScannerDbContext(DbBootstrapper.BuildOptions(_dbPath));
        var adopted = await db.Documents
            .Where(d => d.GroupId == group.Id)
            .OrderBy(d => d.Sequence)
            .Select(d => d.Pages.First().Checksum)
            .ToListAsync(TestContext.Current.CancellationToken);
        Assert.Equal(onScreen, adopted);
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

    /// <summary>
    /// Adoption renames every file to scan_NNNNN in the order it is handed them, so the saved
    /// names are ascending whatever order the pages went in and prove nothing on their own. The
    /// checksum is what ties a row back to the page it came from.
    /// </summary>
    private static string Checksum(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(stream)).ToLowerInvariant();
    }

    private async Task<int> PagesInAsync(Guid groupId)
    {
        await using var db = new FgScannerDbContext(DbBootstrapper.BuildOptions(_dbPath));
        return await db.Pages.CountAsync(
            p => p.Document!.GroupId == groupId, TestContext.Current.CancellationToken);
    }

    private async Task<Group> GroupAsync(string name) => await _groupService.CreateGroupAsync(
        Path.Combine(_root, "groups"), name, null, TestContext.Current.CancellationToken);

    /// <summary>
    /// "Scan into this group" leaves AutoSaveAfterScan on for the whole round trip, and the two
    /// passes go through the ordinary scan path. Left alone, the fronts are adopted as whole
    /// one-sided documents the moment the first pass ends, the session is reset, and the operator
    /// is bounced to Groups — where the group looks complete and the backs were never asked for.
    /// </summary>
    [Fact]
    public async Task An_automatic_save_does_not_adopt_the_fronts_on_their_own()
    {
        var scan = await CreateScanViewModelAsync(new FakeScanService { PageCount = 3 });
        var group = await GroupAsync("Round trip");
        _activeGroup.Current = group;
        scan.AutoSaveAfterScan = true;

        await scan.ScanBothSidesCommand.ExecuteAsync(null);

        Assert.True(scan.DuplexActive);
        Assert.Equal(3, scan.Pages.Count);
        Assert.Equal(0, await PagesInAsync(group.Id));
    }

    /// <summary>…and the round trip still completes once both passes are in.</summary>
    [Fact]
    public async Task An_automatic_save_adopts_the_whole_stack_once_it_is_paired()
    {
        var scan = await CreateScanViewModelAsync(new FakeScanService { PageCount = 3 });
        var group = await GroupAsync("Round trip");
        _activeGroup.Current = group;
        scan.AutoSaveAfterScan = true;

        await scan.ScanBothSidesCommand.ExecuteAsync(null);
        await scan.ScanBothSidesCommand.ExecuteAsync(null);

        Assert.Equal(6, await PagesInAsync(group.Id));
        Assert.Empty(scan.Pages);
    }

    /// <summary>
    /// A refused pairing is not a finished stack. Adopting it automatically would put a stack
    /// nobody has looked at into a group in capture order and bounce the operator away from the
    /// one screen where the refusal is written.
    /// </summary>
    [Fact]
    public async Task An_automatic_save_leaves_a_refused_pairing_on_screen()
    {
        var scanner = new FakeScanService { PageCount = 3 };
        var scan = await CreateScanViewModelAsync(scanner);
        var group = await GroupAsync("Mismatch");
        _activeGroup.Current = group;
        scan.AutoSaveAfterScan = true;

        await scan.ScanBothSidesCommand.ExecuteAsync(null);
        scanner.PageCount = 2;
        await scan.ScanBothSidesCommand.ExecuteAsync(null);

        Assert.Equal(5, scan.Pages.Count);
        Assert.Equal(0, await PagesInAsync(group.Id));
    }

    /// <summary>
    /// The Save button sits directly under the duplex panel and the Save shortcut fires on any
    /// section, so a half-captured stack is one keystroke from being adopted. A front whose back
    /// was never captured is not half a record on disk: it is adopted as a whole document and
    /// read as one.
    /// </summary>
    [Fact]
    public async Task Saving_is_refused_while_a_stack_is_half_captured()
    {
        var scan = await CreateScanViewModelAsync(new FakeScanService { PageCount = 3 });
        var group = await GroupAsync("Half a stack");
        _activeGroup.Current = group;

        await scan.ScanBothSidesCommand.ExecuteAsync(null);
        await scan.SaveToGroupCommand.ExecuteAsync(null);

        Assert.Equal(3, scan.Pages.Count);
        Assert.Equal(0, await PagesInAsync(group.Id));
        Assert.True(scan.DuplexActive);
        Assert.Contains("backs", scan.StatusText, StringComparison.OrdinalIgnoreCase);
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
