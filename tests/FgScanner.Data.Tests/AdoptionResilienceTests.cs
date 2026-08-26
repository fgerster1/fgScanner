using FgScanner.Data;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FgScanner.Data.Tests;

/// <summary>
/// Adoption moves files inside its loop but saved the database rows only at the end, so a failure
/// partway left the already-moved files on disk with no rows describing them — invisible to the
/// app, and unrecoverable except by hand. The recovery session still listed them, so every retry
/// died on the first file that was no longer there, and the user could neither save nor discard.
///
/// Observed for real: three pages moved into a group folder, the fourth hit a transient lock, and
/// the batch aborted. The three were orphaned and the session was wedged.
/// </summary>
public sealed class AdoptionResilienceTests : IDisposable
{
    private readonly TestDb _db = new();
    private readonly GroupService _groups;
    private readonly string _root;

    public AdoptionResilienceTests()
    {
        _groups = new GroupService(_db.Factory);
        _root = Path.Combine(_db.Root, "groups");
        Directory.CreateDirectory(_root);
    }

    public void Dispose() => _db.Dispose();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<(Group Group, List<string> Files)> StageAsync(int count)
    {
        var group = await _groups.CreateGroupAsync(_root, "Batch", null, Ct);
        var staging = Directory.CreateDirectory(
            Path.Combine(_db.Root, "in-" + Guid.NewGuid().ToString("N"))).FullName;
        var files = new List<string>();
        for (var i = 1; i <= count; i++)
        {
            var file = Path.Combine(staging, $"page-{i:00000}.jpg");
            // Distinct content so nothing is skipped as a duplicate.
            await File.WriteAllBytesAsync(file, [(byte)i, (byte)(i + 1), (byte)(i + 2)], Ct);
            files.Add(file);
        }

        return (group, files);
    }

    private async Task<int> RowsInAsync(Guid groupId) =>
        (await _groups.GetPagesAsync(groupId, Ct)).Count;

    [Fact]
    public async Task A_source_file_that_vanished_is_skipped_rather_than_aborting_the_batch()
    {
        // After a partial failure the session still lists pages whose files already moved. Throwing
        // on the first of them makes the whole batch unrecoverable; the rest must still land.
        var (group, files) = await StageAsync(4);
        File.Delete(files[0]);

        var result = await _groups.AdoptPagesAsync(group.Id, files, Ct);

        Assert.Equal(3, result.Adopted.Count);
        Assert.Equal(files[0], Assert.Single(result.MissingSourceFiles));
        Assert.Equal(3, await RowsInAsync(group.Id));
    }

    [Fact]
    public async Task Every_file_vanishing_is_reported_rather_than_throwing()
    {
        var (group, files) = await StageAsync(2);
        files.ForEach(File.Delete);

        var result = await _groups.AdoptPagesAsync(group.Id, files, Ct);

        Assert.Empty(result.Adopted);
        Assert.Equal(2, result.MissingSourceFiles.Count);
    }

    [Fact]
    public async Task A_file_locked_only_briefly_is_still_adopted()
    {
        // A freshly written scan can be held for a moment by a virus scanner or the shell's
        // thumbnailer. Adoption used to abort the batch on the first such moment.
        var (group, files) = await StageAsync(2);
        var holder = new FileStream(files[1], FileMode.Open, FileAccess.Read, FileShare.None);
        _ = Task.Run(
            async () =>
            {
                await Task.Delay(150, Ct);
                await holder.DisposeAsync();
            },
            Ct);

        var result = await _groups.AdoptPagesAsync(group.Id, files, Ct);

        Assert.Equal(2, result.Adopted.Count);
        Assert.Equal(2, await RowsInAsync(group.Id));
    }

    [Fact]
    public async Task Pages_moved_before_a_failure_keep_their_database_rows()
    {
        // The invariant that matters: a file in the group folder always has a row describing it.
        // Losing the batch is recoverable; a moved file with no row is invisible to the whole app.
        var (group, files) = await StageAsync(3);
        using var holder = new FileStream(files[2], FileMode.Open, FileAccess.Read, FileShare.None);

        var result = await _groups.AdoptPagesAsync(group.Id, files, Ct);

        Assert.Equal(2, result.Adopted.Count);
        Assert.Equal(2, await RowsInAsync(group.Id));
        Assert.Equal(files[2], Assert.Single(result.FailedSourceFiles).Path);

        // The one that could not move is still where it was, so a retry can pick it up.
        Assert.True(File.Exists(files[2]));
    }

    [Fact]
    public async Task Retrying_after_a_partial_failure_completes_the_batch()
    {
        // The exact situation the user hit: retry the same list, with some already moved.
        var (group, files) = await StageAsync(3);
        using (var holder = new FileStream(files[2], FileMode.Open, FileAccess.Read, FileShare.None))
        {
            await _groups.AdoptPagesAsync(group.Id, files, Ct);
        }

        var second = await _groups.AdoptPagesAsync(group.Id, files, Ct);

        Assert.Single(second.Adopted);
        Assert.Equal(2, second.MissingSourceFiles.Count);
        Assert.Equal(3, await RowsInAsync(group.Id));
    }

    [Fact]
    public async Task A_group_folder_never_holds_a_file_without_a_row()
    {
        var (group, files) = await StageAsync(4);
        using var holder = new FileStream(files[1], FileMode.Open, FileAccess.Read, FileShare.None);

        await _groups.AdoptPagesAsync(group.Id, files, Ct);

        var onDisk = Directory.GetFiles(group.DirectoryPath, "*.jpg").Length;
        Assert.Equal(await RowsInAsync(group.Id), onDisk);
    }
}
