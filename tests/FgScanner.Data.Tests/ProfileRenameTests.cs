using FgScanner.Data;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FgScanner.Data.Tests;

/// <summary>
/// Profile.Name was written only at creation and import, so correcting a typo meant Export then
/// Import — which produced a "(2)" copy and left the original behind.
/// </summary>
public sealed class ProfileRenameTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "fgscanner-tests", Guid.NewGuid().ToString("N"));
    private readonly string _dbPath;
    private readonly ProfileService _profiles;

    public ProfileRenameTests()
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
    public async Task Renaming_changes_the_name_and_keeps_the_same_profile()
    {
        var profile = await _profiles.CreateAsync("Invoces", TestContext.Current.CancellationToken);

        await _profiles.RenameAsync(profile.Id, "Invoices", TestContext.Current.CancellationToken);

        var all = await _profiles.ListAsync(TestContext.Current.CancellationToken);
        var renamed = Assert.Single(all, p => p.Id == profile.Id);
        Assert.Equal("Invoices", renamed.Name);
    }

    [Fact]
    public async Task Renaming_does_not_create_a_second_profile()
    {
        var profile = await _profiles.CreateAsync("Invoces", TestContext.Current.CancellationToken);
        var before = (await _profiles.ListAsync(TestContext.Current.CancellationToken)).Count;

        await _profiles.RenameAsync(profile.Id, "Invoices", TestContext.Current.CancellationToken);

        Assert.Equal(before, (await _profiles.ListAsync(TestContext.Current.CancellationToken)).Count);
    }

    [Fact]
    public async Task Renaming_keeps_the_schema_and_its_version()
    {
        var profile = await _profiles.CreateAsync("Invoces", TestContext.Current.CancellationToken);
        await _profiles.SaveSchemaAsync(
            profile.Id,
            [new FieldDefinition { Name = "Vendor", Type = FieldType.Text, Order = 0 }],
            TestContext.Current.CancellationToken);
        var before = await _profiles.GetLatestSchemaAsync(profile.Id, TestContext.Current.CancellationToken);

        await _profiles.RenameAsync(profile.Id, "Invoices", TestContext.Current.CancellationToken);

        var after = await _profiles.GetLatestSchemaAsync(profile.Id, TestContext.Current.CancellationToken);
        Assert.Equal(before.Version, after.Version);
        Assert.Equal("Vendor", Assert.Single(after.Fields).Name);
    }

    [Fact]
    public async Task A_name_already_in_use_is_refused()
    {
        await _profiles.CreateAsync("Invoices", TestContext.Current.CancellationToken);
        var other = await _profiles.CreateAsync("Receipts", TestContext.Current.CancellationToken);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _profiles.RenameAsync(other.Id, "Invoices", TestContext.Current.CancellationToken));
        Assert.Contains("already exists", ex.Message);
    }

    [Fact]
    public async Task An_empty_name_is_refused()
    {
        var profile = await _profiles.CreateAsync("Invoices", TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _profiles.RenameAsync(profile.Id, "   ", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Renaming_to_its_own_name_is_allowed()
    {
        var profile = await _profiles.CreateAsync("Invoices", TestContext.Current.CancellationToken);

        await _profiles.RenameAsync(profile.Id, "Invoices", TestContext.Current.CancellationToken);

        Assert.Equal("Invoices", Assert.Single(
            await _profiles.ListAsync(TestContext.Current.CancellationToken), p => p.Id == profile.Id).Name);
    }
}
