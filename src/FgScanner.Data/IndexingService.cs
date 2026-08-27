using System.Text.Json;
using FgScanner.Core.Index;
using Microsoft.EntityFrameworkCore;

namespace FgScanner.Data;

public sealed record DocumentValidation(Guid DocumentId, string ImageName, IReadOnlyList<string> Errors);

public sealed record GroupValidation(IReadOnlyList<DocumentValidation> Documents)
{
    public bool HasErrors => Documents.Any(d => d.Errors.Count > 0);

    public int ErrorCount => Documents.Sum(d => d.Errors.Count);
}

/// <summary>
/// Bridges the database and the Core export pipeline: field values, validation, commit,
/// re-export, and the add-missed-page flow. The DB is the source of truth; index files
/// are regenerated projections (PLAN §5.2).
/// </summary>
public sealed class IndexingService(
    IDbContextFactory<FgScannerDbContext> dbFactory,
    ProfileService profileService,
    IndexExporter exporter,
    CommitHookRunner? commitHooks = null)
{
    private static readonly JsonSerializerOptions JsonOptions = new();

    public static string AppVersion { get; set; } = "0.0.0";

    // ---- field values ----

    /// <summary>
    /// Stored field values for every document in the group, blank-flagged documents included.
    /// The export projection deliberately omits blanks (they never reach an index file), so the
    /// grid must not source values from it: doing so rendered blank rows empty and the first edit
    /// then persisted that emptiness over the real values (BUG-3, docs/roadmap-v0.2.md).
    /// </summary>
    public async Task<IReadOnlyDictionary<Guid, IReadOnlyDictionary<string, string?>>> GetStoredFieldValuesAsync(
        Guid groupId, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var documents = await db.Documents
            .Where(d => d.GroupId == groupId)
            .Select(d => new { d.Id, d.CustomFieldsJson })
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        return documents.ToDictionary(
            d => d.Id,
            d => (IReadOnlyDictionary<string, string?>)(
                JsonSerializer.Deserialize<Dictionary<string, string?>>(d.CustomFieldsJson) ?? []));
    }

    public async Task SetFieldValuesAsync(
        Guid documentId, IReadOnlyDictionary<string, string?> values, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var document = await db.Documents.FirstAsync(d => d.Id == documentId, cancellationToken).ConfigureAwait(false);
        document.CustomFieldsJson = JsonSerializer.Serialize(
            values.Where(kv => kv.Value is not null).ToDictionary(kv => kv.Key, kv => kv.Value), JsonOptions);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Writes the same field values across every document in a group, and returns how many rows
    /// changed.
    ///
    /// This is what a user reaches for after discovering their fields were missing while they
    /// scanned: the pending-values mechanism only reaches documents adopted after the fact, and
    /// filling a hundred rows by hand is not a fix. With <paramref name="overwrite"/> false — the
    /// default the UI uses — a row that already holds a value keeps it, because the common case is
    /// completing what is blank rather than replacing work already done by hand.
    ///
    /// Values are validated against the field type once, up front. Stamping an unparseable date
    /// onto every row and surfacing it one row at a time at commit is the failure this avoids.
    /// </summary>
    public async Task<int> ApplyValuesToAllAsync(
        Guid groupId,
        IReadOnlyDictionary<string, string?> values,
        bool overwrite = false,
        CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var group = await db.Groups.FirstAsync(g => g.Id == groupId, cancellationToken).ConfigureAwait(false);
        if (group.ProfileId is null)
        {
            return 0;
        }

        var schema = await profileService
            .GetSchemaAsync(group.ProfileId.Value, group.SchemaVersion, cancellationToken).ConfigureAwait(false);

        // A value for a field this group's layout does not have would never be shown or exported,
        // so it is dropped rather than written into a row where nothing could reach it again.
        var applicable = new Dictionary<string, string?>();
        foreach (var field in schema.Fields)
        {
            if (!values.TryGetValue(field.Name, out var value) || string.IsNullOrEmpty(value))
            {
                continue;
            }

            var error = FieldValidator.Validate(
                new IndexFieldDef(field.Name, (IndexFieldType)field.Type, field.Required),
                value,
                ParseChoices(field.ListChoicesJson));
            if (error is not null)
            {
                throw new InvalidOperationException($"{field.Name}: {error}");
            }

            applicable[field.Name] = value;
        }

        if (applicable.Count == 0)
        {
            return 0;
        }

        var documents = await db.Documents
            .Where(d => d.GroupId == groupId)
            .OrderBy(d => d.Sequence)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        var changed = 0;
        foreach (var document in documents)
        {
            var current = JsonSerializer.Deserialize<Dictionary<string, string?>>(document.CustomFieldsJson) ?? [];
            var touched = false;
            foreach (var (name, value) in applicable)
            {
                if (!overwrite && current.TryGetValue(name, out var existing) && !string.IsNullOrEmpty(existing))
                {
                    continue;
                }

                current[name] = value;
                touched = true;
            }

            if (touched)
            {
                document.CustomFieldsJson = JsonSerializer.Serialize(current, JsonOptions);
                changed++;
            }
        }

        if (changed > 0)
        {
            group.UpdatedUtc = DateTime.UtcNow;
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        return changed;
    }

    /// <summary>
    /// Applies initial values to freshly adopted documents: explicit pending values win, then
    /// sticky values from the previous document, then expanded defaults (PLAN §5.4).
    /// </summary>
    public async Task ApplyInitialValuesAsync(
        Guid groupId,
        IReadOnlyList<Guid> newDocumentIds,
        IReadOnlyDictionary<string, string?>? pendingValues = null,
        CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var group = await db.Groups.FirstAsync(g => g.Id == groupId, cancellationToken).ConfigureAwait(false);
        if (group.ProfileId is null)
        {
            return;
        }

        var schema = await profileService.GetSchemaAsync(group.ProfileId.Value, group.SchemaVersion, cancellationToken).ConfigureAwait(false);
        if (schema.Fields.Count == 0)
        {
            return;
        }

        var newDocs = await db.Documents
            .Where(d => newDocumentIds.Contains(d.Id))
            .OrderBy(d => d.Sequence)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var previous = await db.Documents
            .Where(d => d.GroupId == groupId && !newDocumentIds.Contains(d.Id))
            .OrderByDescending(d => d.Sequence)
            .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        var stickySource = previous is null
            ? []
            : JsonSerializer.Deserialize<Dictionary<string, string?>>(previous.CustomFieldsJson) ?? [];

        foreach (var document in newDocs)
        {
            var values = new Dictionary<string, string?>();
            foreach (var field in schema.Fields)
            {
                string? value = null;
                if (pendingValues is not null && pendingValues.TryGetValue(field.Name, out var pending) && !string.IsNullOrEmpty(pending))
                {
                    value = pending;
                }
                else if (field.Sticky && stickySource.TryGetValue(field.Name, out var sticky) && !string.IsNullOrEmpty(sticky))
                {
                    value = sticky;
                }
                else if (!string.IsNullOrEmpty(field.DefaultValue))
                {
                    value = TokenExpander.Expand(field.DefaultValue, group.Name, document.Sequence);
                }

                if (value is not null)
                {
                    values[field.Name] = value;
                }
            }

            document.CustomFieldsJson = JsonSerializer.Serialize(values, JsonOptions);
            stickySource = values; // sticky chains through consecutive new documents
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    // ---- validation ----

    public async Task<GroupValidation> ValidateAsync(Guid groupId, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var group = await db.Groups.FirstAsync(g => g.Id == groupId, cancellationToken).ConfigureAwait(false);
        var documents = await LoadDocumentsAsync(db, groupId, cancellationToken).ConfigureAwait(false);
        if (group.ProfileId is null)
        {
            return new GroupValidation([.. documents.Select(d =>
                new DocumentValidation(d.Doc.Id, d.ImageName, []))]);
        }

        var schema = await profileService.GetSchemaAsync(group.ProfileId.Value, group.SchemaVersion, cancellationToken).ConfigureAwait(false);
        var results = new List<DocumentValidation>();
        foreach (var (doc, imageName) in documents)
        {
            var values = JsonSerializer.Deserialize<Dictionary<string, string?>>(doc.CustomFieldsJson) ?? [];
            var errors = new List<string>();
            foreach (var field in schema.Fields)
            {
                var choices = ParseChoices(field.ListChoicesJson);
                var error = FieldValidator.Validate(
                    new IndexFieldDef(field.Name, (IndexFieldType)field.Type, field.Required),
                    values.GetValueOrDefault(field.Name),
                    choices);
                if (error is not null)
                {
                    errors.Add(error);
                }
            }

            results.Add(new DocumentValidation(doc.Id, imageName, errors));
        }

        return new GroupValidation(results);
    }

    // ---- export & commit ----

    public async Task<IndexExportData> BuildExportDataAsync(Guid groupId, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var group = await db.Groups.Include(g => g.Profile).FirstAsync(g => g.Id == groupId, cancellationToken).ConfigureAwait(false);
        var documents = await LoadDocumentsAsync(db, groupId, cancellationToken, includeBlanks: true).ConfigureAwait(false);

        IReadOnlyList<IndexFieldDef> fields = [];
        var formats = new List<IndexFormat> { IndexFormat.Csv };
        var delimiter = ',';
        var profileName = "(none)";
        if (group.Profile is { } profile)
        {
            profileName = profile.Name;
            var schema = await profileService.GetSchemaAsync(profile.Id, group.SchemaVersion, cancellationToken).ConfigureAwait(false);
            fields = [.. schema.Fields.Select(f => new IndexFieldDef(f.Name, (IndexFieldType)f.Type, f.Required))];
            formats.Clear();
            if (profile.ExportCsv)
            {
                formats.Add(IndexFormat.Csv);
            }

            if (profile.ExportXlsx)
            {
                formats.Add(IndexFormat.Xlsx);
            }

            if (profile.ExportXml)
            {
                formats.Add(IndexFormat.Xml);
            }

            if (profile.ExportJson)
            {
                formats.Add(IndexFormat.Json);
            }

            delimiter = profile.CsvDelimiter.Length > 0 ? profile.CsvDelimiter[0] : ',';
        }

        var rows = new List<IndexRow>();
        foreach (var (doc, imageName) in documents)
        {
            var page = doc.Pages.OrderBy(p => p.Sequence).First();
            rows.Add(new IndexRow(
                imageName,
                page.OcrStatus switch
                {
                    OcrStatus.Yes => "Yes",
                    OcrStatus.Failed => "Failed",
                    OcrStatus.Pending => "Pending",
                    _ => "No",
                },
                page.OcrMeanConfidence,
                page.AiDescription,
                page.AiStatus.ToString(),
                JsonSerializer.Deserialize<Dictionary<string, string?>>(doc.CustomFieldsJson) ?? [],
                doc.Sequence,
                page.Id,
                page.Checksum,
                page.IsBlank,
                page.OriginalChecksum));
        }

        return new IndexExportData(
            group.Name, group.DirectoryPath, profileName, group.SchemaVersion,
            AppVersion, DateTime.UtcNow, fields, formats, rows)
        {
            CsvDelimiter = delimiter,
        };
    }

    /// <summary>Validation with errors blocks the commit; the export never blocks the DB state change.</summary>
    public async Task<(GroupValidation Validation, ExportResult? Export)> CommitGroupAsync(
        Guid groupId, CancellationToken cancellationToken = default)
    {
        var validation = await ValidateAsync(groupId, cancellationToken).ConfigureAwait(false);
        if (validation.HasErrors)
        {
            return (validation, null);
        }

        await using (var db = await dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false))
        {
            var group = await db.Groups.FirstAsync(g => g.Id == groupId, cancellationToken).ConfigureAwait(false);
            group.State = GroupState.Committed;
            group.UpdatedUtc = DateTime.UtcNow;
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        var data = await BuildExportDataAsync(groupId, cancellationToken).ConfigureAwait(false);
        var export = await exporter.ExportAsync(data, cancellationToken).ConfigureAwait(false);
        if (commitHooks is not null)
        {
            await commitHooks.RunAsync(data, cancellationToken).ConfigureAwait(false);
        }

        return (validation, export);
    }

    /// <summary>Regenerates every enabled index format + manifest from current DB state.</summary>
    public async Task<ExportResult> ReexportAsync(Guid groupId, CancellationToken cancellationToken = default)
    {
        var data = await BuildExportDataAsync(groupId, cancellationToken).ConfigureAwait(false);
        return await exporter.ExportAsync(data, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Background flows (OCR/AI) refresh index files only once a group is committed.</summary>
    public async Task<ExportResult?> ReexportIfCommittedAsync(Guid groupId, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var state = await db.Groups.Where(g => g.Id == groupId).Select(g => g.State)
            .FirstAsync(cancellationToken).ConfigureAwait(false);
        return state == GroupState.Committed
            ? await ReexportAsync(groupId, cancellationToken).ConfigureAwait(false)
            : null;
    }

    // ---- missed page ----

    /// <summary>
    /// Inserts a page file at <paramref name="position"/> (1-based document sequence; 0/large = append).
    /// Order lives in the DB — file names never encode it (PLAN §5.2).
    /// </summary>
    public async Task<Document> InsertMissedPageAsync(
        Guid groupId, string sourceFile, int position, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var group = await db.Groups.FirstAsync(g => g.Id == groupId, cancellationToken).ConfigureAwait(false);
        var checksum = await GroupService.ComputeSha256Async(sourceFile, cancellationToken).ConfigureAwait(false);

        var maxSequence = await db.Documents.Where(d => d.GroupId == groupId)
            .MaxAsync(d => (int?)d.Sequence, cancellationToken).ConfigureAwait(false) ?? 0;
        var target = position < 1 || position > maxSequence ? maxSequence + 1 : position;

        // Shift documents at/after the insertion point.
        var toShift = await db.Documents
            .Where(d => d.GroupId == groupId && d.Sequence >= target)
            .OrderByDescending(d => d.Sequence)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        foreach (var doc in toShift)
        {
            doc.Sequence++;
        }

        // File keeps a fresh non-colliding name; naming does not encode order.
        var extension = Path.GetExtension(sourceFile);
        var fileName = $"scan_{(maxSequence + 1):00000}{extension}";
        var targetPath = Path.Combine(group.DirectoryPath, fileName);
        for (var suffix = 1; File.Exists(targetPath); suffix++)
        {
            fileName = $"scan_{(maxSequence + 1):00000}_{suffix}{extension}";
            targetPath = Path.Combine(group.DirectoryPath, fileName);
        }

        File.Copy(sourceFile, targetPath);

        var document = new Document
        {
            Id = Guid.NewGuid(),
            GroupId = groupId,
            Sequence = target,
            CreatedUtc = DateTime.UtcNow,
        };
        db.Documents.Add(document);
        db.Pages.Add(new Page
        {
            Id = Guid.NewGuid(),
            DocumentId = document.Id,
            FileName = fileName,
            Checksum = checksum,
            Sequence = 1,
            CreatedUtc = DateTime.UtcNow,
        });
        group.UpdatedUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return document;
    }

    // ---- helpers ----

    public static IReadOnlyList<string>? ParseChoices(string? listChoicesJson) =>
        listChoicesJson is null ? null : JsonSerializer.Deserialize<List<string>>(listChoicesJson);

    private static async Task<List<(Document Doc, string ImageName)>> LoadDocumentsAsync(
        FgScannerDbContext db, Guid groupId, CancellationToken cancellationToken, bool includeBlanks = false)
    {
        var documents = await db.Documents
            .Include(d => d.Pages)
            .Where(d => d.GroupId == groupId)
            .OrderBy(d => d.Sequence)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        // Flag-policy blanks stay in the group and the grid but never reach validation or the
        // human-facing index files (PLAN prompt 10: excluded from OCR/AI/index). Since Phase 16
        // the export DOES load them — index.json carries every sheet, flagged, so a copied folder
        // is complete evidence; the CSV/XLSX/XML writers filter them back out.
        return [.. documents
            .Where(d => d.Pages.Count > 0)
            .Where(d => includeBlanks || !d.Pages.OrderBy(p => p.Sequence).First().IsBlank)
            .Select(d => (d, d.Pages.OrderBy(p => p.Sequence).First().FileName))];
    }
}
