using System.IO;
using FgScanner.App.Services;
using FgScanner.App.Views;
using FgScanner.Core.Index;
using FgScanner.Data;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FgScanner.App.Tests;

/// <summary>
/// The group detail pane has to say when a group is pinned to an older field layout than its
/// profile. A group created before its fields were defined resolves zero of them and renders an
/// empty pane, which reads as "this feature does not work" rather than "this group is behind".
/// The app already knew — it said so in a status line that vanished on the next action.
/// </summary>
public sealed class SchemaNoticeTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "fgscanner-tests", Guid.NewGuid().ToString("N"));
    private readonly string _dbPath;
    private readonly ActiveGroupStore _activeGroup = new();
    private readonly GroupService _groupService;
    private readonly ProfileService _profileService;
    private readonly IndexingService _indexingService;
    private readonly TrashService _trashService;

    public SchemaNoticeTests()
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

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

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

    private async Task<GroupDetailViewModel> ViewModelFor(Group group)
    {
        var vm = new GroupDetailViewModel(
            group, _groupService, _profileService, _indexingService, _trashService, _activeGroup,
            CreateToolset());
        await vm.LoadAsync();
        return vm;
    }

    /// <summary>Reproduces the real sequence: create the group, then define the fields.</summary>
    private async Task<(Profile Profile, Group Group)> GroupCreatedBeforeItsFieldsAsync()
    {
        var profile = await _profileService.CreateAsync("JimsStuff", Ct);
        var empty = await _profileService.GetLatestSchemaAsync(profile.Id, Ct);
        var group = await _groupService.CreateGroupAsync(_root, "test", (profile.Id, empty.Version), Ct);
        await _profileService.SaveSchemaAsync(
            profile.Id,
            [
                new() { Name = "Came From", Type = FieldType.Text, Required = true },
                new() { Name = "Recieved", Type = FieldType.Date, Required = true },
            ],
            Ct);
        return (profile, group);
    }

    [Fact]
    public async Task A_group_behind_its_profile_says_so_and_reports_how_many_fields_it_is_missing()
    {
        var (_, group) = await GroupCreatedBeforeItsFieldsAsync();

        var vm = await ViewModelFor(group);

        Assert.Contains("no fields", vm.SchemaNotice, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("2", vm.SchemaNotice, StringComparison.Ordinal);
        Assert.Empty(vm.PendingFields);
    }

    [Fact]
    public async Task Moving_the_group_forward_clears_the_notice_and_reveals_the_fields()
    {
        var (profile, group) = await GroupCreatedBeforeItsFieldsAsync();
        var latest = await _profileService.GetLatestSchemaAsync(profile.Id, Ct);

        await _groupService.UpgradeSchemaVersionAsync(group.Id, latest.Version, Ct);
        group.SchemaVersion = latest.Version;
        var vm = await ViewModelFor(group);

        Assert.Equal("", vm.SchemaNotice);
        Assert.Equal(2, vm.PendingFields.Count);
    }

    [Fact]
    public async Task A_group_on_the_current_layout_shows_no_notice()
    {
        var profile = await _profileService.CreateAsync("Current", Ct);
        var schema = await _profileService.SaveSchemaAsync(
            profile.Id, [new() { Name = "Vendor", Type = FieldType.Text }], Ct);
        var group = await _groupService.CreateGroupAsync(
            _root, "fresh", (profile.Id, schema.Version), Ct);

        var vm = await ViewModelFor(group);

        Assert.Equal("", vm.SchemaNotice);
        Assert.Single(vm.PendingFields);
    }

    [Fact]
    public async Task A_group_with_no_profile_shows_no_notice()
    {
        var group = await _groupService.CreateGroupAsync(_root, "profileless", null, Ct);

        Assert.Equal("", (await ViewModelFor(group)).SchemaNotice);
    }
}
