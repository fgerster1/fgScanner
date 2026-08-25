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
/// Regression cover for BUG-3 (docs/roadmap-v0.2.md). Rows are built from every page, but their
/// field values were loaded from BuildExportDataAsync, which deliberately omits blank-flagged
/// documents because they never reach an index file. A blank row therefore rendered with empty
/// cells even when the document held values, and the first edit serialised that emptiness over
/// the real CustomFieldsJson — silently destroying values applied at adoption.
/// </summary>
public sealed class BlankRowFieldValueTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "fgscanner-tests", Guid.NewGuid().ToString("N"));
    private readonly string _dbPath;
    private readonly ActiveGroupStore _activeGroup = new();
    private readonly GroupService _groupService;
    private readonly ProfileService _profileService;
    private readonly IndexingService _indexingService;
    private readonly TrashService _trashService;

    public BlankRowFieldValueTests()
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
        new CaptureTriageService(new TestFactory(_dbPath), new AppSettingsService(new TestFactory(_dbPath))));

    /// <summary>A group with one blank-flagged page whose document already carries field values.</summary>
    private async Task<(Group Group, Guid DocumentId, GroupDetailViewModel Vm)> CreateGroupWithBlankPageAsync()
    {
        var profile = await _profileService.CreateAsync("Invoices", TestContext.Current.CancellationToken);
        await _profileService.SaveSchemaAsync(
            profile.Id,
            [new FieldDefinition { Name = "Vendor", Type = FieldType.Text, Order = 0 }],
            TestContext.Current.CancellationToken);
        var schema = await _profileService.GetLatestSchemaAsync(profile.Id, TestContext.Current.CancellationToken);
        var group = await _groupService.CreateGroupAsync(
            _root, "Batch1", (profile.Id, schema.Version), TestContext.Current.CancellationToken);

        var file = Path.Combine(group.DirectoryPath, "scan_00001.png");
        await File.WriteAllBytesAsync(file, [1, 2, 3], TestContext.Current.CancellationToken);
        var adopted = await _groupService.AdoptPagesAsync(
            group.Id, [file], _ => true, TestContext.Current.CancellationToken);
        var documentId = adopted.Adopted.Single().DocumentId;

        await _indexingService.SetFieldValuesAsync(
            documentId,
            new Dictionary<string, string?> { ["Vendor"] = "Summit Racing" },
            TestContext.Current.CancellationToken);

        var vm = new GroupDetailViewModel(
            group, _groupService, _profileService, _indexingService, _trashService, _activeGroup,
            CreateToolset());
        await vm.LoadAsync();
        return (group, documentId, vm);
    }

    [Fact]
    public async Task A_blank_flagged_row_shows_its_stored_field_values()
    {
        var (_, _, vm) = await CreateGroupWithBlankPageAsync();

        var row = Assert.Single(vm.Rows);
        Assert.Equal("Summit Racing", row.Values["Vendor"]);
    }

    [Fact]
    public async Task Editing_one_field_on_a_blank_row_does_not_wipe_the_others()
    {
        var profile = await _profileService.CreateAsync("Two", TestContext.Current.CancellationToken);
        await _profileService.SaveSchemaAsync(
            profile.Id,
            [
                new FieldDefinition { Name = "Vendor", Type = FieldType.Text, Order = 0 },
                new FieldDefinition { Name = "InvoiceNo", Type = FieldType.Text, Order = 1 },
            ],
            TestContext.Current.CancellationToken);
        var schema = await _profileService.GetLatestSchemaAsync(profile.Id, TestContext.Current.CancellationToken);
        var group = await _groupService.CreateGroupAsync(
            _root, "Batch2", (profile.Id, schema.Version), TestContext.Current.CancellationToken);
        var file = Path.Combine(group.DirectoryPath, "scan_00001.png");
        await File.WriteAllBytesAsync(file, [4, 5, 6], TestContext.Current.CancellationToken);
        var adopted = await _groupService.AdoptPagesAsync(
            group.Id, [file], _ => true, TestContext.Current.CancellationToken);
        var documentId = adopted.Adopted.Single().DocumentId;
        await _indexingService.SetFieldValuesAsync(
            documentId,
            new Dictionary<string, string?> { ["Vendor"] = "Summit Racing", ["InvoiceNo"] = "7363454" },
            TestContext.Current.CancellationToken);

        var vm = new GroupDetailViewModel(
            group, _groupService, _profileService, _indexingService, _trashService, _activeGroup,
            CreateToolset());
        await vm.LoadAsync();

        // The user edits one cell on the blank row.
        Assert.Single(vm.Rows).Values["Vendor"] = "Summit Racing Equipment";
        await Task.Delay(200, TestContext.Current.CancellationToken); // persistence is fire-and-forget

        await using var db = new FgScannerDbContext(DbBootstrapper.BuildOptions(_dbPath));
        var json = (await db.Documents.SingleAsync(d => d.Id == documentId, TestContext.Current.CancellationToken))
            .CustomFieldsJson;
        var stored = JsonSerializer.Deserialize<Dictionary<string, string?>>(json) ?? [];

        Assert.Equal("Summit Racing Equipment", stored["Vendor"]);
        Assert.Equal("7363454", stored["InvoiceNo"]); // the untouched field must survive
    }
}
