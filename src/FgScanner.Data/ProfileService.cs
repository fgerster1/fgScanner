using System.Text.Json;
using FgScanner.Core.Evidence;
using Microsoft.EntityFrameworkCore;

namespace FgScanner.Data;

public sealed class ProfileService(IDbContextFactory<FgScannerDbContext> dbFactory)
{
    /// <summary>
    /// PLAN §8 set this at 12 to keep the pre-scan field editor usable; nothing downstream
    /// is bounded by it (the XSD and the JSON/CSV writers are unbounded). The evidence
    /// capture profile is 13 fields, so the editor's comfort was costing a legal contract.
    /// </summary>
    public const int MaxFields = 16;

    /// <summary>The profile <see cref="EnsureEvidenceProfileAsync"/> creates and repairs.</summary>
    public const string EvidenceProfileName = "Evidence";

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

    /// <summary>
    /// Creates the evidence capture profile, or repairs an existing one back to the contract.
    /// The field names are parsed by the JimsStuff importer, and hand-entering thirteen of
    /// them made one typo a silent break in a legal pipeline. Idempotent: re-seeding an
    /// intact profile mints no schema version, so the button is safe to press twice.
    /// </summary>
    public async Task<Profile> EnsureEvidenceProfileAsync(CancellationToken cancellationToken = default)
    {
        Profile? profile;
        await using (var db = await dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false))
        {
            profile = await db.Profiles
                .FirstOrDefaultAsync(p => p.Name == EvidenceProfileName, cancellationToken)
                .ConfigureAwait(false);
        }

        profile ??= await CreateAsync(EvidenceProfileName, cancellationToken).ConfigureAwait(false);

        var fields = EvidenceProfile.Fields
            .Select(spec => new FieldDefinition
            {
                Name = spec.Name,
                Type = (FieldType)spec.Type,
                Required = spec.Required,
                Sticky = spec.Sticky,
                DefaultValue = spec.DefaultValue,
                ListChoicesJson = spec.ListChoices is { Count: > 0 } choices
                    ? JsonSerializer.Serialize(choices)
                    : null,
            })
            .ToList();

