using System.IO;
using System.Text.Json;
using FgScanner.App.Services;
using FgScanner.App.Views;
using FgScanner.Core.Index;
using FgScanner.Data;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FgScanner.App.Tests;

/// <summary>
/// SPEC-2026-009 AC-1/AC-1e: "Propose documents" shows the runs it would make and writes DocNo
/// only on confirm. DocNo is what the portal import groups sheets by before it mints permanent
/// page ids, so a stray click must not regroup anything.
/// </summary>
public sealed class ProposeDocumentsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "fgscanner-tests", Guid.NewGuid().ToString("N"));
    private readonly string _dbPath;
    private readonly GroupService _groupService;
    private readonly ProfileService _profileService;
    private readonly IndexingService _indexingService;
    private readonly TrashService _trashService;
    private readonly ActiveGroupStore _activeGroup = new();

    public ProposeDocumentsTests()
    {
        Directory.CreateDirectory(_root);
        _dbPath = Path.Combine(_root, "test.db");
        using (var db = new FgScannerDbContext(DbBootstrapper.BuildOptions(_dbPath)))
        {
            db.Database.Migrate();
        }

        var factory = new TestFactory(_dbPath);
        _groupService = new GroupService(factory);
        _profileService = new ProfileService(factory);
        _indexingService = new IndexingService(factory, _profileService, new IndexExporter());
        _trashService = new TrashService(factory, Path.Combine(_root, "trash"));
    }

    public void Dispose()
    {
        // Pooled connections hold the database file open, which left a folder per test behind.
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

    private sealed record Sheet(string? Title, string? DocNo = null, string? NoteState = null);

    private Task<(List<Guid> Documents, GroupDetailViewModel Vm)> CreateGroupAsync(params Sheet[] sheets) =>
        CreateScopedGroupAsync(FieldScope.Row, sheets);

    private async Task<(List<Guid> Documents, GroupDetailViewModel Vm)> CreateScopedGroupAsync(
        FieldScope docNoScope, params Sheet[] sheets)
    {
        var ct = TestContext.Current.CancellationToken;
        var profile = await _profileService.CreateAsync("Evidence-like", ct);
        string[] names = ["DocNo", "DocType", "DocDate", "Title", "NoteState"];
        await _profileService.SaveSchemaAsync(
            profile.Id,
            [.. names.Select((n, i) => new FieldDefinition
            {
                Name = n,
                Type = FieldType.Text,
                Order = i,
                Scope = n == "DocNo" ? docNoScope : FieldScope.Row,
            })],
            ct);
        var schema = await _profileService.GetLatestSchemaAsync(profile.Id, ct);
        var group = await _groupService.CreateGroupAsync(_root, "Box1", (profile.Id, schema.Version), ct);

        var documents = new List<Guid>();
        for (var i = 0; i < sheets.Length; i++)
        {
            var file = Path.Combine(group.DirectoryPath, $"scan_{i + 1:00000}.png");
            await File.WriteAllBytesAsync(file, [(byte)i, 1, 2], ct);
            var adopted = await _groupService.AdoptPagesAsync(group.Id, [file], ct);
            var documentId = adopted.Adopted.Single().DocumentId;
            await _indexingService.SetFieldValuesAsync(
                documentId,
                new Dictionary<string, string?>
                {
                    ["Title"] = sheets[i].Title,
                    ["DocNo"] = sheets[i].DocNo,
                    ["NoteState"] = sheets[i].NoteState,
                    ["DocType"] = "letter",
                    ["DocDate"] = "2021-10-12",
                },
                ct);
            documents.Add(documentId);
        }

        var vm = new GroupDetailViewModel(
            group, _groupService, _profileService, _indexingService, _trashService, _activeGroup,
            CreateToolset());
        await vm.LoadAsync();
        return (documents, vm);
    }

    private async Task<string?> StoredDocNoAsync(Guid documentId)
    {
        await using var db = new FgScannerDbContext(DbBootstrapper.BuildOptions(_dbPath));
        var json = (await db.Documents.SingleAsync(d => d.Id == documentId, TestContext.Current.CancellationToken))
            .CustomFieldsJson;
        var stored = JsonSerializer.Deserialize<Dictionary<string, string?>>(json ?? "{}") ?? [];
        return stored.GetValueOrDefault("DocNo");
    }

    private sealed class NoopAction : IUndoableAction
    {
        public string Description => "Reorder";

        public Task UndoAsync() => Task.CompletedTask;

        public Task RedoAsync() => Task.CompletedTask;
    }

    [Fact]
    public async Task Cancel_leaves_every_row_unchanged()
    {
        var (documents, vm) = await CreateGroupAsync(new("A"), new("A"), new("B"));
        string? shown = null;
        vm.ConfirmProposal = message =>
        {
            shown = message;
            return false;
        };

        await vm.ProposeDocumentsCommand.ExecuteAsync(null);

        Assert.Contains("2 documents from 3 sheets", shown);
        foreach (var document in documents)
        {
            Assert.True(string.IsNullOrEmpty(await StoredDocNoAsync(document)));
        }
    }

    [Fact]
    public async Task Apply_numbers_the_blank_sheets_and_keeps_a_typed_one()
    {
        var (documents, vm) = await CreateGroupAsync(new("A"), new("A"), new("B", "5"), new("C"));
        vm.ConfirmProposal = _ => true;

        await vm.ProposeDocumentsCommand.ExecuteAsync(null);

        Assert.Equal("6", await StoredDocNoAsync(documents[0]));
        Assert.Equal("6", await StoredDocNoAsync(documents[1]));
        Assert.Equal("5", await StoredDocNoAsync(documents[2]));
        Assert.Equal("7", await StoredDocNoAsync(documents[3]));
        Assert.Equal("6", vm.Rows[0].Values["DocNo"]);
    }

    [Fact]
    public async Task A_group_whose_every_sheet_has_a_docno_proposes_nothing()
    {
        var (_, vm) = await CreateGroupAsync(new("A", "1"), new("B", "2"));
        var asked = false;
        vm.ConfirmProposal = _ => asked = true;

        await vm.ProposeDocumentsCommand.ExecuteAsync(null);

        Assert.False(asked);
        Assert.Contains("already", vm.StatusText);
    }

    /// <summary>Finding 12: the preview is the safety before permanent ids, so it counts what the
    /// import will make — a sticky-note capture is a document of its own there — and names every
    /// run by its Title instead of stopping after fifteen.</summary>
    [Fact]
    public async Task The_preview_counts_what_the_import_makes_and_names_every_run()
    {
        var sheets = new List<Sheet> { new("A"), new("x", NoteState: "as-found"), new("A", NoteState: "clean") };
        sheets.AddRange(Enumerable.Range(0, 20).Select(i => new Sheet($"T{i}")));
        var (_, vm) = await CreateGroupAsync([.. sheets]);
        string? shown = null;
        vm.ConfirmProposal = message =>
        {
            shown = message;
            return false;
        };

        await vm.ProposeDocumentsCommand.ExecuteAsync(null);

        Assert.Contains("21 documents and 1 sticky-note capture from 23 sheets", shown);
        Assert.Contains("DocNo 1: A (3 sheets)", shown);
        Assert.Contains("DocNo 21: T19 (1 sheet)", shown);
        Assert.Contains("continue the last document", shown);
    }

    /// <summary>R-D1: a malformed sticky-note sheet stops the proposal and is named, so NoteState
    /// is fixed before any number is written.</summary>
    [Fact]
    public async Task A_malformed_annotation_is_refused_and_named()
    {
        var (documents, vm) = await CreateGroupAsync(
            new("A"), new("Farm sale", NoteState: "as-found"), new("Trust Efforts", NoteState: "as-found"),
            new("B", NoteState: "clean"));
        var asked = false;
        vm.ConfirmProposal = _ => asked = true;
        string? refusal = null;
        vm.ShowProposalRefusal = message => refusal = message;

        await vm.ProposeDocumentsCommand.ExecuteAsync(null);

        Assert.False(asked);
        Assert.Contains(vm.Rows[1].ImageName, refusal);
        Assert.Contains("Farm sale", refusal);
        Assert.DoesNotContain(vm.Rows[2].ImageName, refusal);
        foreach (var document in documents)
        {
            Assert.True(string.IsNullOrEmpty(await StoredDocNoAsync(document)));
        }
    }

    /// <summary>Finding 8: a committed folder may already be on the transfer drive, and regrouping
    /// it there is the post-commit rewrite R11 exists to prevent.</summary>
    [Fact]
    public async Task Propose_is_disabled_on_a_committed_group()
    {
        var (_, vm) = await CreateGroupAsync(new Sheet("A"));

        vm.Group.State = GroupState.Committed;
        vm.ProposeDocumentsCommand.NotifyCanExecuteChanged();

        Assert.False(vm.ProposeDocumentsCommand.CanExecute(null));
    }

    [Fact]
    public async Task A_batch_scoped_docno_is_refused()
    {
        var (documents, vm) = await CreateScopedGroupAsync(FieldScope.Batch, new Sheet("A"), new Sheet("B"));
        var asked = false;
        vm.ConfirmProposal = _ => asked = true;

        await vm.ProposeDocumentsCommand.ExecuteAsync(null);

        Assert.False(asked);
        Assert.Contains("batch", vm.StatusText);
        Assert.True(string.IsNullOrEmpty(await StoredDocNoAsync(documents[0])));
    }

    /// <summary>Finding 9: the values typed for the next scan survive the apply, on screen and in
    /// the store the Scan page adopts with.</summary>
    [Fact]
    public async Task Apply_keeps_the_values_typed_for_the_next_scan()
    {
        var (_, vm) = await CreateGroupAsync(new("A"), new("B"));
        vm.PendingFields.Single(f => f.Field.Name == "Title").Value = "Deed of trust";
        vm.ConfirmProposal = _ => true;

        await vm.ProposeDocumentsCommand.ExecuteAsync(null);

        Assert.Equal("Deed of trust", vm.PendingFields.Single(f => f.Field.Name == "Title").Value);
        Assert.Equal("Deed of trust", _activeGroup.PendingValues?["Title"]);
    }

    /// <summary>Finding 14: Propose restructures documents like Split and Combine, so undo must not
    /// replay an earlier reorder underneath the new numbers.</summary>
    [Fact]
    public async Task Apply_clears_the_undo_stack()
    {
        var (_, vm) = await CreateGroupAsync(new("A"), new("B"));
        vm.UndoRedo.Push(new NoopAction());
        vm.ConfirmProposal = _ => true;

        await vm.ProposeDocumentsCommand.ExecuteAsync(null);

        Assert.False(vm.UndoRedo.CanUndo);
    }

    /// <summary>Finding 15: a failed write reaches the status line in one piece — nothing is
    /// half-numbered and nothing escapes to the dispatcher.</summary>
    [Fact]
    public async Task A_failed_write_numbers_nothing_and_says_so()
    {
        var (documents, vm) = await CreateGroupAsync(new("A"), new("B"));
        vm.ConfirmProposal = _ => true;
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        File.SetAttributes(_dbPath, FileAttributes.ReadOnly);
        try
        {
            await vm.ProposeDocumentsCommand.ExecuteAsync(null);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            File.SetAttributes(_dbPath, FileAttributes.Normal);
        }

        Assert.Contains("not filled in", vm.StatusText);
        foreach (var document in documents)
        {
            Assert.True(string.IsNullOrEmpty(await StoredDocNoAsync(document)));
        }
    }
}
