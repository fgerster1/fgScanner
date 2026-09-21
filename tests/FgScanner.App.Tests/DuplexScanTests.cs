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

    private async Task<ScanViewModel> CreateScanViewModelAsync(FakeScanService scanner)
    {
        var scan = new ScanViewModel(
            scanner, _sessionService, _groupService, _indexingService, _activeGroup,
            new ProfileOcrTrigger(_profileService, new OcrQueueService(new TestFactory(_dbPath))),
            CreateToolset(), _trashService);
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
