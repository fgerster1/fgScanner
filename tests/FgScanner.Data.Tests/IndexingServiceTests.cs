using System.Text.Json;
using FgScanner.Core.Index;
using FgScanner.Data;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FgScanner.Data.Tests;

public sealed class IndexingServiceTests : IDisposable
{
    private readonly TestDb _db = new();
    private readonly GroupService _groups;
    private readonly ProfileService _profiles;
    private readonly IndexingService _indexing;
    private readonly string _groupsRoot;

    public IndexingServiceTests()
    {
        _groups = new GroupService(_db.Factory);
        _profiles = new ProfileService(_db.Factory);
        _indexing = new IndexingService(_db.Factory, _profiles, new IndexExporter());
        _groupsRoot = Path.Combine(_db.Root, "groups");
        Directory.CreateDirectory(_groupsRoot);
    }

    public void Dispose() => _db.Dispose();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<(Profile Profile, IndexSchema Schema)> CreateProfileWithFieldsAsync(
        bool xlsx = false, bool xml = false, bool json = false)
    {
        var profile = await _profiles.CreateAsync("Accounting", Ct);
        var schema = await _profiles.SaveSchemaAsync(profile.Id,
        [
            new FieldDefinition { Name = "Vendor", Type = FieldType.Text, Required = true },
            new FieldDefinition { Name = "InvoiceDate", Type = FieldType.Date },
            new FieldDefinition { Name = "Amount", Type = FieldType.Number, Sticky = true },
            new FieldDefinition { Name = "Origin", Type = FieldType.Text, DefaultValue = "$(group)" },
        ], Ct);
        await _profiles.UpdateExportSettingsAsync(profile.Id, csv: true, xlsx: xlsx, xml: xml, json: json, ",", Ct);
        return (profile, schema);
    }

