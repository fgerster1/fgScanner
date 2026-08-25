using FgScanner.Data;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FgScanner.Data.Tests;

/// <summary>
/// Deleting a profile did not exist. Group.ProfileId is a nullable FK with no declared delete
/// behaviour, so an unguarded delete would either cascade groups away or throw at runtime, and
/// opening a group resolves its schema through that profile.
/// </summary>
public sealed class DeleteProfileTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "fgscanner-tests", Guid.NewGuid().ToString("N"));
    private readonly string _dbPath;
    private readonly ProfileService _profiles;
    private readonly GroupService _groups;

    public DeleteProfileTests()
    {
        Directory.CreateDirectory(_root);
        _dbPath = Path.Combine(_root, "test.db");
        using (var db = new FgScannerDbContext(DbBootstrapper.BuildOptions(_dbPath)))
        {
            db.Database.Migrate();
        }

        _profiles = new ProfileService(new TestFactory(_dbPath));
        _groups = new GroupService(new TestFactory(_dbPath));
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

    [Fact]
    public async Task An_unused_profile_can_be_deleted()
    {
        await _profiles.CreateAsync("Keep", TestContext.Current.CancellationToken);
        var doomed = await _profiles.CreateAsync("Doomed", TestContext.Current.CancellationToken);

        await _profiles.DeleteAsync(doomed.Id, TestContext.Current.CancellationToken);

        var remaining = await _profiles.ListAsync(TestContext.Current.CancellationToken);
        Assert.DoesNotContain(remaining, p => p.Id == doomed.Id);
    }

    [Fact]
    public async Task Deleting_a_profile_removes_its_schemas_and_fields()
    {
        await _profiles.CreateAsync("Keep", TestContext.Current.CancellationToken);
        var doomed = await _profiles.CreateAsync("Doomed", TestContext.Current.CancellationToken);
        await _profiles.SaveSchemaAsync(
            doomed.Id,
            [new FieldDefinition { Name = "Vendor", Type = FieldType.Text, Order = 0 }],
            TestContext.Current.CancellationToken);

        await _profiles.DeleteAsync(doomed.Id, TestContext.Current.CancellationToken);

        await using var db = new FgScannerDbContext(DbBootstrapper.BuildOptions(_dbPath));
        Assert.Empty(await db.IndexSchemas.Where(s => s.ProfileId == doomed.Id)
            .ToListAsync(TestContext.Current.CancellationToken));
        Assert.Empty(await db.FieldDefinitions.Where(f => f.Name == "Vendor")
            .ToListAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_profile_still_used_by_groups_is_refused_and_the_message_says_how_many()
    {
        await _profiles.CreateAsync("Spare", TestContext.Current.CancellationToken);
        var inUse = await _profiles.CreateAsync("InUse", TestContext.Current.CancellationToken);
        var schema = await _profiles.GetLatestSchemaAsync(inUse.Id, TestContext.Current.CancellationToken);
        await _groups.CreateGroupAsync(_root, "A", (inUse.Id, schema.Version), TestContext.Current.CancellationToken);
        await _groups.CreateGroupAsync(_root, "B", (inUse.Id, schema.Version), TestContext.Current.CancellationToken);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _profiles.DeleteAsync(inUse.Id, TestContext.Current.CancellationToken));

        Assert.Contains("2", ex.Message);
        Assert.NotEmpty(await _profiles.ListAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Groups_using_the_profile_are_left_untouched_when_the_delete_is_refused()
    {
        var inUse = await _profiles.CreateAsync("InUse", TestContext.Current.CancellationToken);
        await _profiles.CreateAsync("Spare", TestContext.Current.CancellationToken);
        var schema = await _profiles.GetLatestSchemaAsync(inUse.Id, TestContext.Current.CancellationToken);
        var group = await _groups.CreateGroupAsync(
            _root, "A", (inUse.Id, schema.Version), TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _profiles.DeleteAsync(inUse.Id, TestContext.Current.CancellationToken));

        var reloaded = await _groups.FindAsync(group.Id, TestContext.Current.CancellationToken);
        Assert.Equal(inUse.Id, reloaded!.ProfileId);
    }

    [Fact]
    public async Task The_last_profile_cannot_be_deleted()
    {
        var only = await _profiles.EnsureDefaultAsync(TestContext.Current.CancellationToken);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _profiles.DeleteAsync(only.Id, TestContext.Current.CancellationToken));

        // EnsureDefaultAsync assumes one exists; an empty table would strand the app with no schema.
        Assert.Contains("last", ex.Message, StringComparison.OrdinalIgnoreCase);
    }
}