        await SaveSchemaAsync(profile.Id, fields, cancellationToken).ConfigureAwait(false);
        return profile;
    }

    /// <summary>
    /// Sets the root folder new groups for this profile are created under. Empty restores
    /// "ask every time".
    /// </summary>
    public async Task UpdateBaseDirectoryAsync(
        Guid profileId, string baseDirectory, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var profile = await db.Profiles
            .FirstAsync(p => p.Id == profileId, cancellationToken).ConfigureAwait(false);
        profile.BaseDirectory = baseDirectory.Trim();
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Deletes a profile, refusing while any group still uses it. Group.ProfileId carries no
    /// declared delete behaviour, so an unguarded delete would either cascade the groups away or
    /// throw at runtime — and opening a group resolves its schema through the profile, so a group
    /// whose profile vanished would lose its columns and leave CustomFieldsJson unreachable.
    /// The last profile is protected because EnsureDefaultAsync assumes one exists.
    /// </summary>
    public async Task DeleteAsync(Guid profileId, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var profile = await db.Profiles
            .FirstAsync(p => p.Id == profileId, cancellationToken).ConfigureAwait(false);

        if (await db.Profiles.CountAsync(cancellationToken).ConfigureAwait(false) <= 1)
        {
            throw new InvalidOperationException(
                $"\"{profile.Name}\" is the last profile and cannot be deleted.");
        }

        var usedBy = await db.Groups
            .CountAsync(g => g.ProfileId == profileId, cancellationToken).ConfigureAwait(false);
        if (usedBy > 0)
        {
            throw new InvalidOperationException(
                $"\"{profile.Name}\" is used by {usedBy} group(s). Move or delete those groups first.");
        }

        var schemas = await db.IndexSchemas
            .Where(s => s.ProfileId == profileId)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        db.IndexSchemas.RemoveRange(schemas); // field definitions cascade from the schema
        db.Profiles.Remove(profile);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Renames a profile. Until now Name was written only at creation and import, so the only way
    /// to correct a typo was Export then Import, which produced a "(2)" copy and left the original.
    /// </summary>
    public async Task RenameAsync(Guid profileId, string newName, CancellationToken cancellationToken = default)
    {
        var trimmed = newName.Trim();
        if (trimmed.Length == 0)
        {
            throw new InvalidOperationException("A profile name cannot be empty.");
        }

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var clash = await db.Profiles
            .AnyAsync(p => p.Id != profileId && p.Name == trimmed, cancellationToken).ConfigureAwait(false);
        if (clash)
        {
            throw new InvalidOperationException($"A profile named \"{trimmed}\" already exists.");
        }

        var profile = await db.Profiles
            .FirstAsync(p => p.Id == profileId, cancellationToken).ConfigureAwait(false);
        profile.Name = trimmed;
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
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

        // Saving a layout that has not actually changed used to mint a version anyway, so clicking
        // Save twice left every existing group two versions behind for no reason.
        var current = await db.IndexSchemas
            .Include(s => s.Fields.OrderBy(f => f.Order))
            .Where(s => s.ProfileId == profileId)
            .OrderByDescending(s => s.Version)
            .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        if (current is not null && Unchanged(current.Fields, fields))
        {
            return current;
        }

        var latest = current?.Version ?? 0;
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
                Scope = field.Scope,
                DefaultValue = field.DefaultValue,
                ListChoicesJson = field.ListChoicesJson,
            });
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return schema;
    }

    /// <summary>
    /// Whether a submitted layout is identical to the stored one, field for field and in order.
    /// Required and Sticky are compared because they change behaviour — treating a flag flip as
    /// cosmetic would silently leave groups on a layout that validates differently.
    /// </summary>
    private static bool Unchanged(
        List<FieldDefinition> stored, IReadOnlyList<FieldDefinition> submitted)
    {
        if (stored.Count != submitted.Count)
        {
            return false;
        }

        return !stored.Where((field, i) =>
            !string.Equals(field.Name, submitted[i].Name.Trim(), StringComparison.Ordinal)
            || field.Type != submitted[i].Type
            || field.Required != submitted[i].Required
            || field.Sticky != submitted[i].Sticky
            || field.Scope != submitted[i].Scope
            || field.DefaultValue != submitted[i].DefaultValue
            || field.ListChoicesJson != submitted[i].ListChoicesJson).Any();
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

    public async Task UpdateCapturePolicyAsync(
        Guid profileId, bool separatorDetection, bool keepSeparators,
        FgScanner.Core.Capture.BlankPagePolicy blankPolicy, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var profile = await db.Profiles.FirstAsync(p => p.Id == profileId, cancellationToken).ConfigureAwait(false);
        profile.SeparatorDetectionEnabled = separatorDetection;
        profile.KeepSeparatorPages = keepSeparators;
        profile.BlankPolicy = blankPolicy;
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    // ---- .fgprofile import/export (PLAN §5.8) ----

    private static readonly System.Text.Json.JsonSerializerOptions FgProfileJsonOptions =
        new() { WriteIndented = true };

    private sealed record FgProfileFile(
        int FormatVersion, string Name, bool OcrEnabled,
        bool ExportCsv, bool ExportXlsx, bool ExportXml, bool ExportJson, string CsvDelimiter,
        List<FgProfileField> Fields)
    {
        // Capture triage (phase 10). Init-props with defaults so version-1 files without them still load.
        public bool SeparatorDetectionEnabled { get; init; }

        public bool KeepSeparatorPages { get; init; }

        public string BlankPolicy { get; init; } = nameof(FgScanner.Core.Capture.BlankPagePolicy.Keep);
    }

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
                f.Name, f.Type.ToString(), f.Required, f.Sticky, f.DefaultValue, f.ListChoicesJson))])
        {
            SeparatorDetectionEnabled = profile.SeparatorDetectionEnabled,
            KeepSeparatorPages = profile.KeepSeparatorPages,
            BlankPolicy = profile.BlankPolicy.ToString(),
        };
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
        await UpdateCapturePolicyAsync(
            profile.Id, file.SeparatorDetectionEnabled, file.KeepSeparatorPages,
            Enum.TryParse<FgScanner.Core.Capture.BlankPagePolicy>(file.BlankPolicy, out var policy)
                ? policy
                : FgScanner.Core.Capture.BlankPagePolicy.Keep,
            cancellationToken).ConfigureAwait(false);
        return profile;
    }
}
