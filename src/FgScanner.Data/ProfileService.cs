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

    public async Task UpdateOcrEnabledAsync(
        Guid profileId, bool ocrEnabled, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var profile = await db.Profiles.FirstAsync(p => p.Id == profileId, cancellationToken).ConfigureAwait(false);
        profile.OcrEnabled = ocrEnabled;
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
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

    // ---- .fgprofile import/export (PLAN §5.8) ----

    private static readonly System.Text.Json.JsonSerializerOptions FgProfileJsonOptions =
        new() { WriteIndented = true };

    private sealed record FgProfileFile(
        int FormatVersion, string Name, bool OcrEnabled,
        bool ExportCsv, bool ExportXlsx, bool ExportXml, bool ExportJson, string CsvDelimiter,
        List<FgProfileField> Fields);

    private sealed record FgProfileField(
        string Name, string Type, bool Required, bool Sticky, string? DefaultValue, string? ListChoicesJson);

    /// <summary>Serializes a profile + its latest schema as schema-versioned JSON (.fgprofile).</summary>
    public async Task<string> ExportProfileJsonAsync(Guid profileId, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var profile = await db.Profiles.FirstAsync(p => p.Id == profileId, cancellationToken).ConfigureAwait(false);
        var schema = await GetLatestSchemaAsync(profileId, cancellationToken).ConfigureAwait(false);
        var file = new FgProfileFile(
            1, profile.Name, profile.OcrEnabled,
            profile.ExportCsv, profile.ExportXlsx, profile.ExportXml, profile.ExportJson, profile.CsvDelimiter,
            [.. schema.Fields.Select(f => new FgProfileField(
                f.Name, f.Type.ToString(), f.Required, f.Sticky, f.DefaultValue, f.ListChoicesJson))]);
        return System.Text.Json.JsonSerializer.Serialize(file, FgProfileJsonOptions);
    }

    /// <summary>Creates a new profile from .fgprofile JSON; a taken name gets a numeric suffix.</summary>
    public async Task<Profile> ImportProfileJsonAsync(string json, CancellationToken cancellationToken = default)
    {
        var file = System.Text.Json.JsonSerializer.Deserialize<FgProfileFile>(json)
            ?? throw new InvalidOperationException("Not a valid .fgprofile file.");
        if (file.FormatVersion != 1)
        {
            throw new InvalidOperationException(
                $"Unsupported .fgprofile format version {file.FormatVersion} (this build reads version 1).");
        }

        var existing = (await ListAsync(cancellationToken).ConfigureAwait(false)).Select(p => p.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var name = file.Name;
        for (var suffix = 2; existing.Contains(name); suffix++)
        {
            name = $"{file.Name} ({suffix.ToString(System.Globalization.CultureInfo.InvariantCulture)})";
        }

        var profile = await CreateAsync(name, cancellationToken).ConfigureAwait(false);
        if (file.Fields.Count > 0)
        {
            await SaveSchemaAsync(
                profile.Id,
                [.. file.Fields.Select(f => new FieldDefinition
                {
                    Name = f.Name,
                    Type = Enum.Parse<FieldType>(f.Type),
                    Required = f.Required,
                    Sticky = f.Sticky,
                    DefaultValue = f.DefaultValue,
                    ListChoicesJson = f.ListChoicesJson,
                })],
                cancellationToken).ConfigureAwait(false);
        }

        await UpdateExportSettingsAsync(
            profile.Id, file.ExportCsv, file.ExportXlsx, file.ExportXml, file.ExportJson, file.CsvDelimiter,
            cancellationToken).ConfigureAwait(false);
        await UpdateOcrEnabledAsync(profile.Id, file.OcrEnabled, cancellationToken).ConfigureAwait(false);
        return profile;
    }
}
