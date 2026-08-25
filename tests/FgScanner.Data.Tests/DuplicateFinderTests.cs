using FgScanner.Data;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FgScanner.Data.Tests;

/// <summary>
/// Pairing suspected duplicates inside one group. Exact content already auto-skips at adoption, but
/// pages can still become identical afterwards — an edit rewrites the file and its checksum.
/// </summary>
public sealed class DuplicateFinderTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "fgscanner-tests", Guid.NewGuid().ToString("N"));
    private readonly string _dbPath;
    private readonly GroupService _groups;
    private readonly DuplicateFinder _finder;

    public DuplicateFinderTests()
    {
        Directory.CreateDirectory(_root);
        _dbPath = Path.Combine(_root, "test.db");
        using (var db = new FgScannerDbContext(DbBootstrapper.BuildOptions(_dbPath)))
        {
            db.Database.Migrate();
        }

        _groups = new GroupService(new TestFactory(_dbPath));
        _finder = new DuplicateFinder(new TestFactory(_dbPath));
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

    private async Task<Group> CreateGroupAsync() =>
        await _groups.CreateGroupAsync(_root, "G", null, TestContext.Current.CancellationToken);

    private async Task<Guid> AddPageAsync(Group group, byte content)
    {
        var staging = Directory.CreateDirectory(Path.Combine(_root, Guid.NewGuid().ToString("N"))).FullName;
        var file = Path.Combine(staging, "scan.png");
        await File.WriteAllBytesAsync(file, [content, content, content], TestContext.Current.CancellationToken);
        var result = await _groups.AdoptPagesAsync(
            group.Id, [file], _ => false, TestContext.Current.CancellationToken);
        return result.Adopted.Single().Id;
    }

    private async Task SetAsync(Guid pageId, string? ocrText = null, string? checksum = null, string? imageHash = null)
    {
        await using var db = new FgScannerDbContext(DbBootstrapper.BuildOptions(_dbPath));
        var page = await db.Pages.SingleAsync(p => p.Id == pageId, TestContext.Current.CancellationToken);
        if (ocrText is not null)
        {
            page.OcrText = ocrText;
        }

        if (checksum is not null)
        {
            page.Checksum = checksum;
        }

        if (imageHash is not null)
        {
            page.ImageHash = imageHash;
        }

        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private const string LongText =
        "summit racing equipment 1200 southeast avenue tallmadge ohio invoice total distributor " +
        "flame thrower vacuum mechanical advance carbureted ignition coil canister";

    [Fact]
    public async Task Identical_checksums_are_reported_as_exact()
    {
        var group = await CreateGroupAsync();
        var a = await AddPageAsync(group, 1);
        var b = await AddPageAsync(group, 2);
        await SetAsync(b, checksum: "SAMECHECKSUM");
        await SetAsync(a, checksum: "SAMECHECKSUM");

        var found = await _finder.FindAsync(group.Id, cancellationToken: TestContext.Current.CancellationToken);

        var candidate = Assert.Single(found);
        Assert.Equal(DuplicateKind.Exact, candidate.Kind);
        Assert.Equal(1.0, candidate.Score);
    }

    [Fact]
    public async Task Pages_with_matching_ocr_text_are_reported_as_text_matches()
    {
        var group = await CreateGroupAsync();
        var a = await AddPageAsync(group, 1);
        var b = await AddPageAsync(group, 2);
        await SetAsync(a, ocrText: LongText);
        await SetAsync(b, ocrText: LongText);

        var found = await _finder.FindAsync(group.Id, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(DuplicateKind.Text, Assert.Single(found).Kind);
    }

    [Fact]
    public async Task Unrelated_pages_are_not_reported()
    {
        var group = await CreateGroupAsync();
        var a = await AddPageAsync(group, 1);
        var b = await AddPageAsync(group, 2);
        await SetAsync(a, ocrText: LongText);
        await SetAsync(
            b,
            ocrText: "windows printer test page driver version spooler subsystem application "
                + "succeeded configuration diagnostics network adapter settings");

        Assert.Empty(await _finder.FindAsync(group.Id, cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Image_similarity_is_only_consulted_when_text_cannot_judge()
    {
        var group = await CreateGroupAsync();
        var a = await AddPageAsync(group, 1);
        var b = await AddPageAsync(group, 2);
        // Both pages have plenty of text but say different things. Their image hashes are identical,
        // which under a naive "report every signal" design would still flag them.
        await SetAsync(a, ocrText: LongText, imageHash: new string('a', 64));
        await SetAsync(
            b,
            ocrText: "windows printer test page driver version spooler subsystem application "
                + "succeeded configuration diagnostics network adapter settings",
            imageHash: new string('a', 64));

        Assert.Empty(await _finder.FindAsync(group.Id, cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Pages_without_usable_text_fall_back_to_image_similarity()
    {
        var group = await CreateGroupAsync();
        var a = await AddPageAsync(group, 1);
        var b = await AddPageAsync(group, 2);
        await SetAsync(a, imageHash: new string('a', 64));
        await SetAsync(b, imageHash: new string('a', 64));

        var found = await _finder.FindAsync(group.Id, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(DuplicateKind.Image, Assert.Single(found).Kind);
    }

    [Fact]
    public async Task A_missing_image_hash_is_not_treated_as_a_match()
    {
        var group = await CreateGroupAsync();
        await AddPageAsync(group, 1);
        await AddPageAsync(group, 2);

        // No hashes and no hashing delegate: "cannot say" must not become "identical".
        Assert.Empty(await _finder.FindAsync(group.Id, cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Missing_hashes_are_computed_once_and_cached()
    {
        var group = await CreateGroupAsync();
        await AddPageAsync(group, 1);
        await AddPageAsync(group, 2);
        var calls = 0;

        await _finder.FindAsync(
            group.Id, _ => { calls++; return new string('a', 64); },
            cancellationToken: TestContext.Current.CancellationToken);
        var afterFirst = calls;
        await _finder.FindAsync(
            group.Id, _ => { calls++; return new string('a', 64); },
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(2, afterFirst);
        Assert.Equal(afterFirst, calls); // second run reuses the stored hashes
    }

    [Fact]
    public async Task A_group_with_one_page_has_no_pairs()
    {
        var group = await CreateGroupAsync();
        await AddPageAsync(group, 1);

        Assert.Empty(await _finder.FindAsync(group.Id, cancellationToken: TestContext.Current.CancellationToken));
    }
}
