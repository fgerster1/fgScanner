using FgScanner.Data;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FgScanner.Data.Tests;

/// <summary>
/// Deleting a group did not exist at all: a mis-created group was permanent for the life of the
/// database. The file policy is the user's choice at delete time, because "delete the group" can
/// reasonably mean unregister it, relocate the scans, or discard everything.
/// </summary>
public sealed class DeleteGroupTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "fgscanner-tests", Guid.NewGuid().ToString("N"));
    private readonly string _dbPath;
    private readonly string _trashRoot;
    private readonly GroupService _groups;

    public DeleteGroupTests()
    {
        Directory.CreateDirectory(_root);
        _dbPath = Path.Combine(_root, "test.db");
        _trashRoot = Path.Combine(_root, "trash");
        using (var db = new FgScannerDbContext(DbBootstrapper.BuildOptions(_dbPath)))
        {
            db.Database.Migrate();
        }

        _groups = new GroupService(new TestFactory(_dbPath));
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

    private async Task<Group> CreateGroupWithAPageAsync(string name)
    {
        var group = await _groups.CreateGroupAsync(_root, name, null, TestContext.Current.CancellationToken);
        var staging = Directory.CreateDirectory(Path.Combine(_root, Guid.NewGuid().ToString("N"))).FullName;
        var file = Path.Combine(staging, "scan.png");
        await File.WriteAllBytesAsync(file, [1, 2, 3], TestContext.Current.CancellationToken);
        await _groups.AdoptPagesAsync(group.Id, [file], _ => false, TestContext.Current.CancellationToken);
        return group;
    }

    [Fact]
    public async Task Unregister_removes_the_group_but_keeps_every_file()
    {
        var group = await CreateGroupWithAPageAsync("Keep");

        await _groups.DeleteGroupAsync(
            group.Id, GroupFilePolicy.KeepFiles, trashRoot: _trashRoot,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Empty(await _groups.ListGroupsAsync(cancellationToken: TestContext.Current.CancellationToken));
        Assert.True(Directory.Exists(group.DirectoryPath));
        Assert.Single(Directory.GetFiles(group.DirectoryPath, "*.png"));
    }

    [Fact]
    public async Task Deleting_the_files_moves_the_folder_into_trash_rather_than_destroying_it()
    {
        var group = await CreateGroupWithAPageAsync("Discard");

        await _groups.DeleteGroupAsync(
            group.Id, GroupFilePolicy.DeleteFiles, trashRoot: _trashRoot,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Empty(await _groups.ListGroupsAsync(cancellationToken: TestContext.Current.CancellationToken));
        Assert.False(Directory.Exists(group.DirectoryPath));
        // Recoverable from disk: a wrong click must not be the end of a batch of scans.
        var recovered = Directory.GetFiles(_trashRoot, "*.png", SearchOption.AllDirectories);
        Assert.Single(recovered);
    }

    [Fact]
    public async Task Relocating_moves_the_folder_to_the_chosen_place()
    {
        var group = await CreateGroupWithAPageAsync("Relocate");
        var destination = Path.Combine(_root, "elsewhere");

        await _groups.DeleteGroupAsync(
            group.Id, GroupFilePolicy.MoveFiles, trashRoot: _trashRoot, moveTo: destination,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Empty(await _groups.ListGroupsAsync(cancellationToken: TestContext.Current.CancellationToken));
        Assert.False(Directory.Exists(group.DirectoryPath));
        Assert.Single(Directory.GetFiles(Path.Combine(destination, "Relocate"), "*.png"));
    }

    [Fact]
    public async Task Deleting_a_group_removes_its_documents_and_pages()
    {
        var group = await CreateGroupWithAPageAsync("Cascade");

        await _groups.DeleteGroupAsync(
            group.Id, GroupFilePolicy.KeepFiles, trashRoot: _trashRoot,
            cancellationToken: TestContext.Current.CancellationToken);

        await using var db = new FgScannerDbContext(DbBootstrapper.BuildOptions(_dbPath));
        Assert.Empty(await db.Documents.ToListAsync(TestContext.Current.CancellationToken));
        Assert.Empty(await db.Pages.ToListAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Deleting_a_group_drops_its_pages_from_search()
    {
        var group = await CreateGroupWithAPageAsync("Searchable");
        await using (var seed = new FgScannerDbContext(DbBootstrapper.BuildOptions(_dbPath)))
        {
            var page = await seed.Pages.SingleAsync(TestContext.Current.CancellationToken);
            page.OcrText = "zzyzx";
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await _groups.DeleteGroupAsync(
            group.Id, GroupFilePolicy.KeepFiles, trashRoot: _trashRoot,
            cancellationToken: TestContext.Current.CancellationToken);

        var hits = await new SearchService(new TestFactory(_dbPath))
            .SearchAsync("zzyzx", cancellationToken: TestContext.Current.CancellationToken);
        Assert.Empty(hits);
    }

    [Fact]
    public async Task Relocating_without_a_destination_is_refused()
    {
        var group = await CreateGroupWithAPageAsync("NoTarget");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _groups.DeleteGroupAsync(
                group.Id, GroupFilePolicy.MoveFiles, trashRoot: _trashRoot,
                cancellationToken: TestContext.Current.CancellationToken));
    }
}
