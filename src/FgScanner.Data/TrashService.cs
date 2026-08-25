using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace FgScanner.Data;

/// <summary>
/// Trash with retention (PLAN §5.2): deleting a page moves its image + sidecars (same base name:
/// .md, .txt) into an app trash folder and archives the row data; Restore puts everything back.
/// Nothing is ever hard-deleted directly from a group.
/// </summary>
public sealed class TrashService(
    IDbContextFactory<FgScannerDbContext> dbFactory,
    string trashRoot,
    TimeProvider? time = null)
{
    /// <summary>Where trashed content lives; group deletes relocate whole folders here.</summary>
    public string TrashRoot => trashRoot;

    public const string RetentionSettingKey = "Trash.RetentionDays";
    public const int DefaultRetentionDays = 30;
    private static readonly string[] SidecarExtensions = [".md", ".txt"];
    private static readonly JsonSerializerOptions JsonOptions = new();
    private readonly TimeProvider _time = time ?? TimeProvider.System;

    private sealed record TrashPayload(Document Document, List<Page> Pages);

    public async Task<TrashItem> DeleteDocumentAsync(Guid documentId, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var document = await db.Documents
            .Include(d => d.Pages)
            .Include(d => d.Group)
            .FirstAsync(d => d.Id == documentId, cancellationToken).ConfigureAwait(false);
        var group = document.Group!;

        var itemId = Guid.NewGuid();
        var folder = Path.Combine(trashRoot, itemId.ToString("N"));
        Directory.CreateDirectory(folder);

        var movedFiles = new List<string>();
        foreach (var page in document.Pages)
        {
            MoveIfExists(Path.Combine(group.DirectoryPath, page.FileName), folder, movedFiles);
            var baseName = Path.GetFileNameWithoutExtension(page.FileName);
            foreach (var ext in SidecarExtensions)
            {
                MoveIfExists(Path.Combine(group.DirectoryPath, baseName + ext), folder, movedFiles);
            }
        }

        var item = new TrashItem
        {
            Id = itemId,
            OriginalGroupId = group.Id,
            GroupName = group.Name,
            GroupDirectoryPath = group.DirectoryPath,
            DocumentSequence = document.Sequence,
            PayloadJson = JsonSerializer.Serialize(
                new TrashPayload(Strip(document), [.. document.Pages.Select(Strip)]), JsonOptions),
            FilesJson = JsonSerializer.Serialize(movedFiles, JsonOptions),
            TrashFolderPath = folder,
            DeletedUtc = _time.GetUtcNow().UtcDateTime,
        };
        db.TrashItems.Add(item);
        db.Documents.Remove(document); // cascades to pages
        group.UpdatedUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return item;
    }

    /// <summary>A replaced sidecar (re-OCR) goes through the same trash so it stays restorable.</summary>
    public async Task ArchiveReplacedFileAsync(
        Guid groupId, string filePath, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(filePath))
        {
            return;
        }

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var group = await db.Groups.FirstAsync(g => g.Id == groupId, cancellationToken).ConfigureAwait(false);
        var itemId = Guid.NewGuid();
        var folder = Path.Combine(trashRoot, itemId.ToString("N"));
        Directory.CreateDirectory(folder);
        var moved = new List<string>();
        MoveIfExists(filePath, folder, moved);
        db.TrashItems.Add(new TrashItem
        {
            Id = itemId,
            OriginalGroupId = group.Id,
            GroupName = group.Name,
            GroupDirectoryPath = group.DirectoryPath,
            DocumentSequence = 0,
            PayloadJson = "null",
            FilesJson = JsonSerializer.Serialize(moved, JsonOptions),
            TrashFolderPath = folder,
            DeletedUtc = _time.GetUtcNow().UtcDateTime,
        });
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<TrashItem>> ListAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        return await db.TrashItems.OrderByDescending(t => t.DeletedUtc).ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Returns files and (for document items) DB rows to the origin group.</summary>
    public async Task RestoreAsync(Guid trashItemId, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var item = await db.TrashItems.FirstAsync(t => t.Id == trashItemId, cancellationToken).ConfigureAwait(false);
        Directory.CreateDirectory(item.GroupDirectoryPath);

        foreach (var fileName in JsonSerializer.Deserialize<List<string>>(item.FilesJson) ?? [])
        {
            var source = Path.Combine(item.TrashFolderPath, fileName);
            var target = Path.Combine(item.GroupDirectoryPath, fileName);
            if (File.Exists(source) && !File.Exists(target))
            {
                File.Move(source, target);
            }
        }

        var payload = JsonSerializer.Deserialize<TrashPayload>(item.PayloadJson, JsonOptions);
        if (payload is not null)
        {
            // Original sequence slot may have been reused; append at the end in that case.
            var occupied = await db.Documents.AnyAsync(
                d => d.GroupId == item.OriginalGroupId && d.Sequence == payload.Document.Sequence, cancellationToken).ConfigureAwait(false);
            if (occupied)
            {
                payload.Document.Sequence = await db.Documents
                    .Where(d => d.GroupId == item.OriginalGroupId)
                    .MaxAsync(d => (int?)d.Sequence, cancellationToken).ConfigureAwait(false) ?? 0;
                payload.Document.Sequence++;
            }

            db.Documents.Add(payload.Document);
            db.Pages.AddRange(payload.Pages);
        }

        db.TrashItems.Remove(item);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        TryDeleteFolder(item.TrashFolderPath);
    }

    public async Task DeletePermanentlyAsync(Guid trashItemId, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var item = await db.TrashItems.FirstAsync(t => t.Id == trashItemId, cancellationToken).ConfigureAwait(false);
        db.TrashItems.Remove(item);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        TryDeleteFolder(item.TrashFolderPath);
    }

    /// <summary>Background purge honoring the configurable retention (clock injectable for tests).</summary>
    public async Task<int> PurgeExpiredAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var retentionDays = DefaultRetentionDays;
        var setting = await db.Settings.FindAsync([RetentionSettingKey], cancellationToken).ConfigureAwait(false);
        if (setting is not null && int.TryParse(setting.Value, out var configured) && configured > 0)
        {
            retentionDays = configured;
        }

        var cutoff = _time.GetUtcNow().UtcDateTime.AddDays(-retentionDays);
        var expired = await db.TrashItems.Where(t => t.DeletedUtc < cutoff).ToListAsync(cancellationToken).ConfigureAwait(false);
        foreach (var item in expired)
        {
            db.TrashItems.Remove(item);
            TryDeleteFolder(item.TrashFolderPath);
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return expired.Count;
    }

    public async Task SetRetentionDaysAsync(int days, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var setting = await db.Settings.FindAsync([RetentionSettingKey], cancellationToken).ConfigureAwait(false);
        if (setting is null)
        {
            db.Settings.Add(new Setting { Key = RetentionSettingKey, Value = days.ToString(System.Globalization.CultureInfo.InvariantCulture) });
        }
        else
        {
            setting.Value = days.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private static void MoveIfExists(string sourcePath, string trashFolder, List<string> movedFiles)
    {
        if (File.Exists(sourcePath))
        {
            var name = Path.GetFileName(sourcePath);
            File.Move(sourcePath, Path.Combine(trashFolder, name));
            movedFiles.Add(name);
        }
    }

    private static void TryDeleteFolder(string folder)
    {
        try
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
        catch (IOException)
        {
        }
    }

    private static Document Strip(Document d) => new()
    {
        Id = d.Id,
        GroupId = d.GroupId,
        Sequence = d.Sequence,
        CustomFieldsJson = d.CustomFieldsJson,
        CreatedUtc = d.CreatedUtc,
    };

    private static Page Strip(Page p) => new()
    {
        Id = p.Id,
        DocumentId = p.DocumentId,
        FileName = p.FileName,
        Checksum = p.Checksum,
        Sequence = p.Sequence,
        IsBlank = p.IsBlank,
        OcrStatus = p.OcrStatus,
        AiStatus = p.AiStatus,
        OcrMeanConfidence = p.OcrMeanConfidence,
        OcrText = p.OcrText,
        AiDescription = p.AiDescription,
        CreatedUtc = p.CreatedUtc,
    };
}
