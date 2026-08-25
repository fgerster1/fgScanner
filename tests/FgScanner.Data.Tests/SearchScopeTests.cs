using FgScanner.Data;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FgScanner.Data.Tests;

/// <summary>
/// Search was global only: a hit reported which group it came from, but there was no way to ask
/// the question of a single group.
/// </summary>
public sealed class SearchScopeTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "fgscanner-tests", Guid.NewGuid().ToString("N"));
    private readonly string _dbPath;
    private readonly GroupService _groups;
    private readonly SearchService _search;

    public SearchScopeTests()
    {
        Directory.CreateDirectory(_root);
        _dbPath = Path.Combine(_root, "test.db");
        using (var db = new FgScannerDbContext(DbBootstrapper.BuildOptions(_dbPath)))
        {
            db.Database.Migrate();
        }

        _groups = new GroupService(new TestFactory(_dbPath));
        _search = new SearchService(new TestFactory(_dbPath));
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

    /// <summary>A group holding one page whose OCR text and field values are both searchable.</summary>
    private async Task<Group> SeedGroupAsync(string name, byte content, string ocrText, string vendor)
    {
        var group = await _groups.CreateGroupAsync(_root, name, null, TestContext.Current.CancellationToken);
        var staging = Directory.CreateDirectory(Path.Combine(_root, Guid.NewGuid().ToString("N"))).FullName;
        var file = Path.Combine(staging, "scan.png");
        await File.WriteAllBytesAsync(file, [content, content, content], TestContext.Current.CancellationToken);
        await _groups.AdoptPagesAsync(group.Id, [file], _ => false, TestContext.Current.CancellationToken);

        await using var db = new FgScannerDbContext(DbBootstrapper.BuildOptions(_dbPath));
        var page = await db.Pages.Include(p => p.Document)
            .SingleAsync(p => p.Document!.GroupId == group.Id, TestContext.Current.CancellationToken);
        page.OcrText = ocrText;
        page.Document!.CustomFieldsJson = $$"""{"Vendor":"{{vendor}}"}""";
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return group;
    }

    [Fact]
    public async Task Searching_all_groups_finds_matches_in_every_group()
    {
        await SeedGroupAsync("A", 1, "tallmadge invoice", "Summit");
        await SeedGroupAsync("B", 2, "tallmadge receipt", "Summit");

        var hits = await _search.SearchAsync("tallmadge", cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(2, hits.Count);
    }

    [Fact]
    public async Task Scoping_to_one_group_excludes_the_other()
    {
        var a = await SeedGroupAsync("A", 1, "tallmadge invoice", "Summit");
        await SeedGroupAsync("B", 2, "tallmadge receipt", "Summit");

        var hits = await _search.SearchAsync(
            "tallmadge", groupId: a.Id, cancellationToken: TestContext.Current.CancellationToken);

        var hit = Assert.Single(hits);
        Assert.Equal("A", hit.GroupName);
    }

    [Fact]
    public async Task Scoping_applies_to_field_and_ai_matches_too_not_just_ocr()
    {
        var a = await SeedGroupAsync("A", 1, "nothing relevant", "Summit Racing");
        await SeedGroupAsync("B", 2, "nothing relevant", "Summit Racing");

        // "Summit Racing" lives in CustomFieldsJson, which is the LIKE path rather than FTS —
        // scoping has to cover both or half the results ignore the filter.
        var hits = await _search.SearchAsync(
            "Summit Racing", groupId: a.Id, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("A", Assert.Single(hits).GroupName);
    }

    [Fact]
    public async Task Scoping_to_a_group_with_no_matches_returns_nothing()
    {
        await SeedGroupAsync("A", 1, "tallmadge", "Summit");
        var b = await SeedGroupAsync("B", 2, "unrelated", "Other");

        var hits = await _search.SearchAsync(
            "tallmadge", groupId: b.Id, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Empty(hits);
    }
}
