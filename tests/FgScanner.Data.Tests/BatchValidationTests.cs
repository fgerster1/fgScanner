using System.Text.Json;
using FgScanner.Core.Index;
using FgScanner.Data;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FgScanner.Data.Tests;

public sealed class BatchValidationTests : IDisposable
{
    private readonly TestDb _db = new();
    private readonly GroupService _groups;
    private readonly ProfileService _profiles;
    private readonly IndexingService _indexing;
    private readonly string _groupsRoot;

    public BatchValidationTests()
    {
        _groups = new GroupService(_db.Factory);
        _profiles = new ProfileService(_db.Factory);
        _indexing = new IndexingService(_db.Factory, _profiles, new IndexExporter());
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

    /// <summary>
    /// A missing Box used to produce one identical error per row — two hundred of them for a
    /// two-hundred-page box, none of which told the operator anything the first did not.
    /// </summary>
    [Fact]
    public async Task A_missing_required_batch_value_is_one_error_not_one_per_row()
    {
        var group = await ArrangeAsync(pages: 3);

        var validation = await _indexing.ValidateAsync(group.Id, Ct);

        Assert.Single(validation.GroupErrors);
        Assert.Contains("Box", validation.GroupErrors[0], StringComparison.Ordinal);
        Assert.All(validation.Documents, d => Assert.Empty(d.Errors));
    }

    [Fact]
    public async Task A_present_batch_value_satisfies_every_row()
    {
        var group = await ArrangeAsync(pages: 3);
        await SetBatchValueAsync(group.Id, "12");

        var validation = await _indexing.ValidateAsync(group.Id, Ct);

        Assert.False(validation.HasErrors);
    }

    /// <summary>Row-scoped fields keep reporting per row; only batch scope moves to the group.</summary>
    [Fact]
    public async Task Row_scoped_required_fields_still_report_per_row()
    {
        var profile = await _profiles.CreateAsync("Evidence", Ct);
        var schema = await _profiles.SaveSchemaAsync(profile.Id,
        [
            new FieldDefinition { Name = "Title", Type = FieldType.Text, Required = true },
        ], Ct);
        var group = await _groups.CreateGroupAsync(_groupsRoot, "Rows", (profile.Id, schema.Version), Ct);

        var incoming = Path.Combine(_db.Root, "incoming-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(incoming);
        var files = new List<string>();
        for (var i = 1; i <= 2; i++)
        {
            var f = Path.Combine(incoming, $"p{i}.png");
            await File.WriteAllBytesAsync(f, [(byte)i, 0xFF], Ct);
            files.Add(f);
        }

        await _groups.AdoptPagesAsync(group.Id, files, Ct);

        var validation = await _indexing.ValidateAsync(group.Id, Ct);

        Assert.Empty(validation.GroupErrors);
        Assert.Equal(2, validation.Documents.Count(d => d.Errors.Count > 0));
    }
}
