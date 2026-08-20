using Microsoft.EntityFrameworkCore;

namespace FgScanner.Data;

public sealed class ProfileService(IDbContextFactory<FgScannerDbContext> dbFactory)
{
    public const int MaxFields = 12;

    public async Task<IReadOnlyList<Profile>> ListAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        return await db.Profiles.OrderBy(p => p.Name).ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Every install has a "Default" profile so groups always have one to attach to.</summary>
    public async Task<Profile> EnsureDefaultAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var existing = await db.Profiles.FirstOrDefaultAsync(p => p.Name == "Default", cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            return existing;
        }

        var profile = new Profile { Id = Guid.NewGuid(), Name = "Default", CreatedUtc = DateTime.UtcNow };
        db.Profiles.Add(profile);
        db.IndexSchemas.Add(new IndexSchema
        {
            Id = Guid.NewGuid(),
            ProfileId = profile.Id,
            Version = 1,
            CreatedUtc = DateTime.UtcNow,
        });
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return profile;
    }

    public async Task<Profile> CreateAsync(string name, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var profile = new Profile { Id = Guid.NewGuid(), Name = name.Trim(), CreatedUtc = DateTime.UtcNow };
        db.Profiles.Add(profile);
        db.IndexSchemas.Add(new IndexSchema
        {
            Id = Guid.NewGuid(),
            ProfileId = profile.Id,
            Version = 1,
            CreatedUtc = DateTime.UtcNow,
        });
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return profile;
    }

    public async Task<IndexSchema> GetLatestSchemaAsync(Guid profileId, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        return await db.IndexSchemas
            .Include(s => s.Fields.OrderBy(f => f.Order))
            .Where(s => s.ProfileId == profileId)
            .OrderByDescending(s => s.Version)
            .FirstAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IndexSchema> GetSchemaAsync(Guid profileId, int version, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        return await db.IndexSchemas
            .Include(s => s.Fields.OrderBy(f => f.Order))
            .FirstAsync(s => s.ProfileId == profileId && s.Version == version, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Schema versions are immutable: saving field edits creates the next version (PLAN §5.3).
    /// Existing groups keep pointing at the version they were created with.
    /// </summary>
    public async Task<IndexSchema> SaveSchemaAsync(
        Guid profileId, IReadOnlyList<FieldDefinition> fields, CancellationToken cancellationToken = default)
    {
        if (fields.Count > MaxFields)
        {
            throw new InvalidOperationException($"A profile can have at most {MaxFields} custom fields.");
        }

        var duplicate = fields.GroupBy(f => f.Name.Trim(), StringComparer.OrdinalIgnoreCase).FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null)
        {
            throw new InvalidOperationException($"Field name \"{duplicate.Key}\" is used more than once.");
        }

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var latest = await db.IndexSchemas
            .Where(s => s.ProfileId == profileId)
            .MaxAsync(s => (int?)s.Version, cancellationToken).ConfigureAwait(false) ?? 0;
        var schema = new IndexSchema
        {
            Id = Guid.NewGuid(),
            ProfileId = profileId,
            Version = latest + 1,
            CreatedUtc = DateTime.UtcNow,
        };
        db.IndexSchemas.Add(schema);
        var order = 0;
        foreach (var field in fields)
        {
            db.FieldDefinitions.Add(new FieldDefinition
            {
                Id = Guid.NewGuid(),
                SchemaId = schema.Id,
                Order = order++,
                Name = field.Name.Trim(),
                Type = field.Type,
                Required = field.Required,
                Sticky = field.Sticky,
                DefaultValue = field.DefaultValue,
                ListChoicesJson = field.ListChoicesJson,
            });
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return schema;
    }

    public async Task UpdateExportSettingsAsync(
        Guid profileId, bool csv, bool xlsx, bool xml, bool json, string delimiter,
        CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var profile = await db.Profiles.FirstAsync(p => p.Id == profileId, cancellationToken).ConfigureAwait(false);
        profile.ExportCsv = csv;
        profile.ExportXlsx = xlsx;
        profile.ExportXml = xml;
        profile.ExportJson = json;
        profile.CsvDelimiter = string.IsNullOrEmpty(delimiter) ? "," : delimiter[..1];
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
