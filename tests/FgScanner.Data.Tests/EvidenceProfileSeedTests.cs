using System.Text.Json;
using FgScanner.Core.Evidence;
using FgScanner.Data;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FgScanner.Data.Tests;

/// <summary>
/// The evidence profile was hand-entered field by field by the operator, which made a typo
/// ("NoteAuthour") a silent break in a legal pipeline: the JimsStuff importer parses these
/// names and cannot tell a misspelled field from an absent one.
/// </summary>
public sealed class EvidenceProfileSeedTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "fgscanner-tests", Guid.NewGuid().ToString("N"));
    private readonly string _dbPath;
    private readonly ProfileService _profiles;

    public EvidenceProfileSeedTests()
    {
        Directory.CreateDirectory(_root);
        _dbPath = Path.Combine(_root, "test.db");
        using (var db = new FgScannerDbContext(DbBootstrapper.BuildOptions(_dbPath)))
        {
            db.Database.Migrate();
        }

        _profiles = new ProfileService(new TestFactory(_dbPath));
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // A held file handle must not fail a passing test.
        }

        GC.SuppressFinalize(this);
    }

    private sealed class TestFactory(string dbPath) : IDbContextFactory<FgScannerDbContext>
    {
        public FgScannerDbContext CreateDbContext() => new(DbBootstrapper.BuildOptions(dbPath));
    }

    /// <summary>
    /// Tied to the contract rather than to a literal, so a fourteenth evidence field fails
    /// here instead of throwing in the operator's hands halfway through a box.
    /// </summary>
    [Fact]
    public void Field_cap_admits_the_whole_evidence_contract() =>
        Assert.True(ProfileService.MaxFields >= EvidenceProfile.Fields.Count);

    [Fact]
    public async Task Seeding_writes_every_contract_field_in_order()
    {
        var profile = await _profiles.EnsureEvidenceProfileAsync(TestContext.Current.CancellationToken);

        var schema = await _profiles.GetLatestSchemaAsync(profile.Id, TestContext.Current.CancellationToken);
        Assert.Equal(
            EvidenceProfile.Fields.Select(f => f.Name),
            schema.Fields.OrderBy(f => f.Order).Select(f => f.Name));
    }

    [Fact]
    public async Task Seeding_preserves_the_sticky_and_required_flags()
    {
        var profile = await _profiles.EnsureEvidenceProfileAsync(TestContext.Current.CancellationToken);

        var schema = await _profiles.GetLatestSchemaAsync(profile.Id, TestContext.Current.CancellationToken);
        var seeded = schema.Fields.ToDictionary(f => f.Name);
        foreach (var spec in EvidenceProfile.Fields)
        {
            Assert.Equal(spec.Required, seeded[spec.Name].Required);
            Assert.Equal(spec.Sticky, seeded[spec.Name].Sticky);
        }
    }

    [Fact]
    public async Task Seeding_writes_list_choices_as_json()
    {
        var profile = await _profiles.EnsureEvidenceProfileAsync(TestContext.Current.CancellationToken);

        var schema = await _profiles.GetLatestSchemaAsync(profile.Id, TestContext.Current.CancellationToken);
        var noteState = schema.Fields.Single(f => f.Name == "NoteState");
        var choices = JsonSerializer.Deserialize<string[]>(noteState.ListChoicesJson!);
        Assert.Equal(["as-found", "note-face", "clean"], choices!);
    }

    /// <summary>
    /// The button is pressable twice. A second schema version would leave every existing group
    /// a version behind and prompt the operator to upgrade for no change at all.
    /// </summary>
    [Fact]
    public async Task Seeding_twice_does_not_mint_a_second_schema_version()
    {
        var first = await _profiles.EnsureEvidenceProfileAsync(TestContext.Current.CancellationToken);
        var afterFirst = await _profiles.GetLatestSchemaAsync(first.Id, TestContext.Current.CancellationToken);

        var second = await _profiles.EnsureEvidenceProfileAsync(TestContext.Current.CancellationToken);

        Assert.Equal(first.Id, second.Id);
        var afterSecond = await _profiles.GetLatestSchemaAsync(second.Id, TestContext.Current.CancellationToken);
        Assert.Equal(afterFirst.Version, afterSecond.Version);
    }

    /// <summary>
    /// Seeding repairs a profile somebody has already damaged — that is most of the point.
    /// </summary>
    [Fact]
    public async Task Seeding_restores_a_field_the_operator_renamed()
    {
        var profile = await _profiles.EnsureEvidenceProfileAsync(TestContext.Current.CancellationToken);
        var schema = await _profiles.GetLatestSchemaAsync(profile.Id, TestContext.Current.CancellationToken);
        var damaged = schema.Fields.OrderBy(f => f.Order).ToList();
        damaged.Single(f => f.Name == "NoteAuthor").Name = "NoteAuthour";
        await _profiles.SaveSchemaAsync(profile.Id, damaged, TestContext.Current.CancellationToken);

        await _profiles.EnsureEvidenceProfileAsync(TestContext.Current.CancellationToken);

        var repaired = await _profiles.GetLatestSchemaAsync(profile.Id, TestContext.Current.CancellationToken);
        Assert.Contains(repaired.Fields, f => f.Name == "NoteAuthor");
        Assert.DoesNotContain(repaired.Fields, f => f.Name == "NoteAuthour");
    }
}
