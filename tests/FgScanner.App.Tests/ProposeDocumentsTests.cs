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
/// SPEC-2026-009 AC-1: "Propose documents" shows the runs it would make and writes DocNo only
/// on confirm. DocNo is what the portal import groups sheets by before it mints permanent page
/// ids, so a stray click must not regroup anything.
/// </summary>
public sealed class ProposeDocumentsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "fgscanner-tests", Guid.NewGuid().ToString("N"));
    private readonly string _dbPath;
    private readonly GroupService _groupService;
    private readonly ProfileService _profileService;
    private readonly IndexingService _indexingService;
    private readonly TrashService _trashService;

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

    /// <summary>Three sheets titled A, A, B, with DocNo blank — Jim's groups as they are.</summary>
    private async Task<(List<Guid> Documents, GroupDetailViewModel Vm)> CreateGroupAsync(
        params (string? Title, string? DocNo)[] sheets)
    {
        var ct = TestContext.Current.CancellationToken;
        var profile = await _profileService.CreateAsync("Evidence-like", ct);
        string[] names = ["DocNo", "DocType", "DocDate", "Title", "NoteState"];
        await _profileService.SaveSchemaAsync(
            profile.Id,
            [.. names.Select((n, i) => new FieldDefinition { Name = n, Type = FieldType.Text, Order = i })],
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
                    ["DocType"] = "letter",
                    ["DocDate"] = "2021-10-12",
                },
                ct);
            documents.Add(documentId);
        }

        var vm = new GroupDetailViewModel(
            group, _groupService, _profileService, _indexingService, _trashService, new ActiveGroupStore(),
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

    [Fact]
    public async Task Cancel_leaves_every_row_unchanged()
    {
        var (documents, vm) = await CreateGroupAsync(("A", null), ("A", null), ("B", null));
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
        var (documents, vm) = await CreateGroupAsync(("A", null), ("A", null), ("B", "5"), ("C", null));
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
        var (_, vm) = await CreateGroupAsync(("A", "1"), ("B", "2"));
        var asked = false;
        vm.ConfirmProposal = _ => asked = true;

        await vm.ProposeDocumentsCommand.ExecuteAsync(null);

        Assert.False(asked);
        Assert.Contains("already", vm.StatusText);
    }
}
