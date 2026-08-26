using FgScanner.Core.Index;
using FgScanner.Data;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FgScanner.Data.Tests;

/// <summary>
/// Moving an existing group onto a newer field layout, and filling those fields across rows.
///
/// Every profile's v1 is born with no fields, and a group keeps the version it was created with.
/// A user who creates a group, then defines their fields, ends up with a group permanently pinned
/// to a zero-field schema and no way out but deleting and recreating it — which is exactly what
/// happened in practice. The two halves ship together on purpose: re-pointing a group at a layout
/// whose fields are Required, without a way to fill them, only trades "no fields" for "four
/// mandatory empty fields blocking commit".
/// </summary>
public sealed class SchemaUpgradeTests : IDisposable
{
    private readonly TestDb _db = new();
    private readonly GroupService _groups;
    private readonly ProfileService _profiles;
    private readonly IndexingService _indexing;
    private readonly string _root;

    public SchemaUpgradeTests()
    {
        _groups = new GroupService(_db.Factory);
        _profiles = new ProfileService(_db.Factory);
        _indexing = new IndexingService(_db.Factory, _profiles, new IndexExporter());
        _root = Path.Combine(_db.Root, "groups");
        Directory.CreateDirectory(_root);
    }

    public void Dispose() => _db.Dispose();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>Their real shape: every field Required, none Sticky.</summary>
    private static List<FieldDefinition> RealWorldFields() =>
    [
        new() { Name = "Came From", Type = FieldType.Text, Required = true },
        new() { Name = "Recieved", Type = FieldType.Date, Required = true },
    ];

    /// <summary>A group created before its profile had any fields — the situation being fixed.</summary>
    private async Task<(Profile Profile, Group Group)> GroupPinnedToEmptySchemaAsync(string name)
    {
        var profile = await _profiles.CreateAsync(name, Ct);
        var empty = await _profiles.GetLatestSchemaAsync(profile.Id, Ct);
        var group = await _groups.CreateGroupAsync(
            _root, name, (profile.Id, empty.Version), Ct);
        await _profiles.SaveSchemaAsync(profile.Id, RealWorldFields(), Ct);
        return (profile, group);
    }

