using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace FgScanner.Data;

public class FgScannerDbContext(DbContextOptions<FgScannerDbContext> options) : DbContext(options)
{
    public DbSet<Profile> Profiles => Set<Profile>();

    public DbSet<IndexSchema> IndexSchemas => Set<IndexSchema>();

    public DbSet<FieldDefinition> FieldDefinitions => Set<FieldDefinition>();

    public DbSet<Group> Groups => Set<Group>();

    public DbSet<Document> Documents => Set<Document>();

    public DbSet<Page> Pages => Set<Page>();

    public DbSet<QueuedJob> Jobs => Set<QueuedJob>();

    public DbSet<TrashItem> TrashItems => Set<TrashItem>();

    public DbSet<Setting> Settings => Set<Setting>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Profile>(e =>
        {
            e.HasIndex(p => p.Name).IsUnique();
        });

        modelBuilder.Entity<IndexSchema>(e =>
        {
            e.HasIndex(s => new { s.ProfileId, s.Version }).IsUnique();
            e.HasMany(s => s.Fields).WithOne(f => f.Schema).HasForeignKey(f => f.SchemaId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Group>(e =>
        {
            e.HasIndex(g => g.DirectoryPath).IsUnique();
            e.HasMany(g => g.Documents).WithOne(d => d.Group).HasForeignKey(d => d.GroupId)
                .OnDelete(DeleteBehavior.Cascade);
            // Explicit SQL default so groups that existed before this phase get valid JSON in the
            // new column, not the CLR default of "" that would fail to parse.
            e.Property(g => g.BatchFieldsJson).HasDefaultValue("{}");
        });

        modelBuilder.Entity<Document>(e =>
        {
            e.HasIndex(d => new { d.GroupId, d.Sequence });
            e.HasMany(d => d.Pages).WithOne(p => p.Document).HasForeignKey(p => p.DocumentId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Page>(e =>
        {
            e.HasIndex(p => p.Checksum);
        });

        modelBuilder.Entity<QueuedJob>(e =>
        {
            e.HasIndex(j => new { j.State, j.Type });
        });

        modelBuilder.Entity<TrashItem>(e =>
        {
            e.HasIndex(t => t.DeletedUtc);
        });

        modelBuilder.Entity<Setting>(e =>
        {
            e.HasKey(x => x.Key);
        });
    }
}

/// <summary>Design-time factory for `dotnet ef` migration commands.</summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<FgScannerDbContext>
{
    public FgScannerDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<FgScannerDbContext>()
            .UseSqlite("Data Source=design-time.db")
            .Options);
}
