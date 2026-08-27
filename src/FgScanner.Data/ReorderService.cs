using Microsoft.EntityFrameworkCore;

namespace FgScanner.Data;

/// <summary>
/// Page order operations (PLAN §5.8: move / interleave / deinterleave / reverse). Order lives in
/// Document.Sequence; every operation renumbers 1..n so exports and the grid stay consistent.
/// </summary>
public sealed class ReorderService(IDbContextFactory<FgScannerDbContext> dbFactory)
{
    /// <summary>Moves a document to a 1-based position, shifting the others.</summary>
    public async Task MoveAsync(
        Guid groupId, Guid documentId, int newPosition, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var documents = await OrderedDocumentsAsync(db, groupId, cancellationToken).ConfigureAwait(false);
        var moving = documents.First(d => d.Id == documentId);
        documents.Remove(moving);
        documents.Insert(Math.Clamp(newPosition - 1, 0, documents.Count), moving);
        Renumber(documents);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public Task ReverseAsync(Guid groupId, CancellationToken cancellationToken = default) =>
        ReorderAsync(groupId, docs => [.. Enumerable.Reverse(docs)], cancellationToken);

    /// <summary>
    /// Interleaves the first half with the second half (manual duplex: fronts then backs
    /// become front/back/front/back).
    /// </summary>
    public Task InterleaveAsync(Guid groupId, CancellationToken cancellationToken = default) =>
        ReorderAsync(
            groupId,
            docs =>
            {
                var half = (docs.Count + 1) / 2;
                var result = new List<Document>();
                for (var i = 0; i < half; i++)
                {
                    result.Add(docs[i]);
                    if (half + i < docs.Count)
                    {
                        result.Add(docs[half + i]);
                    }
                }

                return result;
            },
            cancellationToken);

    /// <summary>Inverse of interleave: odd positions first, then even positions.</summary>
    public Task DeinterleaveAsync(Guid groupId, CancellationToken cancellationToken = default) =>
        ReorderAsync(
            groupId,
            docs =>
            {
                var result = new List<Document>();
                for (var i = 0; i < docs.Count; i += 2)
                {
                    result.Add(docs[i]);
                }

                for (var i = 1; i < docs.Count; i += 2)
                {
                    result.Add(docs[i]);
                }

                return result;
            },
            cancellationToken);

    /// <summary>Applies an explicit order (used by undo/redo to restore a captured arrangement).</summary>
    public Task SetOrderAsync(
        Guid groupId, IReadOnlyList<Guid> orderedDocumentIds, CancellationToken cancellationToken = default) =>
        ReorderAsync(
            groupId,
            docs =>
            {
                var byId = docs.ToDictionary(d => d.Id);
                var result = orderedDocumentIds.Where(byId.ContainsKey).Select(id => byId[id]).ToList();
                result.AddRange(docs.Where(d => !orderedDocumentIds.Contains(d.Id)));
                return result;
            },
            cancellationToken);

    public async Task<IReadOnlyList<Guid>> GetOrderAsync(Guid groupId, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        return await db.Documents
            .Where(d => d.GroupId == groupId)
            .OrderBy(d => d.Sequence)
            .Select(d => d.Id)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Recomputes a page's checksum after its image file was edited in place, and discards the
    /// perceptual hash. That hash describes a picture rather than a file, so an edit invalidates
    /// it; leaving it would have duplicate detection compare pages against images that no longer
    /// exist, and a stale hash is indistinguishable from a current one. It is recomputed on demand.
    /// </summary>
    public async Task RefreshChecksumAsync(Guid pageId, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var page = await db.Pages
            .Include(p => p.Document!).ThenInclude(d => d.Group)
            .FirstAsync(p => p.Id == pageId, cancellationToken).ConfigureAwait(false);
        var filePath = Path.Combine(page.Document!.Group!.DirectoryPath, page.FileName);
        page.Checksum = await GroupService.ComputeSha256Async(filePath, cancellationToken).ConfigureAwait(false);
        page.ImageHash = null;

        // The first edit with Feature.PreserveOriginals on left the untouched capture in
        // originals\; record its hash exactly once — later edits must not move that anchor.
        var archivePath = Core.Imaging.OriginalArchive.PathFor(filePath);
        if (page.OriginalChecksum is null && File.Exists(archivePath))
        {
            page.OriginalChecksum = await GroupService.ComputeSha256Async(archivePath, cancellationToken).ConfigureAwait(false);
        }

        page.Document.Group.UpdatedUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task ReorderAsync(
        Guid groupId, Func<List<Document>, List<Document>> arrange, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var documents = await OrderedDocumentsAsync(db, groupId, cancellationToken).ConfigureAwait(false);
        Renumber(arrange(documents));
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<List<Document>> OrderedDocumentsAsync(
        FgScannerDbContext db, Guid groupId, CancellationToken cancellationToken) =>
        await db.Documents
            .Where(d => d.GroupId == groupId)
            .OrderBy(d => d.Sequence)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

    private static void Renumber(List<Document> ordered)
    {
        for (var i = 0; i < ordered.Count; i++)
        {
            ordered[i].Sequence = i + 1;
        }
    }
}
