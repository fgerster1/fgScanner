using System.IO;
using FgScanner.App.Services;
using FgScanner.App.Views;
using FgScanner.Data;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FgScanner.App.Tests;

/// <summary>
/// Clearing out the Trash was one page at a time: the grid allowed a single selection, and Restore
/// and Delete both acted on that one row. Emptying a batch of rejected scans meant clicking once
/// per page.
///
/// Multi-select changes what the existing commands mean, so both had to move together — with three
/// rows highlighted, a Restore that quietly took only one of them is the same class of bug as a
/// right-click acting on a different group than the one under the cursor.
/// </summary>
public sealed class TrashMultiSelectTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "fgscanner-tests", Guid.NewGuid().ToString("N"));
    private readonly string _dbPath;
    private readonly GroupService _groups;
    private readonly TrashService _trash;
    private readonly TrashViewModel _viewModel;
    private readonly string _groupsRoot;

    public TrashMultiSelectTests()
    {
        Directory.CreateDirectory(_root);
        _dbPath = Path.Combine(_root, "test.db");
        using (var db = new FgScannerDbContext(DbBootstrapper.BuildOptions(_dbPath)))
        {
            db.Database.Migrate();
        }

        var factory = new TestFactory(_dbPath);
        _groups = new GroupService(factory);
        _trash = new TrashService(factory, Path.Combine(_root, "trash"));
        _viewModel = new TrashViewModel(_trash, new ActiveGroupStore());
        _groupsRoot = Directory.CreateDirectory(Path.Combine(_root, "groups")).FullName;
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

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed class TestFactory(string dbPath) : IDbContextFactory<FgScannerDbContext>
    {
        public FgScannerDbContext CreateDbContext() => new(DbBootstrapper.BuildOptions(dbPath));
    }

    /// <summary>Puts <paramref name="count"/> pages into the Trash and loads them into the grid.</summary>
    private async Task<List<TrashItem>> TrashPagesAsync(int count)
    {
        var group = await _groups.CreateGroupAsync(_groupsRoot, "Rejects", null, Ct);
        var incoming = Directory.CreateDirectory(
            Path.Combine(_root, "in-" + Guid.NewGuid().ToString("N"))).FullName;
        var files = new List<string>();
        for (var i = 1; i <= count; i++)
        {
            var file = Path.Combine(incoming, $"p{i}.png");
            await File.WriteAllBytesAsync(file, [(byte)i, (byte)(i + 9)], Ct);
            files.Add(file);
        }

        var adopted = await _groups.AdoptPagesAsync(group.Id, files, Ct);
        foreach (var page in adopted.Adopted)
        {
            await _trash.DeleteDocumentAsync(page.DocumentId, Ct);
        }

        await _viewModel.RefreshAsync();
        return [.. _viewModel.Items];
    }

    [Fact]
    public async Task Deleting_a_selection_removes_every_one_of_them()
    {
        var items = await TrashPagesAsync(3);

        var deleted = await _viewModel.DeleteAllAsync(items);

        Assert.Equal(3, deleted);
        Assert.Empty(await _trash.ListAsync(Ct));
        Assert.Empty(_viewModel.Items);
    }

    [Fact]
    public async Task Deleting_leaves_the_rows_that_were_not_selected()
    {
        var items = await TrashPagesAsync(3);

        await _viewModel.DeleteAllAsync([items[0], items[2]]);

        var left = Assert.Single(await _trash.ListAsync(Ct));
        Assert.Equal(items[1].Id, left.Id);
    }

    [Fact]
    public async Task Deleting_nothing_does_nothing()
    {
        await TrashPagesAsync(2);

        Assert.Equal(0, await _viewModel.DeleteAllAsync([]));
        Assert.Equal(2, (await _trash.ListAsync(Ct)).Count);
    }

    [Fact]
    public async Task One_row_that_cannot_be_deleted_does_not_cost_the_others()
    {
        // Same rule as adoption: a batch that reports "2 of 3" beats one that abandons the work.
        var items = await TrashPagesAsync(3);
        await _trash.DeletePermanentlyAsync(items[1].Id, Ct); // vanishes underneath the selection

        var deleted = await _viewModel.DeleteAllAsync(items);

        Assert.Equal(2, deleted);
        Assert.Empty(await _trash.ListAsync(Ct));
    }

    [Fact]
    public async Task Restoring_a_selection_brings_every_one_of_them_back()
    {
        // Restore has to follow multi-select too: with three highlighted, restoring one silently
        // would be the bug that multi-select introduces.
        var items = await TrashPagesAsync(3);

        var restored = await _viewModel.RestoreAllAsync(items);

        Assert.Equal(3, restored);
        Assert.Empty(await _trash.ListAsync(Ct));
        Assert.Equal(3, (await _groups.GetPagesAsync(items[0].OriginalGroupId, Ct)).Count);
    }

    [Fact]
    public async Task The_status_line_says_how_many_went()
    {
        var items = await TrashPagesAsync(2);

        await _viewModel.DeleteAllAsync(items);

        Assert.Contains("2", _viewModel.StatusText, StringComparison.Ordinal);
    }
}
