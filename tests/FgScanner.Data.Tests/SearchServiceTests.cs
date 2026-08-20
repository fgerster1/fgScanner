using FgScanner.Data;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FgScanner.Data.Tests;

public sealed class SearchServiceTests : IDisposable
{
    private readonly TestDb _db = new();
    private readonly GroupService _groups;
    private readonly SearchService _search;
    private readonly string _groupsRoot;

    public SearchServiceTests()
    {
        _groups = new GroupService(_db.Factory);
        _search = new SearchService(_db.Factory);
        _groupsRoot = Path.Combine(_db.Root, "groups");
        Directory.CreateDirectory(_groupsRoot);
    }

    public void Dispose() => _db.Dispose();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<(Group Group, List<Page> Pages)> SeedAsync()
    {
        var group = await _groups.CreateGroupAsync(_groupsRoot, "Invoices", null, Ct);
        var incoming = Path.Combine(_db.Root, "in");
        Directory.CreateDirectory(incoming);
        var files = new List<string>();
        for (var i = 1; i <= 3; i++)
        {
            var f = Path.Combine(incoming, $"p{i}.png");
            await File.WriteAllBytesAsync(f, [(byte)i], Ct);
            files.Add(f);
        }

        var adopted = await _groups.AdoptPagesAsync(group.Id, files, Ct);
        var pages = adopted.Adopted.ToList();

        await using var db = _db.CreateContext();
        var p1 = await db.Pages.FirstAsync(p => p.Id == pages[0].Id, Ct);
        p1.OcrText = "Quarterly electricity invoice from Acme Utilities, total 142.50 EUR, due September.";
        var p2 = await db.Pages.Include(p => p.Document).FirstAsync(p => p.Id == pages[1].Id, Ct);
        p2.Document!.CustomFieldsJson = """{"Vendor":"Windmill Water Works","Amount":"77.10"}""";
        var p3 = await db.Pages.FirstAsync(p => p.Id == pages[2].Id, Ct);
        p3.AiDescription = "A handwritten delivery note mentioning a shipment of copper pipes.";
        await db.SaveChangesAsync(Ct);
        return (group, pages);
    }

    [Fact]
    public async Task Ocr_text_is_found_with_a_highlighted_snippet()
    {
        var (group, pages) = await SeedAsync();

        var hits = await _search.SearchAsync("electricity", cancellationToken: Ct);

        var hit = Assert.Single(hits);
        Assert.Equal(pages[0].Id, hit.PageId);
        Assert.Equal(group.Id, hit.GroupId);
        Assert.Equal("Invoices", hit.GroupName);
        Assert.Equal("OCR", hit.Source);
        Assert.Contains($"{SearchService.HighlightStart}electricity{SearchService.HighlightEnd}", hit.Snippet);
    }

    [Fact]
    public async Task Field_values_are_found_with_the_field_name_in_the_snippet()
    {
        var (_, pages) = await SeedAsync();

        var hits = await _search.SearchAsync("Windmill", cancellationToken: Ct);

        var hit = Assert.Single(hits);
        Assert.Equal(pages[1].Id, hit.PageId);
        Assert.Equal("Fields", hit.Source);
        Assert.StartsWith("Vendor: ", hit.Snippet, StringComparison.Ordinal);
        Assert.Contains($"{SearchService.HighlightStart}Windmill{SearchService.HighlightEnd}", hit.Snippet);
    }

    [Fact]
    public async Task Ai_descriptions_are_searchable()
    {
        var (_, pages) = await SeedAsync();

        var hits = await _search.SearchAsync("copper pipes", cancellationToken: Ct);

        var hit = Assert.Single(hits);
        Assert.Equal(pages[2].Id, hit.PageId);
        Assert.Equal("AI", hit.Source);
    }

    [Fact]
    public async Task Multi_token_queries_require_every_token()
    {
        await SeedAsync();

        Assert.Single(await _search.SearchAsync("invoice Acme", cancellationToken: Ct));
        Assert.Empty(await _search.SearchAsync("invoice zeppelin", cancellationToken: Ct));
    }

    [Fact]
    public async Task Fts_syntax_in_user_input_never_throws()
    {
        await SeedAsync();

        Assert.Empty(await _search.SearchAsync("AND (NOT \"", cancellationToken: Ct));
        Assert.Empty(await _search.SearchAsync("   ", cancellationToken: Ct));
    }

    [Fact]
    public async Task Updated_ocr_text_is_reindexed_by_the_fts_triggers()
    {
        var (_, pages) = await SeedAsync();

        await using (var db = _db.CreateContext())
        {
            var page = await db.Pages.FirstAsync(p => p.Id == pages[0].Id, Ct);
            page.OcrText = "Completely different content about zeppelins.";
            await db.SaveChangesAsync(Ct);
        }

        Assert.Empty(await _search.SearchAsync("electricity", cancellationToken: Ct));
        Assert.Single(await _search.SearchAsync("zeppelins", cancellationToken: Ct));
    }
}