    private async Task<List<Guid>> AddDocumentsAsync(Group group, int count)
    {
        var incoming = Path.Combine(_db.Root, "in-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(incoming);
        var files = new List<string>();
        for (var i = 1; i <= count; i++)
        {
            var file = Path.Combine(incoming, $"p{i}.png");
            await File.WriteAllBytesAsync(file, [(byte)i], Ct);
            files.Add(file);
        }

        var adopted = await _groups.AdoptPagesAsync(group.Id, files, Ct);
        return [.. adopted.Adopted.Select(p => p.DocumentId)];
    }

    private async Task<Dictionary<string, string?>> ValuesOfAsync(Guid documentId)
    {
        await using var db = _db.Factory.CreateDbContext();
        var doc = await db.Documents.SingleAsync(d => d.Id == documentId, Ct);
        return System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string?>>(
            doc.CustomFieldsJson) ?? [];
    }

    [Fact]
    public async Task A_group_can_be_moved_onto_a_newer_field_layout()
    {
        var (profile, group) = await GroupPinnedToEmptySchemaAsync("Upgradable");
        var latest = await _profiles.GetLatestSchemaAsync(profile.Id, Ct);

        await _groups.UpgradeSchemaVersionAsync(group.Id, latest.Version, Ct);

        var reloaded = await _groups.FindAsync(group.Id, Ct);
        Assert.Equal(latest.Version, reloaded!.SchemaVersion);
    }

    [Fact]
    public async Task Upgrading_reports_which_groups_are_behind_so_the_user_can_be_asked()
    {
        var (profile, group) = await GroupPinnedToEmptySchemaAsync("Behind");

        var stale = await _groups.GroupsOnOlderSchemaAsync(profile.Id, Ct);

        var only = Assert.Single(stale);
        Assert.Equal(group.Id, only.Id);
    }

    [Fact]
    public async Task A_group_already_on_the_latest_layout_is_not_reported_as_behind()
    {
        var (profile, group) = await GroupPinnedToEmptySchemaAsync("Current");
        var latest = await _profiles.GetLatestSchemaAsync(profile.Id, Ct);
        await _groups.UpgradeSchemaVersionAsync(group.Id, latest.Version, Ct);

        Assert.Empty(await _groups.GroupsOnOlderSchemaAsync(profile.Id, Ct));
    }

    [Fact]
    public async Task Upgrading_keeps_the_values_of_fields_that_still_exist()
    {
        // A rename or removal must not silently discard what the user already typed elsewhere.
        var (profile, group) = await GroupPinnedToEmptySchemaAsync("Preserving");
        var latest = await _profiles.GetLatestSchemaAsync(profile.Id, Ct);
        await _groups.UpgradeSchemaVersionAsync(group.Id, latest.Version, Ct);
        var documents = await AddDocumentsAsync(group, 1);
        await _indexing.SetFieldValuesAsync(documents[0], new Dictionary<string, string?> { ["Came From"] = "Jim" }, Ct);

        await _profiles.SaveSchemaAsync(
            profile.Id, [.. RealWorldFields(), new() { Name = "Extra", Type = FieldType.Text }], Ct);
        var newest = await _profiles.GetLatestSchemaAsync(profile.Id, Ct);
        await _groups.UpgradeSchemaVersionAsync(group.Id, newest.Version, Ct);

        Assert.Equal("Jim", (await ValuesOfAsync(documents[0]))["Came From"]);
    }

    [Fact]
    public async Task A_version_from_another_profile_is_refused()
    {
        var (_, group) = await GroupPinnedToEmptySchemaAsync("Mine");
        var stranger = await _profiles.CreateAsync("Stranger", Ct);
        await _profiles.SaveSchemaAsync(stranger.Id, RealWorldFields(), Ct);
        var strangerLatest = await _profiles.GetLatestSchemaAsync(stranger.Id, Ct);

        // Version numbers restart per profile, so "2" exists in both — the guard has to check
        // ownership, not just that some schema with that number exists.
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _groups.UpgradeSchemaVersionAsync(group.Id, strangerLatest.Version + 10, Ct));
    }

    [Fact]
    public async Task Filling_values_across_rows_reaches_every_document()
    {
        var (profile, group) = await GroupPinnedToEmptySchemaAsync("Bulk");
        var latest = await _profiles.GetLatestSchemaAsync(profile.Id, Ct);
        await _groups.UpgradeSchemaVersionAsync(group.Id, latest.Version, Ct);
        var documents = await AddDocumentsAsync(group, 3);

        var filled = await _indexing.ApplyValuesToAllAsync(
            group.Id, new Dictionary<string, string?> { ["Came From"] = "Jim" }, overwrite: false, Ct);

        Assert.Equal(3, filled);
        foreach (var id in documents)
        {
            Assert.Equal("Jim", (await ValuesOfAsync(id))["Came From"]);
        }
    }

    [Fact]
    public async Task Filling_leaves_rows_that_already_have_a_value_alone()
    {
        // The common case is filling in what is missing after realising the fields were absent.
        // Overwriting work the user already did by hand would be the worse default.
        var (profile, group) = await GroupPinnedToEmptySchemaAsync("Respectful");
        var latest = await _profiles.GetLatestSchemaAsync(profile.Id, Ct);
        await _groups.UpgradeSchemaVersionAsync(group.Id, latest.Version, Ct);
        var documents = await AddDocumentsAsync(group, 2);
        await _indexing.SetFieldValuesAsync(documents[0], new Dictionary<string, string?> { ["Came From"] = "Typed by hand" }, Ct);

        var filled = await _indexing.ApplyValuesToAllAsync(
            group.Id, new Dictionary<string, string?> { ["Came From"] = "Bulk" }, overwrite: false, Ct);

        Assert.Equal(1, filled);
        Assert.Equal("Typed by hand", (await ValuesOfAsync(documents[0]))["Came From"]);
        Assert.Equal("Bulk", (await ValuesOfAsync(documents[1]))["Came From"]);
    }

