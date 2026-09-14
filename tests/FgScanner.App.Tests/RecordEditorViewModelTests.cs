using System.IO;
using System.Text.Json;
using FgScanner.App.Services;
using FgScanner.App.Views;
using FgScanner.Core.Index;
using FgScanner.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace FgScanner.App.Tests;

public class FormFieldTests
{
    [Fact]
    public void A_text_field_carries_its_length_and_required_mark()
    {
        var field = new FormField(new FieldDefinition { Name = "Title", Type = FieldType.Text, Required = true, MaxLength = 40 });

        Assert.True(field.IsText);
        Assert.False(field.IsMemo);
        Assert.False(field.IsDate);
        Assert.False(field.IsList);
        Assert.Equal(40, field.MaxLength);
        Assert.Equal("Title *", field.Label);
    }

    [Fact]
    public void A_memo_is_a_text_field_with_the_memo_flag()
    {
        var field = new FormField(new FieldDefinition { Name = "Notes", Type = FieldType.Text, Memo = true, MaxLength = 2000 });

        Assert.True(field.IsText);
        Assert.True(field.IsMemo);
        Assert.Equal(2000, field.MaxLength);
        Assert.Equal("Notes", field.Label);
    }

    /// <summary>
    /// ProfileService clears these on save, but a FieldDefinition can be built by hand; the form
    /// must not put a length guard or a memo box on a date because a stray value said so.
    /// </summary>
    [Fact]
    public void A_date_ignores_a_stray_length_and_memo_flag()
    {
        var field = new FormField(new FieldDefinition { Name = "DocDate", Type = FieldType.Date, MaxLength = 5, Memo = true });

        Assert.True(field.IsDate);
        Assert.False(field.IsText);
        Assert.False(field.IsMemo);
        Assert.Null(field.MaxLength);
    }

    [Fact]
    public void A_list_offers_its_choices()
    {
        var field = new FormField(new FieldDefinition
        {
            Name = "DocType",
            Type = FieldType.List,
            ListChoicesJson = """["Letter","Invoice"]""",
        });

        Assert.True(field.IsList);
        Assert.False(field.IsText);
        Assert.Equal(["Letter", "Invoice"], field.Choices);
    }
}

