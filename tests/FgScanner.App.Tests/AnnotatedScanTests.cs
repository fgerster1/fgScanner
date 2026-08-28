using System.IO;
using System.Text.Json;
using FgScanner.App.Services;
using FgScanner.App.Views;
using FgScanner.Data;
using FgScanner.Scanning;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FgScanner.App.Tests;

/// <summary>
/// A sheet with notes attached is captured twice — as-found, then clean — and the two images
/// must not carry the same NoteState. ApplyInitialValuesAsync stamps ONE pending dictionary
/// onto every document adopted in a save, so each capture has to be its own save, and the
/// sequence, not the operator, has to own the value.
/// </summary>
public sealed class AnnotatedScanTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "fgscanner-tests", Guid.NewGuid().ToString("N"));
    private readonly ScanSessionService _sessionService;
    private readonly GroupService _groupService;
    private readonly ProfileService _profileService;
    private readonly IndexingService _indexingService;
    private readonly TrashService _trashService;
    private readonly ActiveGroupStore _activeGroup = new();
    private readonly string _dbPath;

    public AnnotatedScanTests()
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
            // A held file handle must not fail a passing test.
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

    private ScanViewModel CreateScanViewModel() => new(
        new FakeScanService(), _sessionService, _groupService, _indexingService, _activeGroup,
        new ProfileOcrTrigger(_profileService, new OcrQueueService(new TestFactory(_dbPath))),
        CreateToolset(), _trashService);

    private async Task<Group> AnEvidenceGroupAsync()
    {
        var profile = await _profileService.EnsureEvidenceProfileAsync(Ct);
        var schema = await _profileService.GetLatestSchemaAsync(profile.Id, Ct);
        var group = await _groupService.CreateGroupAsync(
            _root, "Box1", (profile.Id, schema.Version), Ct);
        _activeGroup.Current = group;
        return group;
    }

    /// <summary>The NoteState stamped on each document of a group, in capture order.</summary>
    private async Task<List<string?>> NoteStatesAsync(Guid groupId)
    {
        await using var db = new FgScannerDbContext(DbBootstrapper.BuildOptions(_dbPath));
        var documents = await db.Documents
            .Where(d => d.GroupId == groupId)
            .OrderBy(d => d.Sequence)
            .ToListAsync(Ct);
        return [.. documents.Select(d =>
            JsonSerializer.Deserialize<Dictionary<string, string?>>(d.CustomFieldsJson)
                ?.GetValueOrDefault("NoteState"))];
    }

    [Fact]
    public async Task An_ordinary_scan_stamps_no_note_state()
    {
        var scan = CreateScanViewModel();
        var group = await AnEvidenceGroupAsync();

        await scan.ScanCommand.ExecuteAsync(null);
        await scan.SaveToGroupCommand.ExecuteAsync(null);

        Assert.Equal([null], await NoteStatesAsync(group.Id));
    }

    [Fact]
    public async Task The_annotated_scan_stamps_the_sheet_as_found()
    {
        var scan = CreateScanViewModel();
        var group = await AnEvidenceGroupAsync();

        await scan.ScanAnnotatedCommand.ExecuteAsync(null);

        Assert.Equal(["as-found"], await NoteStatesAsync(group.Id));
        Assert.True(scan.Annotated.IsActive, "the sheet still owes its clean capture");
    }

    [Fact]
    public async Task The_next_save_stamps_the_clean_sheet_and_ends_the_sequence()
    {
        var scan = CreateScanViewModel();
        var group = await AnEvidenceGroupAsync();
        await scan.ScanAnnotatedCommand.ExecuteAsync(null);

        await scan.ScanCommand.ExecuteAsync(null);
        await scan.SaveToGroupCommand.ExecuteAsync(null);

        Assert.Equal(["as-found", "clean"], await NoteStatesAsync(group.Id));
        Assert.False(scan.Annotated.IsActive);
    }

    /// <summary>
    /// The failure this whole design exists to prevent: pending values persist across scans,
    /// so a NoteState that outlived its sheet would stamp `as-found` on plain paper.
    /// </summary>
    [Fact]
    public async Task A_plain_sheet_scanned_after_a_finished_pair_is_stamped_with_nothing()
    {
        var scan = CreateScanViewModel();
        var group = await AnEvidenceGroupAsync();
        await scan.ScanAnnotatedCommand.ExecuteAsync(null);
        await scan.ScanCommand.ExecuteAsync(null);
        await scan.SaveToGroupCommand.ExecuteAsync(null);

        await scan.ScanCommand.ExecuteAsync(null);
        await scan.SaveToGroupCommand.ExecuteAsync(null);

        Assert.Equal(["as-found", "clean", null], await NoteStatesAsync(group.Id));
    }

    /// <summary>
    /// An as-found with no clean partner is a whole-group refusal at import, by which time the
    /// box has been re-shelved — so abandoning the sheet takes its captures with it.
    /// </summary>
    [Fact]
    public async Task Abandoning_the_sheet_discards_what_it_captured()
    {
        var scan = CreateScanViewModel();
        var group = await AnEvidenceGroupAsync();
        await scan.ScanAnnotatedCommand.ExecuteAsync(null);

        await scan.CancelAnnotatedCommand.ExecuteAsync(null);

        Assert.Empty(await NoteStatesAsync(group.Id));
        Assert.False(scan.Annotated.IsActive);
    }
}
