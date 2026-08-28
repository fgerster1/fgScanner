using System.IO;
using System.Text.Json;
using FgScanner.App.Services;
using FgScanner.App.Views;
using FgScanner.Core.Index;
using FgScanner.Data;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FgScanner.App.Tests;

public class BatchFieldUiTests
{
    /// <summary>
    /// Sticky means "chain this row's value to the next row", which is meaningless for a value
    /// the group owns. Allowing both would let the schema express a contradiction.
    /// </summary>
    [Fact]
    public void Marking_a_field_batch_clears_sticky()
    {
        var row = new FieldRow { Name = "Box", Sticky = true };

        row.Scope = FieldScope.Batch;

        Assert.False(row.Sticky);
    }

    [Fact]
    public void Scope_round_trips_through_the_field_editor()
    {
        var definition = new FieldDefinition { Name = "Box", Scope = FieldScope.Batch };

        var restored = FieldRow.From(definition).ToDefinition();

        Assert.Equal(FieldScope.Batch, restored.Scope);
    }

    /// <summary>
    /// A required batch field with no group value is one missing answer, not two hundred. Flagging
    /// it on every row puts the error in a column the grid renders read-only, so the operator is
    /// shown a complaint they cannot act on; the group-level validation summary is where it belongs.
    /// </summary>
    [Fact]
    public void A_missing_batch_value_does_not_flag_every_row()
    {
        var values = new RowValues(
        [
            new FieldDefinition { Name = "Box", Type = FieldType.Text, Required = true, Scope = FieldScope.Batch },
            new FieldDefinition { Name = "Vendor", Type = FieldType.Text, Required = true },
        ]);

        values.Load(new Dictionary<string, string?> { ["Vendor"] = "Acme" });

        Assert.False(values.HasErrors);
    }

    [Fact]
    public void A_missing_row_value_still_flags_the_row()
    {
        var values = new RowValues(
            [new FieldDefinition { Name = "Vendor", Type = FieldType.Text, Required = true }]);

        values.Load(new Dictionary<string, string?>());

        Assert.True(values.HasErrors);
    }
}

