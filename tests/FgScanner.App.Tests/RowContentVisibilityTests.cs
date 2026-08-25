using System.IO;
using FgScanner.App.Services;
using FgScanner.App.Views;
using FgScanner.Core.Index;
using FgScanner.Data;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FgScanner.App.Tests;

/// <summary>
/// Slice 1 of docs/roadmap-v0.2.md: OCR text and AI descriptions were persisted, exported and
/// searchable, but no screen ever rendered them — the only way to read either was to open the CSV.
/// The grid also never showed which folder a document lived in.
/// </summary>
public sealed class RowContentVisibilityTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "fgscanner-tests", Guid.NewGuid().ToString("N"));
    private readonly string _dbPath;
    private readonly ActiveGroupStore _activeGroup = new();
    private readonly GroupService _groupService;
    private readonly ProfileService _profileService;
    private readonly IndexingService _indexingService;
    private readonly TrashService _trashService;

    public RowContentVisibilityTests()
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

    private async Task<GroupDetailViewModel> CreateGroupWithOcrAndAiAsync()
    {
        var group = await _groupService.CreateGroupAsync(_root, "Batch1", null, TestContext.Current.CancellationToken);
        // Stage outside the group, as a real scan does — adopting a file that already sits in the
        // group folder collision-suffixes it.
        var staging = Directory.CreateDirectory(Path.Combine(_root, "staging")).FullName;
        var file = Path.Combine(staging, "scan_00001.png");
        await File.WriteAllBytesAsync(file, [1, 2, 3], TestContext.Current.CancellationToken);
        await _groupService.AdoptPagesAsync(group.Id, [file], _ => false, TestContext.Current.CancellationToken);

        await using (var db = new FgScannerDbContext(DbBootstrapper.BuildOptions(_dbPath)))
        {
            var page = await db.Pages.SingleAsync(TestContext.Current.CancellationToken);
            page.OcrText = "1200 Southeast Ave. Tallmadge, Ohio";
            page.AiDescription = "A printed invoice from an auto parts retailer.";
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var vm = new GroupDetailViewModel(
            group, _groupService, _profileService, _indexingService, _trashService, _activeGroup,
            CreateToolset());
        await vm.LoadAsync();
        return vm;
    }

    [Fact]
    public async Task A_row_carries_the_ocr_text_so_it_can_be_displayed()
    {
        var vm = await CreateGroupWithOcrAndAiAsync();

        Assert.Equal("1200 Southeast Ave. Tallmadge, Ohio", Assert.Single(vm.Rows).OcrText);
    }

    [Fact]
    public async Task A_row_carries_the_ai_description_so_it_can_be_displayed()
    {
        var vm = await CreateGroupWithOcrAndAiAsync();

        Assert.Equal(
            "A printed invoice from an auto parts retailer.",
            Assert.Single(vm.Rows).AiDescription);
    }

    [Fact]
    public async Task A_row_exposes_the_folder_it_lives_in()
    {
        var vm = await CreateGroupWithOcrAndAiAsync();

        var row = Assert.Single(vm.Rows);
        Assert.Equal(vm.Group.DirectoryPath, row.Folder);
        Assert.Equal(Path.Combine(vm.Group.DirectoryPath, "scan_00001.png"), row.ImagePath);
    }

    [Fact]
    public async Task A_page_with_no_ocr_or_ai_yet_reports_nothing_rather_than_stale_text()
    {
        var group = await _groupService.CreateGroupAsync(_root, "Empty", null, TestContext.Current.CancellationToken);
        var file = Path.Combine(group.DirectoryPath, "scan_00001.png");
        await File.WriteAllBytesAsync(file, [9, 9, 9], TestContext.Current.CancellationToken);
        await _groupService.AdoptPagesAsync(group.Id, [file], _ => false, TestContext.Current.CancellationToken);

        var vm = new GroupDetailViewModel(
            group, _groupService, _profileService, _indexingService, _trashService, _activeGroup,
            CreateToolset());
        await vm.LoadAsync();

        var row = Assert.Single(vm.Rows);
        Assert.True(string.IsNullOrEmpty(row.OcrText));
        Assert.True(string.IsNullOrEmpty(row.AiDescription));
    }
}
