using System.Text.Json;
using FgScanner.Core.Index;
using FgScanner.Data;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FgScanner.Data.Tests;

/// <summary>
/// A batch value moved off the document onto the group (Phase 19), so the LIKE search over
/// CustomFieldsJson alone stopped finding it. These pin that the group's bag is searched too.
/// </summary>
public sealed class BatchSearchTests : IDisposable
{
    private readonly TestDb _db = new();
    private readonly GroupService _groups;
    private readonly ProfileService _profiles;
    private readonly SearchService _search;
    private readonly string _groupsRoot;

    public BatchSearchTests()
    {
        _groups = new GroupService(_db.Factory);
        _profiles = new ProfileService(_db.Factory);
        _search = new SearchService(_db.Factory);
        _groupsRoot = Path.Combine(_db.Root, "groups");
        Directory.CreateDirectory(_groupsRoot);
    }

    public void Dispose() => _db.Dispose();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<Group> ArrangeAsync(int pages = 3)
    {
        var profile = await _profiles.CreateAsync("Evidence", Ct);
        var schema = await _profiles.SaveSchemaAsync(profile.Id,
        [
            new FieldDefinition { Name = "Box", Type = FieldType.Text, Required = true, Scope = FieldScope.Batch },
            new FieldDefinition { Name = "Title", Type = FieldType.Text },
        ], Ct);
        await _profiles.UpdateExportSettingsAsync(profile.Id, csv: true, xlsx: false, xml: false, json: true, ",", Ct);

        var group = await _groups.CreateGroupAsync(_groupsRoot, "Box12", (profile.Id, schema.Version), Ct);
        var incoming = Path.Combine(_db.Root, "incoming-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(incoming);
        var files = new List<string>();
        for (var i = 1; i <= pages; i++)
        {
            var f = Path.Combine(incoming, $"p{i}.png");
            await File.WriteAllBytesAsync(f, [(byte)i, 0xFF], Ct);
            files.Add(f);
        }

        await _groups.AdoptPagesAsync(group.Id, files, Ct);
        return group;
    }

    private async Task SetBatchValueAsync(Guid groupId, string box)
    {
        await using var db = _db.Factory.CreateDbContext();
        var group = await db.Groups.FirstAsync(g => g.Id == groupId, Ct);
        group.BatchFieldsJson = JsonSerializer.Serialize(new Dictionary<string, string?> { ["Box"] = box });
        await db.SaveChangesAsync(Ct);
    }

    [Fact]
    public async Task A_batch_value_is_findable()
    {
        var group = await ArrangeAsync(pages: 1);
        await SetBatchValueAsync(group.Id, "12");

        var hits = await _search.SearchAsync("12", limit: 50, groupId: null, Ct);

        Assert.Contains(hits, h => h.GroupId == group.Id && h.Source == "Fields");
    }

    /// <summary>
    /// Field search is a LIKE over live rows, not an index, so a correction is searchable at
    /// once. This pins that there is no re-index step to forget.
    /// </summary>
    [Fact]
    public async Task A_corrected_batch_value_is_findable_immediately()
    {
        var group = await ArrangeAsync(pages: 1);
        await SetBatchValueAsync(group.Id, "12");
        await SetBatchValueAsync(group.Id, "13");

        Assert.Contains(await _search.SearchAsync("13", 50, null, Ct), h => h.GroupId == group.Id);
        Assert.DoesNotContain(await _search.SearchAsync("12", 50, null, Ct), h => h.GroupId == group.Id);
    }
}
