using FgScanner.Data;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace FgScanner.Data.Tests;

public sealed class TrashServiceTests : IDisposable
{
    private readonly TestDb _db = new();
    private readonly GroupService _groups;
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 8, 20, 12, 0, 0, TimeSpan.Zero));
    private readonly TrashService _trash;
    private readonly string _groupsRoot;

    public TrashServiceTests()
    {
        _groups = new GroupService(_db.Factory);
        _trash = new TrashService(_db.Factory, Path.Combine(_db.Root, "trash"), _clock);
        _groupsRoot = Path.Combine(_db.Root, "groups");
        Directory.CreateDirectory(_groupsRoot);
    }

    public void Dispose() => _db.Dispose();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<(Group Group, Page Page)> CreateGroupWithOnePageAsync(string name = "G")
    {
        var group = await _groups.CreateGroupAsync(_groupsRoot, name, null, Ct);
        var incoming = Path.Combine(_db.Root, "in-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(incoming);
        var file = Path.Combine(incoming, "x.png");
        await File.WriteAllBytesAsync(file, [1, 2, 3], Ct);
        var adopted = await _groups.AdoptPagesAsync(group.Id, [file], Ct);
        return (group, adopted.Adopted[0]);
    }

    [Fact]
    public async Task Delete_moves_image_and_sidecar_to_trash_and_removes_rows()
    {
        var (group, page) = await CreateGroupWithOnePageAsync();
        var sidecar = Path.Combine(group.DirectoryPath, Path.GetFileNameWithoutExtension(page.FileName) + ".md");
        await File.WriteAllTextAsync(sidecar, "# ocr text", Ct);

        var item = await _trash.DeleteDocumentAsync(page.DocumentId, Ct);

        Assert.False(File.Exists(Path.Combine(group.DirectoryPath, page.FileName)));
        Assert.False(File.Exists(sidecar));
        Assert.True(File.Exists(Path.Combine(item.TrashFolderPath, page.FileName)));
        Assert.True(File.Exists(Path.Combine(item.TrashFolderPath, Path.GetFileName(sidecar))));
        Assert.Empty(await _groups.GetPagesAsync(group.Id, Ct));
    }

    [Fact]
    public async Task Restore_round_trips_files_and_rows_perfectly()
    {
        var (group, page) = await CreateGroupWithOnePageAsync();
        var item = await _trash.DeleteDocumentAsync(page.DocumentId, Ct);

        await _trash.RestoreAsync(item.Id, Ct);

        var pages = await _groups.GetPagesAsync(group.Id, Ct);
        var restored = Assert.Single(pages);
        Assert.Equal(page.FileName, restored.FileName);
        Assert.Equal(page.Checksum, restored.Checksum);
        Assert.True(File.Exists(Path.Combine(group.DirectoryPath, page.FileName)));
        Assert.Empty(await _trash.ListAsync(Ct));
        Assert.False(Directory.Exists(item.TrashFolderPath));
    }

    [Fact]
    public async Task Purge_honors_injected_clock_and_configured_retention()
    {
        var (_, page) = await CreateGroupWithOnePageAsync();
        var item = await _trash.DeleteDocumentAsync(page.DocumentId, Ct);

        _clock.Advance(TimeSpan.FromDays(29));
        Assert.Equal(0, await _trash.PurgeExpiredAsync(Ct)); // day 29: still retained

        _clock.Advance(TimeSpan.FromDays(2));
        Assert.Equal(1, await _trash.PurgeExpiredAsync(Ct)); // day 31: purged
        Assert.Empty(await _trash.ListAsync(Ct));
        Assert.False(Directory.Exists(item.TrashFolderPath));
    }

    [Fact]
    public async Task Custom_retention_setting_is_respected()
    {
        await _trash.SetRetentionDaysAsync(7, Ct);
        var (_, page) = await CreateGroupWithOnePageAsync();
        await _trash.DeleteDocumentAsync(page.DocumentId, Ct);

        _clock.Advance(TimeSpan.FromDays(8));
        Assert.Equal(1, await _trash.PurgeExpiredAsync(Ct));
    }

    [Fact]
    public async Task Replaced_sidecar_archives_through_trash()
    {
        var (group, page) = await CreateGroupWithOnePageAsync();
        var sidecar = Path.Combine(group.DirectoryPath, Path.GetFileNameWithoutExtension(page.FileName) + ".md");
        await File.WriteAllTextAsync(sidecar, "old ocr", Ct);

        await _trash.ArchiveReplacedFileAsync(group.Id, sidecar, Ct);

        Assert.False(File.Exists(sidecar));
        var item = Assert.Single(await _trash.ListAsync(Ct));
        Assert.True(File.Exists(Path.Combine(item.TrashFolderPath, Path.GetFileName(sidecar))));
    }

    [Fact]
    public async Task Restore_into_reused_sequence_appends_at_end()
    {
        var (group, page) = await CreateGroupWithOnePageAsync();
        var item = await _trash.DeleteDocumentAsync(page.DocumentId, Ct);

        // The freed sequence 1 gets reused by a new scan before restore.
        var incoming = Path.Combine(_db.Root, "in-reuse");
        Directory.CreateDirectory(incoming);
        var newer = Path.Combine(incoming, "new.png");
        await File.WriteAllBytesAsync(newer, [9], Ct);
        await _groups.AdoptPagesAsync(group.Id, [newer], Ct);

        await _trash.RestoreAsync(item.Id, Ct);

        var pages = await _groups.GetPagesAsync(group.Id, Ct);
        Assert.Equal(2, pages.Count); // both coexist; restored one took the next free sequence
    }
}
