using FgScanner.Data;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FgScanner.Data.Tests;

/// <summary>
/// Regression cover for BUG-4 (docs/roadmap-v0.2.md). Group.DirectoryPath was matched with
/// SQLite's default BINARY collation, so on case-insensitive Windows "C:\Docs\Invoices" and
/// "c:\docs\invoices" both missed the lookup and each inserted a Group row. Two groups then owned
/// one physical folder and overwrote each other's index files.
/// </summary>
public sealed class GroupPathIdentityTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "fgscanner-tests", Guid.NewGuid().ToString("N"));
    private readonly string _dbPath;
    private readonly GroupService _groupService;

    public GroupPathIdentityTests()
    {
        Directory.CreateDirectory(_root);
        _dbPath = Path.Combine(_root, "test.db");
        using (var db = new FgScannerDbContext(DbBootstrapper.BuildOptions(_dbPath)))
        {
            db.Database.Migrate();
        }

        _groupService = new GroupService(new TestFactory(_dbPath));
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

    [Fact]
    public async Task The_same_folder_in_different_case_is_the_same_group()
    {
        var upper = Path.Combine(_root, "Invoices");
        var lower = Path.Combine(_root.ToUpperInvariant(), "INVOICES");

        var first = await _groupService.AdoptDirectoryAsync(upper, null, TestContext.Current.CancellationToken);
        var second = await _groupService.AdoptDirectoryAsync(lower, null, TestContext.Current.CancellationToken);

        Assert.Equal(first.Id, second.Id);
        var all = await _groupService.ListGroupsAsync(TestContext.Current.CancellationToken);
        Assert.Single(all);
    }

    [Fact]
    public async Task Different_folders_remain_different_groups()
    {
        var a = await _groupService.AdoptDirectoryAsync(
            Path.Combine(_root, "Invoices"), null, TestContext.Current.CancellationToken);
        var b = await _groupService.AdoptDirectoryAsync(
            Path.Combine(_root, "Receipts"), null, TestContext.Current.CancellationToken);

        Assert.NotEqual(a.Id, b.Id);
        Assert.Equal(2, (await _groupService.ListGroupsAsync(TestContext.Current.CancellationToken)).Count);
    }

    [Fact]
    public async Task A_trailing_separator_does_not_mint_a_second_group()
    {
        var plain = Path.Combine(_root, "Invoices");
        var first = await _groupService.AdoptDirectoryAsync(plain, null, TestContext.Current.CancellationToken);
        var second = await _groupService.AdoptDirectoryAsync(
            plain + Path.DirectorySeparatorChar, null, TestContext.Current.CancellationToken);

        Assert.Equal(first.Id, second.Id);
    }

    [Fact]
    public async Task Adopting_an_existing_group_is_reported_as_an_adopt_not_a_create()
    {
        var path = Path.Combine(_root, "Invoices");
        await _groupService.AdoptDirectoryAsync(path, null, TestContext.Current.CancellationToken);

        // Second call in a different case must be recognisable as pre-existing, so the UI can say so
        // instead of silently opening a different group than the user thought they were creating.
        Assert.True(await _groupService.GroupExistsForDirectoryAsync(
            path.ToUpperInvariant(), TestContext.Current.CancellationToken));
        Assert.False(await _groupService.GroupExistsForDirectoryAsync(
            Path.Combine(_root, "Nope"), TestContext.Current.CancellationToken));
    }
}
