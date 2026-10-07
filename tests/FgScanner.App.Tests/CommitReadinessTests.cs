using System.IO;
using FgScanner.App.Services;
using FgScanner.App.Views;
using FgScanner.Core.Index;
using FgScanner.Data;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FgScanner.App.Tests;

/// <summary>
/// SPEC-2026-009 AC-1c. OCR that finishes after commit re-exports index.json into the committed
/// folder (IndexingService.ReexportIfCommittedAsync), so a folder copied to the transfer drive
/// mid-reading arrives stale and without its .md files. Commit says so and offers to wait.
/// </summary>
public sealed class CommitReadinessTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "fgscanner-tests", Guid.NewGuid().ToString("N"));
    private readonly string _dbPath;
    private readonly GroupService _groupService;
    private readonly ProfileService _profileService;
    private readonly IndexingService _indexingService;
    private readonly TrashService _trashService;

    public CommitReadinessTests()
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

    private async Task<(Guid GroupId, GroupDetailViewModel Vm)> CreateGroupAsync(
        bool ocrEnabled, OcrStatus pageStatus, int pages = 2)
    {
        var ct = TestContext.Current.CancellationToken;
        var profile = await _profileService.CreateAsync("P" + Guid.NewGuid().ToString("N")[..6], ct);
        await _profileService.UpdateOcrEnabledAsync(profile.Id, ocrEnabled, ct);
        await _profileService.SaveSchemaAsync(
            profile.Id, [new FieldDefinition { Name = "Title", Type = FieldType.Text, Order = 0 }], ct);
        var schema = await _profileService.GetLatestSchemaAsync(profile.Id, ct);
        var group = await _groupService.CreateGroupAsync(_root, "G" + Guid.NewGuid().ToString("N")[..6],
            (profile.Id, schema.Version), ct);
        for (var i = 0; i < pages; i++)
        {
            var file = Path.Combine(group.DirectoryPath, $"scan_{i + 1:00000}.png");
            await File.WriteAllBytesAsync(file, [(byte)i, .. Guid.NewGuid().ToByteArray()], ct);
            await _groupService.AdoptPagesAsync(group.Id, [file], ct);
        }

        await using (var db = new FgScannerDbContext(DbBootstrapper.BuildOptions(_dbPath)))
        {
            foreach (var page in db.Pages.Where(p => p.Document!.GroupId == group.Id))
            {
                page.OcrStatus = pageStatus;
            }

            await db.SaveChangesAsync(ct);
        }

        var vm = new GroupDetailViewModel(
            group, _groupService, _profileService, _indexingService, _trashService, new ActiveGroupStore(),
            CreateToolset());
        await vm.LoadAsync();
        return (group.Id, vm);
    }

    private async Task<GroupState> StoredStateAsync(Guid groupId)
    {
        await using var db = new FgScannerDbContext(DbBootstrapper.BuildOptions(_dbPath));
        return (await db.Groups.SingleAsync(g => g.Id == groupId, TestContext.Current.CancellationToken)).State;
    }

    [Fact]
    public async Task Commit_warns_while_pages_are_unread_and_waiting_commits_nothing()
    {
        var (groupId, vm) = await CreateGroupAsync(ocrEnabled: true, OcrStatus.No);
        string? asked = null;
        vm.ConfirmCommitWhileReading = message =>
        {
            asked = message;
            return false;
        };

        await vm.CommitCommand.ExecuteAsync(null);

        Assert.Contains("2 page(s)", asked);
        Assert.NotEqual(GroupState.Committed, await StoredStateAsync(groupId));
    }

    [Fact]
    public async Task Commit_anyway_commits_with_pages_unread()
    {
        var (groupId, vm) = await CreateGroupAsync(ocrEnabled: true, OcrStatus.Pending);
        vm.ConfirmCommitWhileReading = _ => true;

        await vm.CommitCommand.ExecuteAsync(null);

        Assert.Equal(GroupState.Committed, await StoredStateAsync(groupId));
    }

    [Fact]
    public async Task Commit_with_every_page_read_asks_nothing()
    {
        var (groupId, vm) = await CreateGroupAsync(ocrEnabled: true, OcrStatus.Yes);
        var asked = false;
        vm.ConfirmCommitWhileReading = _ => asked = true;

        await vm.CommitCommand.ExecuteAsync(null);

        Assert.False(asked);
        Assert.Equal(GroupState.Committed, await StoredStateAsync(groupId));
    }

    /// <summary>A profile that never reads pages would otherwise warn on every commit, forever.</summary>
    [Fact]
    public async Task Commit_on_a_profile_without_ocr_asks_nothing_about_unread_pages()
    {
        var (groupId, vm) = await CreateGroupAsync(ocrEnabled: false, OcrStatus.No);
        var asked = false;
        vm.ConfirmCommitWhileReading = _ => asked = true;

        await vm.CommitCommand.ExecuteAsync(null);

        Assert.False(asked);
        Assert.Equal(GroupState.Committed, await StoredStateAsync(groupId));
    }

    [Fact]
    public async Task Read_all_unread_pages_queues_every_group_and_says_how_many()
    {
        await CreateGroupAsync(ocrEnabled: true, OcrStatus.No, pages: 3);
        var (_, vm) = await CreateGroupAsync(ocrEnabled: true, OcrStatus.No, pages: 2);

        await vm.ReadAllUnreadPagesCommand.ExecuteAsync(null);

        Assert.Contains("5 page(s)", vm.StatusText);
    }
}
