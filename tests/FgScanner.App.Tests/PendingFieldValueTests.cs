using System.IO;
using FgScanner.App.Services;
using FgScanner.App.Views;
using FgScanner.Core.Index;
using FgScanner.Data;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FgScanner.App.Tests;

/// <summary>
/// Regression cover for BUG-2 (docs/roadmap-v0.2.md): values typed into the pre-scan field editors
/// were silently discarded. PushPendingValues() ran once, immediately after the editors were built
/// and while every Value was still null, and nothing re-published when the user typed — so
/// ActiveGroupStore.PendingValues stayed permanently empty and the values never reached
/// ApplyInitialValuesAsync at scan time.
/// </summary>
public sealed class PendingFieldValueTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "fgscanner-tests", Guid.NewGuid().ToString("N"));
    private readonly string _dbPath;
    private readonly ActiveGroupStore _activeGroup = new();
    private readonly GroupService _groupService;
    private readonly ProfileService _profileService;
    private readonly IndexingService _indexingService;
    private readonly TrashService _trashService;

    public PendingFieldValueTests()
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

    private async Task<GroupDetailViewModel> CreateLoadedViewModelAsync()
    {
        var profile = await _profileService.CreateAsync("Invoices", TestContext.Current.CancellationToken);
        await _profileService.SaveSchemaAsync(
            profile.Id,
            [
                new FieldDefinition { Name = "Vendor", Type = FieldType.Text, Order = 0 },
                new FieldDefinition { Name = "InvoiceNo", Type = FieldType.Text, Order = 1 },
            ],
            TestContext.Current.CancellationToken);
        var schema = await _profileService.GetLatestSchemaAsync(profile.Id, TestContext.Current.CancellationToken);
        var group = await _groupService.CreateGroupAsync(
            _root, "Batch1", (profile.Id, schema.Version), TestContext.Current.CancellationToken);

        var vm = new GroupDetailViewModel(
            group, _groupService, _profileService, _indexingService, _trashService, _activeGroup,
            CreateToolset());
        await vm.LoadAsync();
        return vm;
    }

    /// <summary>
    /// The usual case: a profile's fields change, the open group stays on its own pinned layout,
    /// and the editors are rebuilt anyway. Rebuilding is what would throw away values the operator
    /// typed seconds ago for the next scan — and they would not be told.
    /// </summary>
    [Fact]
    public async Task Typed_values_survive_a_refresh_when_the_group_keeps_its_layout()
    {
        var vm = await CreateLoadedViewModelAsync();
        vm.PendingFields.Single(f => f.Field.Name == "Vendor").Value = "Summit Racing";
        vm.PendingFields.Single(f => f.Field.Name == "InvoiceNo").Value = "INV-88";

        await _profileService.SaveSchemaAsync(
            vm.Group.ProfileId!.Value,
            [
                new FieldDefinition { Name = "Vendor", Type = FieldType.Text, Order = 0 },
                new FieldDefinition { Name = "InvoiceNo", Type = FieldType.Text, Order = 1 },
                new FieldDefinition { Name = "PoNumber", Type = FieldType.Text, Order = 2 },
            ],
            TestContext.Current.CancellationToken);

        await vm.RefreshSchemaAsync();

        Assert.Equal("Summit Racing", vm.PendingFields.Single(f => f.Field.Name == "Vendor").Value);
        Assert.Equal("INV-88", vm.PendingFields.Single(f => f.Field.Name == "InvoiceNo").Value);
        Assert.Equal("Summit Racing", _activeGroup.PendingValues!["Vendor"]);
    }

    /// <summary>
    /// When the group does move to the new layout, a value whose field no longer exists has
    /// nowhere to go. It is dropped — but said out loud, because a value vanishing in silence is
    /// what makes an operator stop trusting the pre-scan boxes.
    /// </summary>
    [Fact]
    public async Task A_value_whose_field_is_gone_is_dropped_and_counted()
    {
        var vm = await CreateLoadedViewModelAsync();
        vm.PendingFields.Single(f => f.Field.Name == "Vendor").Value = "Summit Racing";
        vm.PendingFields.Single(f => f.Field.Name == "InvoiceNo").Value = "INV-88";

        await _profileService.SaveSchemaAsync(
            vm.Group.ProfileId!.Value,
            [new FieldDefinition { Name = "Vendor", Type = FieldType.Text, Order = 0 }],
            TestContext.Current.CancellationToken);
        var latest = await _profileService.GetLatestSchemaAsync(
            vm.Group.ProfileId!.Value, TestContext.Current.CancellationToken);
        await _groupService.UpgradeSchemaVersionAsync(
            vm.Group.Id, latest.Version, TestContext.Current.CancellationToken);
        vm.Group.SchemaVersion = latest.Version;

        await vm.RefreshSchemaAsync();

        Assert.Equal("Summit Racing", vm.PendingFields.Single(f => f.Field.Name == "Vendor").Value);
        Assert.DoesNotContain(vm.PendingFields, f => f.Field.Name == "InvoiceNo");
        Assert.Contains("1", vm.StatusText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Typing_a_pre_scan_field_value_reaches_the_active_group_store()
    {
        var vm = await CreateLoadedViewModelAsync();
        var vendor = vm.PendingFields.Single(f => f.Field.Name == "Vendor");

        vendor.Value = "Summit Racing";

        Assert.NotNull(_activeGroup.PendingValues);
        Assert.Equal("Summit Racing", _activeGroup.PendingValues!["Vendor"]);
    }

    [Fact]
    public async Task Clearing_a_pre_scan_field_value_removes_it_again()
    {
        var vm = await CreateLoadedViewModelAsync();
        var vendor = vm.PendingFields.Single(f => f.Field.Name == "Vendor");

        vendor.Value = "Summit Racing";
        vendor.Value = "";

        Assert.False(_activeGroup.PendingValues!.ContainsKey("Vendor"));
    }

    [Fact]
    public async Task Multiple_pre_scan_fields_are_all_carried()
    {
        var vm = await CreateLoadedViewModelAsync();

        vm.PendingFields.Single(f => f.Field.Name == "Vendor").Value = "Summit Racing";
        vm.PendingFields.Single(f => f.Field.Name == "InvoiceNo").Value = "7363454";

        Assert.Equal("Summit Racing", _activeGroup.PendingValues!["Vendor"]);
        Assert.Equal("7363454", _activeGroup.PendingValues!["InvoiceNo"]);
    }

    /// <summary>
    /// The one that proves the user-facing behaviour: type a value, scan, and the value is on the
    /// scanned document. The view-model tests above only prove the editor reaches the store.
    /// </summary>
    [Fact]
    public async Task A_value_typed_before_scanning_lands_on_the_scanned_document()
    {
        var vm = await CreateLoadedViewModelAsync();
        var group = vm.Group;
        _activeGroup.Current = group;
        vm.PendingFields.Single(f => f.Field.Name == "Vendor").Value = "Summit Racing";

        var sessionService = new ScanSessionService(Path.Combine(_root, "recovery"));
        var scan = new ScanViewModel(
            new FgScanner.Scanning.FakeScanService { PageCount = 1 }, sessionService, _groupService,
            _indexingService, _activeGroup,
            new ProfileOcrTrigger(_profileService, new OcrQueueService(new TestFactory(_dbPath))),
            CreateToolset(), _trashService);
        await scan.RefreshDevicesCommand.ExecuteAsync(null);
        await scan.ScanCommand.ExecuteAsync(null);
        await scan.SaveToGroupCommand.ExecuteAsync(null);

        await using var db = new FgScannerDbContext(DbBootstrapper.BuildOptions(_dbPath));
        var document = await db.Documents.SingleAsync(d => d.GroupId == group.Id, TestContext.Current.CancellationToken);
        Assert.Contains("Summit Racing", document.CustomFieldsJson);
    }

    [Fact]
    public async Task Reloading_a_group_does_not_leak_editors_from_the_previous_load()
    {
        var vm = await CreateLoadedViewModelAsync();
        vm.PendingFields.Single(f => f.Field.Name == "Vendor").Value = "First";
        var stale = vm.PendingFields.Single(f => f.Field.Name == "Vendor");

        await vm.LoadAsync();

        // The editors were rebuilt; the detached one must no longer publish into the store.
        stale.Value = "Ghost";
        Assert.False(_activeGroup.PendingValues!.ContainsKey("Vendor"));
    }
}