    [Fact]
    public async Task Filling_can_be_told_to_replace_existing_values()
    {
        var (profile, group) = await GroupPinnedToEmptySchemaAsync("Replacing");
        var latest = await _profiles.GetLatestSchemaAsync(profile.Id, Ct);
        await _groups.UpgradeSchemaVersionAsync(group.Id, latest.Version, Ct);
        var documents = await AddDocumentsAsync(group, 1);
        await _indexing.SetFieldValuesAsync(documents[0], new Dictionary<string, string?> { ["Came From"] = "Wrong" }, Ct);

        await _indexing.ApplyValuesToAllAsync(
            group.Id, new Dictionary<string, string?> { ["Came From"] = "Right" }, overwrite: true, Ct);

        Assert.Equal("Right", (await ValuesOfAsync(documents[0]))["Came From"]);
    }

    [Fact]
    public async Task Filling_refuses_a_value_that_the_field_type_rejects()
    {
        // Without this a malformed date is stamped onto every row at once, and only surfaces
        // one row at a time at commit.
        var (profile, group) = await GroupPinnedToEmptySchemaAsync("Validating");
        var latest = await _profiles.GetLatestSchemaAsync(profile.Id, Ct);
        await _groups.UpgradeSchemaVersionAsync(group.Id, latest.Version, Ct);
        await AddDocumentsAsync(group, 1);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _indexing.ApplyValuesToAllAsync(
                group.Id, new Dictionary<string, string?> { ["Recieved"] = "not-a-date" }, false, Ct));
    }

    [Fact]
    public async Task Filling_ignores_a_field_that_is_not_in_the_group_layout()
    {
        var (profile, group) = await GroupPinnedToEmptySchemaAsync("Unknown");
        var latest = await _profiles.GetLatestSchemaAsync(profile.Id, Ct);
        await _groups.UpgradeSchemaVersionAsync(group.Id, latest.Version, Ct);
        var documents = await AddDocumentsAsync(group, 1);

        await _indexing.ApplyValuesToAllAsync(
            group.Id, new Dictionary<string, string?> { ["Nonexistent"] = "x" }, false, Ct);

        Assert.DoesNotContain("Nonexistent", (await ValuesOfAsync(documents[0])).Keys);
    }

    [Fact]
    public async Task Resaving_an_unchanged_layout_does_not_mint_a_new_version()
    {
        // Clicking Save twice produced v3, v4 and v5 with identical fields, each one a version
        // every existing group then trailed behind.
        var profile = await _profiles.CreateAsync("Churn", Ct);
        var first = await _profiles.SaveSchemaAsync(profile.Id, RealWorldFields(), Ct);

        var second = await _profiles.SaveSchemaAsync(profile.Id, RealWorldFields(), Ct);

        Assert.Equal(first.Version, second.Version);
    }

    [Fact]
    public async Task A_real_change_still_mints_a_new_version()
    {
        var profile = await _profiles.CreateAsync("Changing", Ct);
        var first = await _profiles.SaveSchemaAsync(profile.Id, RealWorldFields(), Ct);

        var second = await _profiles.SaveSchemaAsync(
            profile.Id, [.. RealWorldFields(), new() { Name = "Added", Type = FieldType.Text }], Ct);

        Assert.Equal(first.Version + 1, second.Version);
    }

    [Fact]
    public async Task Changing_only_a_flag_counts_as_a_change()
    {
        // Required and Sticky alter behaviour, so they cannot be treated as cosmetic.
        var profile = await _profiles.CreateAsync("Flags", Ct);
        var first = await _profiles.SaveSchemaAsync(
            profile.Id, [new() { Name = "A", Type = FieldType.Text, Required = false }], Ct);

        var second = await _profiles.SaveSchemaAsync(
            profile.Id, [new() { Name = "A", Type = FieldType.Text, Required = true }], Ct);

        Assert.Equal(first.Version + 1, second.Version);
    }
}