    private async Task<Group> CreateGroupWithPagesAsync(IndexSchema schema, Guid profileId, int pages = 2)
    {
        var group = await _groups.CreateGroupAsync(_groupsRoot, "TestGroup", (profileId, schema.Version), Ct);
        var incoming = Path.Combine(_db.Root, "incoming-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(incoming);
        var files = new List<string>();
        for (var i = 1; i <= pages; i++)
        {
            var f = Path.Combine(incoming, $"p{i}.png");
            await File.WriteAllBytesAsync(f, [(byte)i, 0xFF], Ct);
            files.Add(f);
        }

        var adopted = await _groups.AdoptPagesAsync(group.Id, files, Ct);
        await _indexing.ApplyInitialValuesAsync(
            group.Id, [.. adopted.Adopted.Select(p => p.DocumentId)], null, Ct);
        return group;
    }

    [Fact]
    public async Task Defaults_and_tokens_apply_on_adoption()
    {
        var (profile, schema) = await CreateProfileWithFieldsAsync();
        var group = await CreateGroupWithPagesAsync(schema, profile.Id);

        var data = await _indexing.BuildExportDataAsync(group.Id, Ct);
        Assert.All(data.Rows, r => Assert.Equal("TestGroup", r.CustomValues["Origin"]));
    }

    [Fact]
    public async Task Pending_values_win_and_sticky_carries_forward()
    {
        var (profile, schema) = await CreateProfileWithFieldsAsync();
        var group = await _groups.CreateGroupAsync(_groupsRoot, "Sticky", (profile.Id, schema.Version), Ct);
        var incoming = Path.Combine(_db.Root, "in2");
        Directory.CreateDirectory(incoming);
        var f1 = Path.Combine(incoming, "a.png");
        var f2 = Path.Combine(incoming, "b.png");
        await File.WriteAllBytesAsync(f1, [1], Ct);
        await File.WriteAllBytesAsync(f2, [2], Ct);

        var first = await _groups.AdoptPagesAsync(group.Id, [f1], Ct);
        await _indexing.ApplyInitialValuesAsync(group.Id, [first.Adopted[0].DocumentId],
            new Dictionary<string, string?> { ["Vendor"] = "Acme", ["Amount"] = "10.5" }, Ct);

        var second = await _groups.AdoptPagesAsync(group.Id, [f2], Ct);
        await _indexing.ApplyInitialValuesAsync(group.Id, [second.Adopted[0].DocumentId], null, Ct);

        var data = await _indexing.BuildExportDataAsync(group.Id, Ct);
        Assert.Equal("10.5", data.Rows[1].CustomValues["Amount"]); // sticky carried
        Assert.False(data.Rows[1].CustomValues.ContainsKey("Vendor")); // non-sticky did not
    }

    [Fact]
    public async Task Commit_blocks_on_missing_required_field()
    {
        var (profile, schema) = await CreateProfileWithFieldsAsync();
        var group = await CreateGroupWithPagesAsync(schema, profile.Id);

        var (validation, export) = await _indexing.CommitGroupAsync(group.Id, Ct);

        Assert.True(validation.HasErrors); // Vendor required, never set
        Assert.Null(export);
        Assert.False(File.Exists(Path.Combine(group.DirectoryPath, "index.csv")));
    }

    [Fact]
    public async Task Commit_exports_all_enabled_formats_plus_manifest()
    {
        var (profile, schema) = await CreateProfileWithFieldsAsync(xlsx: true, xml: true, json: true);
        var group = await CreateGroupWithPagesAsync(schema, profile.Id);
        foreach (var doc in (await _indexing.ValidateAsync(group.Id, Ct)).Documents)
        {
            await _indexing.SetFieldValuesAsync(doc.DocumentId, new Dictionary<string, string?>
            {
                ["Vendor"] = "Acme",
                ["InvoiceDate"] = "2026-08-20",
                ["Amount"] = "5",
            }, Ct);
        }

        var (validation, export) = await _indexing.CommitGroupAsync(group.Id, Ct);

        Assert.False(validation.HasErrors);
        Assert.NotNull(export);
        Assert.True(export.AllSucceeded);
        foreach (var file in (string[])["index.csv", "index.xlsx", "index.xml", "index.json", "manifest.json"])
        {
            Assert.True(File.Exists(Path.Combine(group.DirectoryPath, file)), file);
        }

        var manifest = JsonDocument.Parse(
            await File.ReadAllTextAsync(Path.Combine(group.DirectoryPath, "manifest.json"), Ct));
        Assert.Equal("Accounting", manifest.RootElement.GetProperty("profile").GetString());
    }

    [Fact]
    public async Task Missed_page_inserts_at_position_and_reexports_in_order()
    {
        var (profile, schema) = await CreateProfileWithFieldsAsync();
        var group = await CreateGroupWithPagesAsync(schema, profile.Id, pages: 3);
        var missed = Path.Combine(_db.Root, "missed.png");
        await File.WriteAllBytesAsync(missed, [77], Ct);

        var doc = await _indexing.InsertMissedPageAsync(group.Id, missed, position: 2, Ct);

        var data = await _indexing.BuildExportDataAsync(group.Id, Ct);
        Assert.Equal(4, data.Rows.Count);
        Assert.Equal("scan_00004.png", data.Rows[1].ImageName); // new file, second position
        Assert.Equal(["scan_00001.png", "scan_00004.png", "scan_00002.png", "scan_00003.png"],
            data.Rows.Select(r => r.ImageName));
        Assert.Equal(2, doc.Sequence);
    }

    [Fact]
    public async Task Blank_page_reaches_json_flagged_but_stays_out_of_csv()
    {
        var (profile, schema) = await CreateProfileWithFieldsAsync(json: true);
        var group = await _groups.CreateGroupAsync(_groupsRoot, "Evidence", (profile.Id, schema.Version), Ct);
        var incoming = Path.Combine(_db.Root, "in-blank");
        Directory.CreateDirectory(incoming);
        var real = Path.Combine(incoming, "real.png");
        var blank = Path.Combine(incoming, "blank.png");
        await File.WriteAllBytesAsync(real, [1, 2, 3], Ct);
        await File.WriteAllBytesAsync(blank, [9], Ct);
        var adopted = await _groups.AdoptPagesAsync(
            group.Id, [real, blank], f => f.EndsWith("blank.png", StringComparison.Ordinal), Ct);
        await _indexing.ApplyInitialValuesAsync(
            group.Id, [.. adopted.Adopted.Select(p => p.DocumentId)], null, Ct);
        foreach (var doc in (await _indexing.ValidateAsync(group.Id, Ct)).Documents)
        {
            await _indexing.SetFieldValuesAsync(doc.DocumentId, new Dictionary<string, string?>
            {
                ["Vendor"] = "Acme",
                ["Amount"] = "5",
            }, Ct);
        }

        var (validation, export) = await _indexing.CommitGroupAsync(group.Id, Ct);

        Assert.False(validation.HasErrors); // the blank page never reaches validation
        Assert.NotNull(export);
        var json = JsonDocument.Parse(
            await File.ReadAllTextAsync(Path.Combine(group.DirectoryPath, "index.json"), Ct));
        var rows = json.RootElement.GetProperty("rows").EnumerateArray().ToList();
        Assert.Equal(2, rows.Count);
        var blankRow = Assert.Single(rows, r => r.GetProperty("isBlank").GetBoolean());
        Assert.Equal("scan_00002.png", blankRow.GetProperty("imageName").GetString());

        var csv = await File.ReadAllTextAsync(Path.Combine(group.DirectoryPath, "index.csv"), Ct);
        Assert.Contains("scan_00001.png", csv);
        Assert.DoesNotContain("scan_00002.png", csv);

        var manifest = JsonDocument.Parse(
            await File.ReadAllTextAsync(Path.Combine(group.DirectoryPath, "manifest.json"), Ct));
        Assert.Equal(1, manifest.RootElement.GetProperty("evidenceExport").GetInt32());
    }

    [Fact]
    public async Task Json_checksum_matches_the_bytes_on_disk()
    {
        var (profile, schema) = await CreateProfileWithFieldsAsync(json: true);
        var group = await CreateGroupWithPagesAsync(schema, profile.Id, pages: 1);

        await _indexing.ReexportAsync(group.Id, Ct);

        var json = JsonDocument.Parse(
            await File.ReadAllTextAsync(Path.Combine(group.DirectoryPath, "index.json"), Ct));
        var row = json.RootElement.GetProperty("rows").EnumerateArray().Single();
        var recomputed = await GroupService.ComputeSha256Async(
            Path.Combine(group.DirectoryPath, row.GetProperty("imageName").GetString()!), Ct);
        Assert.Equal(recomputed, row.GetProperty("checksum").GetString());
        Assert.NotEqual(Guid.Empty, Guid.Parse(row.GetProperty("pageId").GetString()!));
        // Never edited: the live file IS the original, and the contract says null, not "".
        Assert.Equal(JsonValueKind.Null, row.GetProperty("originalChecksum").ValueKind);
    }

    [Fact]
    public async Task Json_carries_the_original_checksum_once_recorded()
    {
        var (profile, schema) = await CreateProfileWithFieldsAsync(json: true);
        var group = await CreateGroupWithPagesAsync(schema, profile.Id, pages: 1);
        await using (var db = _db.Factory.CreateDbContext())
        {
            (await db.Pages.SingleAsync(Ct)).OriginalChecksum = new string('a', 64);
            await db.SaveChangesAsync(Ct);
        }

        await _indexing.ReexportAsync(group.Id, Ct);

        var json = JsonDocument.Parse(
            await File.ReadAllTextAsync(Path.Combine(group.DirectoryPath, "index.json"), Ct));
        var row = json.RootElement.GetProperty("rows").EnumerateArray().Single();
        Assert.Equal(new string('a', 64), row.GetProperty("originalChecksum").GetString());
    }

    [Fact]
    public async Task Json_sequence_follows_reorder_not_filename()
    {
        var (profile, schema) = await CreateProfileWithFieldsAsync(json: true);
        var group = await CreateGroupWithPagesAsync(schema, profile.Id, pages: 3);
        await new ReorderService(_db.Factory).ReverseAsync(group.Id, Ct);

        await _indexing.ReexportAsync(group.Id, Ct);

        var json = JsonDocument.Parse(
            await File.ReadAllTextAsync(Path.Combine(group.DirectoryPath, "index.json"), Ct));
        var rows = json.RootElement.GetProperty("rows").EnumerateArray().ToList();
        Assert.Equal(["scan_00003.png", "scan_00002.png", "scan_00001.png"],
            rows.Select(r => r.GetProperty("imageName").GetString()));
        Assert.Equal([1, 2, 3], rows.Select(r => r.GetProperty("sequence").GetInt32()));
    }

    [Fact]
    public async Task Schema_rejects_more_fields_than_the_cap_and_duplicate_names()
    {
        var profile = await _profiles.CreateAsync("Limits", Ct);

        // Relative to the cap, not a literal: this test pinned 12 in place, so raising the
        // cap for the 13-field evidence profile failed here rather than where it mattered.
        var overCap = Enumerable.Range(1, ProfileService.MaxFields + 1)
            .Select(i => new FieldDefinition { Name = $"F{i}", Type = FieldType.Text })
            .ToList();
        await Assert.ThrowsAsync<InvalidOperationException>(() => _profiles.SaveSchemaAsync(profile.Id, overCap, Ct));

        List<FieldDefinition> duplicates =
            [new() { Name = "Same", Type = FieldType.Text }, new() { Name = "same", Type = FieldType.Date }];
        await Assert.ThrowsAsync<InvalidOperationException>(() => _profiles.SaveSchemaAsync(profile.Id, duplicates, Ct));
    }

    [Fact]
    public async Task Schema_versions_are_immutable_and_increment()
    {
        var profile = await _profiles.CreateAsync("Versioned", Ct);
        var v2 = await _profiles.SaveSchemaAsync(profile.Id, [new FieldDefinition { Name = "A", Type = FieldType.Text }], Ct);
        var v3 = await _profiles.SaveSchemaAsync(profile.Id, [new FieldDefinition { Name = "B", Type = FieldType.Text }], Ct);

        Assert.Equal(2, v2.Version);
        Assert.Equal(3, v3.Version);
        Assert.Equal("A", Assert.Single((await _profiles.GetSchemaAsync(profile.Id, 2, Ct)).Fields).Name);
    }
}
