using System.Globalization;
using FgScanner.Core;
using Microsoft.EntityFrameworkCore;

namespace FgScanner.Data;

/// <summary>What a retro-process / reconcile run found and did (PLAN §5.7).</summary>
public sealed record RetroReport(
    Guid GroupId,
    int AdoptedImages,
    int AdoptedPdfPages,
    IReadOnlyList<string> DuplicateFiles,
    IReadOnlyList<(string OldName, string NewName)> RematchedByChecksum,
    IReadOnlyList<string> RowsWithoutFiles,
    IReadOnlyList<string> ForeignIndexFiles)
{
    public bool ChangedAnything =>
        AdoptedImages + AdoptedPdfPages + RematchedByChecksum.Count > 0;
}

/// <summary>
/// Retro-processing (PLAN §5.7): registers an existing directory's images and PDFs as a group
/// through the same adoption path as scanning, keeping the user's file names. Idempotent — a
/// second run over an unchanged folder changes nothing. Reconcile re-matches renamed files by
/// checksum and reports rows whose files vanished.
/// </summary>
public sealed class RetroProcessService(
    IDbContextFactory<FgScannerDbContext> dbFactory,
    GroupService groupService,
    TrashService trashService,
    IPdfRenderer? pdfRenderer = null)
{
    private static readonly string[] ImageExtensions = [".jpg", ".jpeg", ".png", ".tif", ".tiff", ".bmp"];
    private static readonly string[] IndexFileNames =
        ["index.csv", "index.xlsx", "index.xml", "index.json"];

    public async Task<RetroReport> ProcessFolderAsync(
        string directory, (Guid ProfileId, int SchemaVersion)? profile = null,
        CancellationToken cancellationToken = default)
    {
        var group = await groupService.AdoptDirectoryAsync(directory, profile, cancellationToken)
            .ConfigureAwait(false);
        var reconcile = await ReconcileAsync(group.Id, cancellationToken).ConfigureAwait(false);

        var known = await KnownPagesAsync(group.Id, cancellationToken).ConfigureAwait(false);
        var knownChecksums = known.Select(p => p.Checksum).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var knownNames = known.Select(p => p.FileName).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var adoptedImages = 0;
        var duplicates = new List<string>();
        foreach (var file in EnumerateCandidateImages(group.DirectoryPath)
                     .Where(f => !knownNames.Contains(Path.GetFileName(f))))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var checksum = await GroupService.ComputeSha256Async(file, cancellationToken).ConfigureAwait(false);
            if (!knownChecksums.Add(checksum))
            {
                // Same content already registered under another name — report, never re-row.
                duplicates.Add(Path.GetFileName(file));
                continue;
            }

            await RegisterInPlaceAsync(group.Id, Path.GetFileName(file), checksum, cancellationToken)
                .ConfigureAwait(false);
            adoptedImages++;
        }

        var adoptedPdfPages = await AdoptPdfsAsync(group, knownChecksums, duplicates, cancellationToken)
            .ConfigureAwait(false);

        return new RetroReport(
            group.Id, adoptedImages, adoptedPdfPages, duplicates,
            reconcile.RematchedByChecksum, reconcile.RowsWithoutFiles,
            DetectForeignIndexFiles(group.DirectoryPath));
    }

    private async Task<List<(string FileName, string Checksum)>> KnownPagesAsync(
        Guid groupId, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        return [.. (await db.Pages
                .Where(p => p.Document!.GroupId == groupId)
                .Select(p => new { p.FileName, p.Checksum })
                .ToListAsync(cancellationToken).ConfigureAwait(false))
            .Select(p => (p.FileName, p.Checksum))];
    }

    /// <summary>
    /// Rows vs files: renamed files are re-matched by checksum (row keeps its field values);
    /// rows whose content is gone are reported (removal is a separate, explicit step).
    /// </summary>
    public async Task<RetroReport> ReconcileAsync(Guid groupId, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var group = await db.Groups.FirstAsync(g => g.Id == groupId, cancellationToken).ConfigureAwait(false);
        var pages = await db.Pages
            .Where(p => p.Document!.GroupId == groupId)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        var missingRows = pages
            .Where(p => !File.Exists(Path.Combine(group.DirectoryPath, p.FileName)))
            .ToList();
        var knownNames = pages.Select(p => p.FileName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var unmatchedFiles = EnumerateCandidateImages(group.DirectoryPath)
            .Where(f => !knownNames.Contains(Path.GetFileName(f)))
            .ToList();

        var rematched = new List<(string OldName, string NewName)>();
        foreach (var file in unmatchedFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var checksum = await GroupService.ComputeSha256Async(file, cancellationToken).ConfigureAwait(false);
            var match = missingRows.FirstOrDefault(
                p => p.Checksum.Equals(checksum, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
            {
                rematched.Add((match.FileName, Path.GetFileName(file)));
                match.FileName = Path.GetFileName(file);
                missingRows.Remove(match);
            }
        }

        if (rematched.Count > 0)
        {
            group.UpdatedUtc = DateTime.UtcNow;
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        return new RetroReport(
            groupId, 0, 0, [],
            rematched, [.. missingRows.Select(p => p.FileName)],
            DetectForeignIndexFiles(group.DirectoryPath));
    }

    /// <summary>Moves rows whose files are gone (and unmatchable) to the Trash, restorable.</summary>
    public async Task<int> RemoveRowsWithoutFilesAsync(
        Guid groupId, CancellationToken cancellationToken = default)
    {
        var report = await ReconcileAsync(groupId, cancellationToken).ConfigureAwait(false);
        var removed = 0;
        foreach (var fileName in report.RowsWithoutFiles)
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
            var page = await db.Pages
                .FirstOrDefaultAsync(
                    p => p.Document!.GroupId == groupId && p.FileName == fileName, cancellationToken)
                .ConfigureAwait(false);
            if (page is not null)
            {
                await trashService.DeleteDocumentAsync(page.DocumentId, cancellationToken).ConfigureAwait(false);
                removed++;
            }
        }

        return removed;
    }

    private async Task<int> AdoptPdfsAsync(
        Group group, HashSet<string> knownChecksums, List<string> duplicates,
        CancellationToken cancellationToken)
    {
        if (pdfRenderer is null)
        {
            return 0;
        }

        var adopted = 0;
        foreach (var pdf in Directory.EnumerateFiles(group.DirectoryPath, "*.pdf")
                     .OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var workDir = Directory.CreateTempSubdirectory("fgscanner-retro-pdf").FullName;
            try
            {
                var rendered = await pdfRenderer.RenderPagesAsync(pdf, workDir, cancellationToken)
                    .ConfigureAwait(false);
                var pdfBase = Path.GetFileNameWithoutExtension(pdf);
                var pageNumber = 0;
                foreach (var page in rendered)
                {
                    pageNumber++;
                    var checksum = await GroupService.ComputeSha256Async(page, cancellationToken)
                        .ConfigureAwait(false);
                    if (!knownChecksums.Add(checksum))
                    {
                        duplicates.Add($"{Path.GetFileName(pdf)} page {pageNumber}");
                        continue;
                    }

                    var targetName = UniqueName(
                        group.DirectoryPath,
                        $"{pdfBase}_page_{pageNumber.ToString("000", CultureInfo.InvariantCulture)}.png");
                    File.Move(page, Path.Combine(group.DirectoryPath, targetName));
                    await RegisterInPlaceAsync(group.Id, targetName, checksum, cancellationToken)
                        .ConfigureAwait(false);
                    adopted++;
                }
            }
            finally
            {
                try
                {
                    Directory.Delete(workDir, recursive: true);
                }
                catch (IOException)
                {
                }
            }
        }

        return adopted;
    }

    /// <summary>Registers a file already inside the group directory, keeping its name (PLAN §5.7).</summary>
    private async Task RegisterInPlaceAsync(
        Guid groupId, string fileName, string checksum, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var nextSequence = (await db.Documents
            .Where(d => d.GroupId == groupId)
            .Select(d => (int?)d.Sequence)
            .MaxAsync(cancellationToken).ConfigureAwait(false) ?? 0) + 1;
        var document = new Document
        {
            Id = Guid.NewGuid(),
            GroupId = groupId,
            Sequence = nextSequence,
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
        var group = await db.Groups.FirstAsync(g => g.Id == groupId, cancellationToken).ConfigureAwait(false);
        group.UpdatedUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Images in the folder, ordered by name for deterministic sequences. Sidecars and
    /// our own output files never count as pages.</summary>
    private static IEnumerable<string> EnumerateCandidateImages(string directory) =>
        Directory.EnumerateFiles(directory)
            .Where(f => ImageExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase);

    /// <summary>Index files we did not write (no manifest.json beside them) get a warning, never
    /// a silent overwrite (PLAN §B16).</summary>
    private static IReadOnlyList<string> DetectForeignIndexFiles(string directory)
    {
        if (File.Exists(Path.Combine(directory, "manifest.json")))
        {
            return [];
        }

        return [.. IndexFileNames
            .Where(name => File.Exists(Path.Combine(directory, name)))];
    }

    private static string UniqueName(string directory, string fileName)
    {
        var stem = Path.GetFileNameWithoutExtension(fileName);
        var extension = Path.GetExtension(fileName);
        var candidate = fileName;
        for (var suffix = 2; File.Exists(Path.Combine(directory, candidate)); suffix++)
        {
            candidate = $"{stem}_{suffix.ToString(CultureInfo.InvariantCulture)}{extension}";
        }

        return candidate;
    }
}