/// <summary>
/// The record editor over a real GroupDetailViewModel and database: the form must be another view
/// of the grid's rows, never a second copy of them, or an edit in one is lost or doubled in the other.
/// </summary>
public sealed class RecordEditorViewModelTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "fgscanner-tests", Guid.NewGuid().ToString("N"));
    private readonly string _dbPath;
    private readonly DocumentFieldWrites _writes = new();
    private readonly GroupService _groupService;
    private readonly ProfileService _profileService;
    private readonly IndexingService _indexingService;
    private readonly TrashService _trashService;

    public RecordEditorViewModelTests()
    {
        Directory.CreateDirectory(_root);
        _dbPath = Path.Combine(_root, "test.db");
        using (var db = new FgScannerDbContext(DbBootstrapper.BuildOptions(_dbPath)))
        {
            db.Database.Migrate();
        }

        var factory = new TestFactory(_dbPath, _writes);
        _groupService = new GroupService(factory);
        _profileService = new ProfileService(factory);
        _indexingService = new IndexingService(factory, _profileService, new IndexExporter());
        _trashService = new TrashService(factory, Path.Combine(_root, "trash"));
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private sealed class TestFactory(string dbPath, IInterceptor writes) : IDbContextFactory<FgScannerDbContext>
    {
        public FgScannerDbContext CreateDbContext() => new(
            new DbContextOptionsBuilder<FgScannerDbContext>(DbBootstrapper.BuildOptions(dbPath))
                .AddInterceptors(writes)
                .Options);
    }

    /// <summary>
    /// Records every save that changes a document's field values. Counting at the database is what
    /// "exactly one merge" means to the operator: a second write is a second re-export of a
    /// committed group, and a write from a copied value can overwrite the grid's.
    /// </summary>
    private sealed class DocumentFieldWrites : SaveChangesInterceptor
    {
        private readonly List<string> _json = [];

        public IReadOnlyList<string> Json
        {
            get
            {
                lock (_json)
                {
                    return [.. _json];
                }
            }
        }

        public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
        {
            Record(eventData.Context);
            return base.SavingChanges(eventData, result);
        }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            Record(eventData.Context);
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }

        private void Record(DbContext? context)
        {
            if (context is null)
            {
                return;
            }

            context.ChangeTracker.DetectChanges();
            foreach (var entry in context.ChangeTracker.Entries<Document>())
            {
                if (entry.State == EntityState.Modified && entry.Property(d => d.CustomFieldsJson).IsModified)
                {
                    lock (_json)
                    {
                        _json.Add(entry.Entity.CustomFieldsJson);
                    }
                }
            }
        }
    }

    private PageEditingToolset CreateToolset() => new(
        new FgScanner.Scanning.Editing.ImageEditor(),
        new FgScanner.Scanning.Export.PdfExportService(),
        new FgScanner.Scanning.Export.ImageExportService(),
        new FgScanner.Scanning.Import.FileImportService(),
        new ReorderService(new TestFactory(_dbPath, _writes)),
        new OcrQueueService(new TestFactory(_dbPath, _writes)),
        new AiQueueService(new TestFactory(_dbPath, _writes)),
        new RetroProcessService(new TestFactory(_dbPath, _writes), _groupService, _trashService),
        new FgScanner.Ai.CredentialStore(Path.Combine(_root, "cred"), useCredentialManager: false),
        new AppSettingsService(new TestFactory(_dbPath, _writes)),
        new CaptureTriageService(new TestFactory(_dbPath, _writes), new AppSettingsService(new TestFactory(_dbPath, _writes))),
        new DuplicateFinder(new TestFactory(_dbPath, _writes)));

    private async Task<Group> CreateGroupAsync(string name, params FieldDefinition[] fields)
    {
        var ct = TestContext.Current.CancellationToken;
        var profile = await _profileService.CreateAsync(name, ct);
        for (var i = 0; i < fields.Length; i++)
        {
            fields[i].Order = i;
        }

        await _profileService.SaveSchemaAsync(profile.Id, fields, ct);
        var schema = await _profileService.GetLatestSchemaAsync(profile.Id, ct);
        return await _groupService.CreateGroupAsync(_root, name, (profile.Id, schema.Version), ct);
    }

    private async Task<Guid> AdoptPageAsync(Group group, string fileName)
    {
        var ct = TestContext.Current.CancellationToken;
        var file = Path.Combine(group.DirectoryPath, fileName);
        // Adoption dedupes by checksum, so each file needs its own bytes.
        await File.WriteAllBytesAsync(file, System.Text.Encoding.UTF8.GetBytes(fileName), ct);
        var adopted = await _groupService.AdoptPagesAsync(group.Id, [file], _ => true, ct);
        return adopted.Adopted.Single().DocumentId;
    }

    private async Task<GroupDetailViewModel> LoadAsync(Group group)
    {
        var vm = new GroupDetailViewModel(
            group, _groupService, _profileService, _indexingService, _trashService, new ActiveGroupStore(), CreateToolset());
        await vm.LoadAsync();
        return vm;
    }

    /// <summary>Persistence is fire-and-forget, and the reload it triggers can be mid-way through replacing Rows while this reads them.</summary>
    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (true)
        {
            try
            {
                if (condition())
                {
                    return;
                }
            }
            catch (InvalidOperationException)
            {
            }

            Assert.True(DateTime.UtcNow < deadline, "The condition was not met within 10 seconds.");
            await Task.Delay(20, TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public async Task Form_edit_persists_through_the_row_path()
    {
        var group = await CreateGroupAsync(
            "Form1",
            new FieldDefinition { Name = "Vendor", Type = FieldType.Text },
            new FieldDefinition { Name = "Notes", Type = FieldType.Text, Memo = true, MaxLength = 500 });
        await AdoptPageAsync(group, "scan_00001.png");
        var vm = await LoadAsync(group);
        vm.SelectedRow = vm.Rows[0];
        using var editor = new RecordEditorViewModel(vm);
        var cellChanges = new List<string?>();
        vm.Rows[0].Values.PropertyChanged += (_, e) => cellChanges.Add(e.PropertyName);
        var before = _writes.Json.Count;

        editor.RowFormFields.Single(f => f.Name == "Vendor").Value = "Acme";

        await WaitUntilAsync(() => _writes.Json.Count > before);
        await Task.Delay(200, TestContext.Current.CancellationToken); // room for a second, unwanted write
        var write = Assert.Single(_writes.Json.Skip(before));
        Assert.Equal("Acme", JsonSerializer.Deserialize<Dictionary<string, string?>>(write)!["Vendor"]);
        Assert.Equal("Acme", vm.Rows[0].Values["Vendor"]);
        Assert.Contains("Item[Vendor]", cellChanges);
    }

    [Fact]
    public async Task The_form_binds_the_selected_rows_own_values()
    {
        var group = await CreateGroupAsync("Form2", new FieldDefinition { Name = "Vendor", Type = FieldType.Text });
        await AdoptPageAsync(group, "scan_00001.png");
        await AdoptPageAsync(group, "scan_00002.png");
        var vm = await LoadAsync(group);
        vm.SelectedRow = vm.Rows[0];
        using var editor = new RecordEditorViewModel(vm);

        editor.SelectedRow = vm.Rows[1];

        Assert.Same(vm.Rows[1], vm.SelectedRow);
        Assert.Same(vm.Rows[1].Values, editor.RowFormFields.Single().Values);
    }

    /// <summary>
    /// A length shortened after values were typed must not cost anyone their text. Trimming here
    /// would be a silent edit to evidence; showing it invalid leaves the operator to decide.
    /// </summary>
    [Fact]
    public async Task Over_length_stored_value_is_flagged_never_trimmed()
    {
        var ct = TestContext.Current.CancellationToken;
        var group = await CreateGroupAsync("Form3", new FieldDefinition { Name = "Title", Type = FieldType.Text, MaxLength = 10 });
        var documentId = await AdoptPageAsync(group, "scan_00001.png");
        const string longTitle = "A title of 23 letters.."; // 23 characters against a limit of 10
        await _indexingService.SetFieldValuesAsync(documentId, new Dictionary<string, string?> { ["Title"] = longTitle }, ct);
        var vm = await LoadAsync(group);
        var before = _writes.Json.Count;

        using var editor = new RecordEditorViewModel(vm);
        var title = editor.RowFormFields.Single();

        Assert.Equal(longTitle, title.Value);
        Assert.Contains("23", title.Error);
        await Task.Delay(200, ct);
        Assert.Equal(before, _writes.Json.Count);
    }

    [Fact]
    public async Task Batch_field_in_form_writes_to_the_group()
    {
        var group = await CreateGroupAsync(
            "Form4",
            new FieldDefinition { Name = "Box", Type = FieldType.Text, Scope = FieldScope.Batch },
            new FieldDefinition { Name = "Vendor", Type = FieldType.Text });
        await AdoptPageAsync(group, "scan_00001.png");
        await AdoptPageAsync(group, "scan_00002.png");
        var vm = await LoadAsync(group);
        using var editor = new RecordEditorViewModel(vm);

        var box = Assert.Single(editor.BatchFormFields);
        Assert.Equal("Box", box.Name);
        Assert.DoesNotContain(editor.RowFormFields, f => f.Name == "Box");
        box.Value = "B-777";

        await WaitUntilAsync(() => vm.Rows.Count == 2 && vm.Rows.All(r => r.Values["Box"] == "B-777"));
        var stored = await _groupService.FindAsync(group.Id, TestContext.Current.CancellationToken);
        Assert.Equal("B-777", JsonSerializer.Deserialize<Dictionary<string, string?>>(stored!.BatchFieldsJson)!["Box"]);
    }

    /// <summary>
    /// A reload (OCR finishing, a batch value saved) replaces every DocumentRow. An editor still
    /// holding the old one would write to a row nothing displays any more (§16 R6).
    /// </summary>
    [Fact]
    public async Task Reloading_rows_reselects_the_same_page_by_document_id()
    {
        var group = await CreateGroupAsync("Form5", new FieldDefinition { Name = "Vendor", Type = FieldType.Text });
        await AdoptPageAsync(group, "scan_00001.png");
        await AdoptPageAsync(group, "scan_00002.png");
        await AdoptPageAsync(group, "scan_00003.png");
        var vm = await LoadAsync(group);
        vm.SelectedRow = vm.Rows[1];
        using var editor = new RecordEditorViewModel(vm);
        var stale = editor.SelectedRow!;

        await vm.ReloadRowsAsync();

        Assert.NotSame(stale, editor.SelectedRow);
        Assert.Equal(stale.DocumentId, editor.SelectedRow!.DocumentId);
        Assert.Same(vm.Rows[1], editor.SelectedRow);
        Assert.Same(vm.Rows[1].Values, editor.RowFormFields.Single().Values);
    }

    [Fact]
    public async Task Opening_with_no_selection_starts_on_the_first_page()
    {
        var group = await CreateGroupAsync("Form6", new FieldDefinition { Name = "Vendor", Type = FieldType.Text });
        await AdoptPageAsync(group, "scan_00001.png");
        await AdoptPageAsync(group, "scan_00002.png");
        var vm = await LoadAsync(group);

        using var editor = new RecordEditorViewModel(vm);

        Assert.Same(vm.Rows[0], editor.SelectedRow);
    }

    [Fact]
    public async Task An_empty_group_shows_the_no_pages_state()
    {
        var group = await CreateGroupAsync("Form7", new FieldDefinition { Name = "Vendor", Type = FieldType.Text });
        var vm = await LoadAsync(group);

        using var editor = new RecordEditorViewModel(vm);

        Assert.True(editor.IsEmpty);
        Assert.Equal("No pages in this group yet", editor.EmptyText);
        Assert.Null(editor.SelectedRow);
        Assert.False(editor.RowFormFields.Single().IsEditable);
    }

    [Fact]
    public async Task Next_and_previous_move_through_the_pages_and_stop_at_the_ends()
    {
        var group = await CreateGroupAsync("Form8", new FieldDefinition { Name = "Vendor", Type = FieldType.Text });
        await AdoptPageAsync(group, "scan_00001.png");
        await AdoptPageAsync(group, "scan_00002.png");
        var vm = await LoadAsync(group);
        vm.SelectedRow = vm.Rows[0];
        using var editor = new RecordEditorViewModel(vm);

        Assert.False(editor.PreviousPageCommand.CanExecute(null));
        editor.NextPageCommand.Execute(null);

        Assert.Same(vm.Rows[1], vm.SelectedRow);
        Assert.Same(vm.Rows[1].Values, editor.RowFormFields.Single().Values);
        Assert.False(editor.NextPageCommand.CanExecute(null));
        editor.PreviousPageCommand.Execute(null);
        Assert.Same(vm.Rows[0], vm.SelectedRow);
    }

    [Fact]
    public async Task Open_record_editor_hands_the_window_an_editor_over_the_same_rows()
    {
        var group = await CreateGroupAsync("Form9", new FieldDefinition { Name = "Vendor", Type = FieldType.Text });
        await AdoptPageAsync(group, "scan_00001.png");
        await AdoptPageAsync(group, "scan_00002.png");
        var vm = await LoadAsync(group);
        vm.SelectedRow = vm.Rows[1];
        RecordEditorViewModel? shown = null;
        vm.ShowRecordEditor = editor =>
        {
            shown = editor;
            Assert.Same(vm.Rows, editor.Rows);
            Assert.Same(vm.Rows[1], editor.SelectedRow);
        };

        vm.OpenRecordEditorCommand.Execute(null);

        Assert.NotNull(shown);
    }
}
