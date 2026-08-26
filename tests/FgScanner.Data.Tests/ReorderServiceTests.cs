using FgScanner.Data;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FgScanner.Data.Tests;

public sealed class ReorderServiceTests : IDisposable
{
    private readonly TestDb _db = new();
    private readonly GroupService _groups;
    private readonly ReorderService _reorder;
    private readonly string _groupsRoot;

    public ReorderServiceTests()
    {
        _groups = new GroupService(_db.Factory);
        _reorder = new ReorderService(_db.Factory);
        _groupsRoot = Path.Combine(_db.Root, "groups");
        Directory.CreateDirectory(_groupsRoot);
    }

    public void Dispose() => _db.Dispose();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<(Group Group, List<Page> Pages)> CreateGroupWithPagesAsync(int count)
    {
        var group = await _groups.CreateGroupAsync(_groupsRoot, "R", null, Ct);
        var incoming = Path.Combine(_db.Root, "in-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(incoming);
        var files = new List<string>();
        for (var i = 1; i <= count; i++)
        {
            var f = Path.Combine(incoming, $"p{i}.png");
            await File.WriteAllBytesAsync(f, [(byte)i], Ct);
            files.Add(f);
        }

        var adopted = await _groups.AdoptPagesAsync(group.Id, files, Ct);
        return (group, [.. adopted.Adopted]);
    }

    private async Task<List<string>> CurrentOrderAsync(Guid groupId) =>
        [.. (await _groups.GetPagesAsync(groupId, Ct)).Select(p => p.FileName)];

    [Fact]
    public async Task Move_shifts_a_document_to_the_target_position()
    {
        var (group, pages) = await CreateGroupWithPagesAsync(4);

        await _reorder.MoveAsync(group.Id, pages[3].DocumentId, 2, Ct);

        Assert.Equal(
            ["scan_00001.png", "scan_00004.png", "scan_00002.png", "scan_00003.png"],
            await CurrentOrderAsync(group.Id));
    }

    [Fact]
    public async Task Reverse_inverts_the_order()
    {
        var (group, _) = await CreateGroupWithPagesAsync(3);

        await _reorder.ReverseAsync(group.Id, Ct);

        Assert.Equal(
            ["scan_00003.png", "scan_00002.png", "scan_00001.png"],
            await CurrentOrderAsync(group.Id));
    }

    [Fact]
    public async Task Interleave_merges_fronts_and_backs_for_manual_duplex()
    {
        var (group, _) = await CreateGroupWithPagesAsync(6);

        await _reorder.InterleaveAsync(group.Id, Ct);

        Assert.Equal(
            ["scan_00001.png", "scan_00004.png", "scan_00002.png", "scan_00005.png", "scan_00003.png", "scan_00006.png"],
            await CurrentOrderAsync(group.Id));
    }

    [Fact]
    public async Task Deinterleave_undoes_interleave()
    {
        var (group, _) = await CreateGroupWithPagesAsync(6);

        await _reorder.InterleaveAsync(group.Id, Ct);
        await _reorder.DeinterleaveAsync(group.Id, Ct);

        Assert.Equal(
            ["scan_00001.png", "scan_00002.png", "scan_00003.png", "scan_00004.png", "scan_00005.png", "scan_00006.png"],
            await CurrentOrderAsync(group.Id));
    }

    [Fact]
    public async Task SetOrder_restores_a_captured_arrangement()
    {
        var (group, _) = await CreateGroupWithPagesAsync(3);
        var original = await _reorder.GetOrderAsync(group.Id, Ct);

        await _reorder.ReverseAsync(group.Id, Ct);
        await _reorder.SetOrderAsync(group.Id, original, Ct);

        Assert.Equal(original, await _reorder.GetOrderAsync(group.Id, Ct));
    }

    [Fact]
    public async Task RefreshChecksum_tracks_an_edited_file()
    {
        var (group, pages) = await CreateGroupWithPagesAsync(1);
        var before = pages[0].Checksum;
        await File.WriteAllBytesAsync(
            Path.Combine(group.DirectoryPath, pages[0].FileName), [9, 9, 9], Ct);

        await _reorder.RefreshChecksumAsync(pages[0].Id, Ct);

        var refreshed = (await _groups.GetPagesAsync(group.Id, Ct))[0];
        Assert.NotEqual(before, refreshed.Checksum);
        Assert.Equal(await GroupService.ComputeSha256Async(
            Path.Combine(group.DirectoryPath, refreshed.FileName), Ct), refreshed.Checksum);
    }

    [Fact]
    public async Task RefreshChecksum_discards_the_perceptual_hash_of_the_previous_image()
    {
        // The hash describes a picture, not a file. Keeping it after an edit would have duplicate
        // detection compare pages against images that no longer exist — silently, since a stale
        // hash is indistinguishable from a current one.
        var (group, pages) = await CreateGroupWithPagesAsync(1);
        await SetImageHashAsync(pages[0].Id, new string('a', 64));
        await File.WriteAllBytesAsync(
            Path.Combine(group.DirectoryPath, pages[0].FileName), [9, 9, 9], Ct);

        await _reorder.RefreshChecksumAsync(pages[0].Id, Ct);

        Assert.Null((await _groups.GetPagesAsync(group.Id, Ct))[0].ImageHash);
    }

    private async Task SetImageHashAsync(Guid pageId, string hash)
    {
        await using var db = _db.Factory.CreateDbContext();
        var page = await db.Pages.SingleAsync(p => p.Id == pageId, Ct);
        page.ImageHash = hash;
        await db.SaveChangesAsync(Ct);
    }
}