/// <summary>
/// GroupDetailViewModel end-to-end cover: the batch panel, the merged value shown in the entry
/// grid, and the write path that must not leave a private per-row copy of a group value.
/// </summary>
public sealed class BatchFieldGroupDetailTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "fgscanner-tests", Guid.NewGuid().ToString("N"));
    private readonly string _dbPath;
    private readonly ActiveGroupStore _activeGroup = new();
    private readonly GroupService _groupService;
    private readonly ProfileService _profileService;
    private readonly IndexingService _indexingService;
    private readonly TrashService _trashService;

    public BatchFieldGroupDetailTests()
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

    private async Task<Guid> AdoptOnePageAsync(Group group, string fileName = "scan_00001.png")
    {
        var file = Path.Combine(group.DirectoryPath, fileName);
        // Content must differ per file — adoption dedupes by checksum within a group, so two
        // identical byte arrays would leave the second file skipped as a duplicate.
        await File.WriteAllBytesAsync(file, System.Text.Encoding.UTF8.GetBytes(fileName), TestContext.Current.CancellationToken);
        var adopted = await _groupService.AdoptPagesAsync(
            group.Id, [file], _ => true, TestContext.Current.CancellationToken);
        return adopted.Adopted.Single().DocumentId;
    }

    [Fact]
    public async Task Batch_fields_are_offered_once_and_prefilled_from_the_profile_default()
    {
        var profile = await _profileService.CreateAsync("Ops", TestContext.Current.CancellationToken);
        await _profileService.SaveSchemaAsync(
            profile.Id,
            [
                new FieldDefinition { Name = "Box", Type = FieldType.Text, Order = 0, Scope = FieldScope.Batch, Required = true },
                new FieldDefinition
                {
                    Name = "Operator", Type = FieldType.Text, Order = 1, Scope = FieldScope.Batch, DefaultValue = "$(user)",
                },
                new FieldDefinition { Name = "Vendor", Type = FieldType.Text, Order = 2 },
            ],
            TestContext.Current.CancellationToken);
        var schema = await _profileService.GetLatestSchemaAsync(profile.Id, TestContext.Current.CancellationToken);
        var group = await _groupService.CreateGroupAsync(
            _root, "Batch1", (profile.Id, schema.Version), TestContext.Current.CancellationToken);

        var vm = new GroupDetailViewModel(
            group, _groupService, _profileService, _indexingService, _trashService, _activeGroup, CreateToolset());
        await vm.LoadAsync();

        // Only the batch-scoped fields, in schema order — Vendor (row-scoped) belongs to the
        // per-page panel, not here.
        Assert.Equal(["Box", "Operator"], vm.BatchFields.Select(f => f.Field.Name));
        Assert.Equal(Environment.UserName, vm.BatchFields.Single(f => f.Field.Name == "Operator").Value);
    }

    [Fact]
    public async Task Loading_a_group_shows_the_batch_value_already_stored_on_the_group()
    {
        var profile = await _profileService.CreateAsync("Boxes1", TestContext.Current.CancellationToken);
        await _profileService.SaveSchemaAsync(
            profile.Id,
            [new FieldDefinition { Name = "Box", Type = FieldType.Text, Order = 0, Scope = FieldScope.Batch }],
            TestContext.Current.CancellationToken);
        var schema = await _profileService.GetLatestSchemaAsync(profile.Id, TestContext.Current.CancellationToken);
        var group = await _groupService.CreateGroupAsync(
            _root, "Batch2", (profile.Id, schema.Version), TestContext.Current.CancellationToken);
        await AdoptOnePageAsync(group);
        await _indexingService.SetBatchFieldValuesAsync(
            group.Id, new Dictionary<string, string?> { ["Box"] = "B-900" }, TestContext.Current.CancellationToken);

        // Reload the group the way the real app would on reopening it — GroupsViewModel always
        // hands GroupDetailViewModel a Group fetched fresh from the DB, not the stale reference
        // this test happened to create it with.
        var reloaded = await _groupService.FindAsync(group.Id, TestContext.Current.CancellationToken);
        var vm = new GroupDetailViewModel(
            reloaded!, _groupService, _profileService, _indexingService, _trashService, _activeGroup, CreateToolset());
        await vm.LoadAsync();

        // The document itself carries no "Box" value — this is the group's bag merged in, not a
        // per-row copy. IndexingService.GetStoredFieldValuesAsync only ever reads the document.
        Assert.Equal("B-900", Assert.Single(vm.Rows).Values["Box"]);
    }

    [Fact]
    public async Task Typing_a_batch_value_shows_it_on_every_row()
    {
        var profile = await _profileService.CreateAsync("Boxes2", TestContext.Current.CancellationToken);
        await _profileService.SaveSchemaAsync(
            profile.Id,
            [new FieldDefinition { Name = "Box", Type = FieldType.Text, Order = 0, Scope = FieldScope.Batch }],
            TestContext.Current.CancellationToken);
        var schema = await _profileService.GetLatestSchemaAsync(profile.Id, TestContext.Current.CancellationToken);
        var group = await _groupService.CreateGroupAsync(
            _root, "Batch3", (profile.Id, schema.Version), TestContext.Current.CancellationToken);
        await AdoptOnePageAsync(group, "scan_00001.png");
        await AdoptOnePageAsync(group, "scan_00002.png");

        var vm = new GroupDetailViewModel(
            group, _groupService, _profileService, _indexingService, _trashService, _activeGroup, CreateToolset());
        await vm.LoadAsync();
        vm.BatchFields.Single(f => f.Field.Name == "Box").Value = "B-777";
        await Task.Delay(200, TestContext.Current.CancellationToken); // persistence is fire-and-forget

        Assert.Equal(2, vm.Rows.Count);
        Assert.All(vm.Rows, r => Assert.Equal("B-777", r.Values["Box"]));
    }

    /// <summary>
    /// The one that catches the design's real hazard: a row must never accumulate its own copy of
    /// a value the group owns. If PersistRowAsync stopped filtering out batch-scoped fields, this
    /// test would fail — the "Box" key would reappear in the document's own JSON.
    /// </summary>
    [Fact]
    public async Task Editing_a_row_field_does_not_write_the_batch_value_back_to_the_document()
    {
        var profile = await _profileService.CreateAsync("Boxes3", TestContext.Current.CancellationToken);
        await _profileService.SaveSchemaAsync(
            profile.Id,
            [
                new FieldDefinition { Name = "Box", Type = FieldType.Text, Order = 0, Scope = FieldScope.Batch },
                new FieldDefinition { Name = "Vendor", Type = FieldType.Text, Order = 1 },
            ],
            TestContext.Current.CancellationToken);
        var schema = await _profileService.GetLatestSchemaAsync(profile.Id, TestContext.Current.CancellationToken);
        var group = await _groupService.CreateGroupAsync(
            _root, "Batch4", (profile.Id, schema.Version), TestContext.Current.CancellationToken);
        await AdoptOnePageAsync(group);

        var vm = new GroupDetailViewModel(
            group, _groupService, _profileService, _indexingService, _trashService, _activeGroup, CreateToolset());
        await vm.LoadAsync();
        vm.BatchFields.Single(f => f.Field.Name == "Box").Value = "B-100";
        await Task.Delay(200, TestContext.Current.CancellationToken);

        var row = Assert.Single(vm.Rows);
        Assert.Equal("B-100", row.Values["Box"]); // sanity: the row really does show the merged value

        row.Values["Vendor"] = "Acme"; // a row-scoped edit, triggers PersistRowAsync
        await Task.Delay(200, TestContext.Current.CancellationToken);

        await using var db = new FgScannerDbContext(DbBootstrapper.BuildOptions(_dbPath));
        var document = await db.Documents.SingleAsync(
            d => d.GroupId == group.Id, TestContext.Current.CancellationToken);
        var stored = JsonSerializer.Deserialize<Dictionary<string, string?>>(document.CustomFieldsJson) ?? [];

        Assert.Equal("Acme", stored["Vendor"]);
        Assert.False(stored.ContainsKey("Box"), "Box is batch-scoped; the row must not persist a private copy.");
    }

    /// <summary>
    /// The grid only ever holds the pinned layout's fields, so writing its snapshot over
    /// CustomFieldsJson erases everything else the document stores. That is reachable now: after a
    /// field flips to batch scope its per-row values fall outside the layout, and on a committed
    /// evidence group the next cell edit would re-export index.json with them gone.
    /// </summary>
    [Fact]
    public async Task Editing_a_row_keeps_values_the_current_layout_does_not_show()
    {
        var profile = await _profileService.CreateAsync("Boxes5", TestContext.Current.CancellationToken);
        await _profileService.SaveSchemaAsync(
            profile.Id,
            [new FieldDefinition { Name = "Vendor", Type = FieldType.Text, Order = 0 }],
            TestContext.Current.CancellationToken);
        var schema = await _profileService.GetLatestSchemaAsync(profile.Id, TestContext.Current.CancellationToken);
        var group = await _groupService.CreateGroupAsync(
            _root, "Batch5", (profile.Id, schema.Version), TestContext.Current.CancellationToken);
        var documentId = await AdoptOnePageAsync(group);
        await _indexingService.SetFieldValuesAsync(
            documentId,
            new Dictionary<string, string?> { ["Vendor"] = "old", ["Operator"] = "jdoe" },
            TestContext.Current.CancellationToken);

        var vm = new GroupDetailViewModel(
            group, _groupService, _profileService, _indexingService, _trashService, _activeGroup, CreateToolset());
        await vm.LoadAsync();
        Assert.Single(vm.Rows).Values["Vendor"] = "Acme";
        await Task.Delay(200, TestContext.Current.CancellationToken);

        await using var db = new FgScannerDbContext(DbBootstrapper.BuildOptions(_dbPath));
        var document = await db.Documents.SingleAsync(d => d.Id == documentId, TestContext.Current.CancellationToken);
        var stored = JsonSerializer.Deserialize<Dictionary<string, string?>>(document.CustomFieldsJson) ?? [];

        Assert.Equal("Acme", stored["Vendor"]);
        Assert.Equal("jdoe", stored["Operator"]);
    }

    /// <summary>Merging must not cost the operator the ability to empty a cell.</summary>
    [Fact]
    public async Task Clearing_a_cell_removes_the_stored_value()
    {
        var profile = await _profileService.CreateAsync("Boxes6", TestContext.Current.CancellationToken);
        await _profileService.SaveSchemaAsync(
            profile.Id,
            [new FieldDefinition { Name = "Vendor", Type = FieldType.Text, Order = 0 }],
            TestContext.Current.CancellationToken);
        var schema = await _profileService.GetLatestSchemaAsync(profile.Id, TestContext.Current.CancellationToken);
        var group = await _groupService.CreateGroupAsync(
            _root, "Batch6", (profile.Id, schema.Version), TestContext.Current.CancellationToken);
        var documentId = await AdoptOnePageAsync(group);
        await _indexingService.SetFieldValuesAsync(
            documentId,
            new Dictionary<string, string?> { ["Vendor"] = "old" },
            TestContext.Current.CancellationToken);

        var vm = new GroupDetailViewModel(
            group, _groupService, _profileService, _indexingService, _trashService, _activeGroup, CreateToolset());
        await vm.LoadAsync();
        Assert.Single(vm.Rows).Values["Vendor"] = null;
        await Task.Delay(200, TestContext.Current.CancellationToken);

        await using var db = new FgScannerDbContext(DbBootstrapper.BuildOptions(_dbPath));
        var document = await db.Documents.SingleAsync(d => d.Id == documentId, TestContext.Current.CancellationToken);
        var stored = JsonSerializer.Deserialize<Dictionary<string, string?>>(document.CustomFieldsJson) ?? [];

        Assert.False(stored.ContainsKey("Vendor"));
    }

    /// <summary>
    /// A WPF DataGridTextColumn clears an edited cell to "", not null — there is no
    /// TargetNullValue on the grid's columns — so that is the path a real operator exercises when
    /// they delete a cell's text. A null clear only happens from code (e.g. a future "clear field"
    /// command), which is what the sibling test above covers. The two are not the same: this
    /// asserts what MergeFieldValuesAsync actually does with "", it does not assume it matches null.
    /// </summary>
    [Fact]
    public async Task Clearing_a_cell_to_empty_string_keeps_the_key_with_an_empty_value()
    {
        var profile = await _profileService.CreateAsync("Boxes7", TestContext.Current.CancellationToken);
        await _profileService.SaveSchemaAsync(
            profile.Id,
            [new FieldDefinition { Name = "Vendor", Type = FieldType.Text, Order = 0 }],
            TestContext.Current.CancellationToken);
        var schema = await _profileService.GetLatestSchemaAsync(profile.Id, TestContext.Current.CancellationToken);
        var group = await _groupService.CreateGroupAsync(
            _root, "Batch7", (profile.Id, schema.Version), TestContext.Current.CancellationToken);
        var documentId = await AdoptOnePageAsync(group);
        await _indexingService.SetFieldValuesAsync(
            documentId,
            new Dictionary<string, string?> { ["Vendor"] = "old" },
            TestContext.Current.CancellationToken);

        var vm = new GroupDetailViewModel(
            group, _groupService, _profileService, _indexingService, _trashService, _activeGroup, CreateToolset());
        await vm.LoadAsync();
        Assert.Single(vm.Rows).Values["Vendor"] = string.Empty;
        await Task.Delay(200, TestContext.Current.CancellationToken);

        await using var db = new FgScannerDbContext(DbBootstrapper.BuildOptions(_dbPath));
        var document = await db.Documents.SingleAsync(d => d.Id == documentId, TestContext.Current.CancellationToken);
        var stored = JsonSerializer.Deserialize<Dictionary<string, string?>>(document.CustomFieldsJson) ?? [];

        // Not a removal: an empty string is a value, not the sentinel MergeFieldValuesAsync treats
        // as "delete this key" (that sentinel is null, exactly as in SetFieldValuesAsync).
        Assert.True(stored.ContainsKey("Vendor"));
        Assert.Equal(string.Empty, stored["Vendor"]);
    }
}
