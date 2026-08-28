using System.Text.Json;
using FgScanner.Core.Index;
using FgScanner.Data;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FgScanner.Data.Tests;

public sealed class BatchFieldExportTests : IDisposable
{
    private readonly TestDb _db = new();
    private readonly GroupService _groups;
    private readonly ProfileService _profiles;
    private readonly IndexingService _indexing;
    private readonly string _groupsRoot;

    public BatchFieldExportTests()
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

    [Fact]
    public async Task A_batch_value_appears_on_every_row()
    {
        var group = await ArrangeAsync();
        await SetBatchValueAsync(group.Id, "12");

        var data = await _indexing.BuildExportDataAsync(group.Id, Ct);

        Assert.NotEmpty(data.Rows);
        Assert.All(data.Rows, r => Assert.Equal("12", r.CustomValues["Box"]));
    }

    /// <summary>
    /// The correction the whole design exists for: one edit, every row, and no per-row write —
    /// so no row can be left behind holding the old number.
    /// </summary>
    [Fact]
    public async Task Correcting_the_group_value_changes_every_row()
    {
        var group = await ArrangeAsync();
        await SetBatchValueAsync(group.Id, "12");
        await SetBatchValueAsync(group.Id, "13");

        var data = await _indexing.BuildExportDataAsync(group.Id, Ct);

        Assert.All(data.Rows, r => Assert.Equal("13", r.CustomValues["Box"]));

        await using var db = _db.Factory.CreateDbContext();
        var stored = await db.Documents.Where(d => d.GroupId == group.Id)
            .Select(d => d.CustomFieldsJson).ToListAsync(Ct);
        Assert.All(stored, json => Assert.DoesNotContain("Box", json, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Manifest_records_each_fields_scope()
    {
        var group = await ArrangeAsync();
        await SetBatchValueAsync(group.Id, "12");

        var data = await _indexing.BuildExportDataAsync(group.Id, Ct);
        var json = IndexPayload.ToJson(data);

        Assert.Contains("\"scope\": \"batch\"", json, StringComparison.Ordinal);
        Assert.Contains("\"scope\": \"row\"", json, StringComparison.Ordinal);
    }

    /// <summary>
    /// Operator's $(user) default has to land somewhere, and a batch field is answered once per
    /// group — so the default seeds the group's bag at creation rather than each row.
    /// </summary>
    [Fact]
    public async Task A_batch_defaults_value_seeds_the_group_at_creation()
    {
        var profile = await _profiles.CreateAsync("Evidence", Ct);
        var schema = await _profiles.SaveSchemaAsync(profile.Id,
        [
            new FieldDefinition
            {
                Name = "Operator", Type = FieldType.Text,
                Scope = FieldScope.Batch, DefaultValue = "$(user)",
            },
        ], Ct);

        var group = await _groups.CreateGroupAsync(_groupsRoot, "Seeded", (profile.Id, schema.Version), Ct);

        await using var db = _db.Factory.CreateDbContext();
        var stored = await db.Groups.Where(g => g.Id == group.Id)
            .Select(g => g.BatchFieldsJson).FirstAsync(Ct);
        var values = JsonSerializer.Deserialize<Dictionary<string, string?>>(stored)!;
        Assert.Equal(Environment.UserName, values["Operator"]);
    }

    /// <summary>
    /// The second of the two write paths a batch value must never reach (the first is
    /// SetFieldValuesAsync/ApplyValuesToAllAsync, guarded elsewhere): ApplyInitialValuesAsync loops
    /// every schema field when a document is adopted, and without the Scope filter it would expand
    /// a batch field's default straight into that document's row — exactly the per-row copy the
    /// group's bag exists to make impossible. A field with no DefaultValue can never exercise this
    /// loop's write regardless of scope, so Operator (which has one) is what actually pins it.
    /// </summary>
    [Fact]
    public async Task Initial_values_never_write_a_batch_fields_default_into_the_document()
    {
        var profile = await _profiles.CreateAsync("Evidence", Ct);
        var schema = await _profiles.SaveSchemaAsync(profile.Id,
        [
            new FieldDefinition
            {
                Name = "Operator", Type = FieldType.Text,
                Scope = FieldScope.Batch, DefaultValue = "$(user)",
            },
        ], Ct);

        var group = await _groups.CreateGroupAsync(_groupsRoot, "DirectWrite", (profile.Id, schema.Version), Ct);
        var incoming = Path.Combine(_db.Root, "incoming-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(incoming);
        var f = Path.Combine(incoming, "p1.png");
        await File.WriteAllBytesAsync(f, [1, 0xFF], Ct);

        var adopted = await _groups.AdoptPagesAsync(group.Id, [f], Ct);
        await _indexing.ApplyInitialValuesAsync(
            group.Id, [.. adopted.Adopted.Select(p => p.DocumentId)], null, Ct);

        await using var db = _db.Factory.CreateDbContext();
        var stored = await db.Documents.Where(d => d.GroupId == group.Id)
            .Select(d => d.CustomFieldsJson).ToListAsync(Ct);
        Assert.All(stored, json => Assert.DoesNotContain("Operator", json, StringComparison.Ordinal));
    }

    /// <summary>
    /// The third write path into CustomFieldsJson. Its only caller filters batch fields out one
    /// layer up, in the view model — so the guard the group's bag depends on lives outside the
    /// service that does the writing. Called directly, as here, nothing stops it.
    /// </summary>
    [Fact]
    public async Task Filling_every_row_never_writes_a_batch_field_into_the_document()
    {
        var group = await ArrangeAsync(pages: 2);

        var changed = await _indexing.ApplyValuesToAllAsync(
            group.Id,
            new Dictionary<string, string?> { ["Box"] = "12", ["Title"] = "Deed" },
            overwrite: false,
            Ct);

        Assert.Equal(2, changed);

        await using var db = _db.Factory.CreateDbContext();
        var stored = await db.Documents.Where(d => d.GroupId == group.Id)
            .Select(d => d.CustomFieldsJson).ToListAsync(Ct);
        Assert.All(stored, json => Assert.Contains("Deed", json, StringComparison.Ordinal));
        Assert.All(stored, json => Assert.DoesNotContain("Box", json, StringComparison.Ordinal));
    }
}
