using FgScanner.Core.Duplicates;
using Microsoft.EntityFrameworkCore;

namespace FgScanner.Data;

/// <summary>How two pages were judged to be the same.</summary>
public enum DuplicateKind
{
    /// <summary>Byte-identical content. Certain.</summary>
    Exact,

    /// <summary>Their OCR text overlaps above the threshold. Strong.</summary>
    Text,

    /// <summary>Their images look alike above the threshold. Weakest — a hint, not a verdict.</summary>
    Image,
}

/// <summary>One suspected duplicate pair, ordered so Left is the earlier page in the group.</summary>
public sealed record DuplicateCandidate(
    Guid LeftPageId,
    string LeftFileName,
    Guid LeftDocumentId,
    Guid RightPageId,
    string RightFileName,
    Guid RightDocumentId,
    DuplicateKind Kind,
    double Score);

/// <summary>
/// Finds suspected duplicate pages within one group.
///
/// Three signals, strongest first, and only the strongest that applies to a pair is reported — a
/// page pair should appear once, not three times:
///   1. identical SHA-256, which is certain;
///   2. OCR text overlap, where both pages have enough words to judge;
///   3. image similarity, for pages without usable OCR text.
///
/// Image hashing lives in FgScanner.Scanning because it needs System.Drawing and a Windows target,
/// so it arrives here as a delegate rather than dragging a platform dependency into the data layer.
/// </summary>
public sealed class DuplicateFinder(IDbContextFactory<FgScannerDbContext> dbFactory)
{
    /// <summary>OCR overlap at or above this counts as the same document.</summary>
    public const double DefaultTextThreshold = 0.90;

    public async Task<IReadOnlyList<DuplicateCandidate>> FindAsync(
        Guid groupId,
        Func<string, string>? computeImageHash = null,
        double imageThreshold = 0.93,
        double textThreshold = DefaultTextThreshold,
        CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var group = await db.Groups.FirstAsync(g => g.Id == groupId, cancellationToken).ConfigureAwait(false);
        var pages = await db.Pages
            .Include(p => p.Document)
            .Where(p => p.Document!.GroupId == groupId)
            .OrderBy(p => p.Document!.Sequence).ThenBy(p => p.Sequence)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        if (computeImageHash is not null)
        {
            await FillMissingHashesAsync(db, group, pages, computeImageHash, cancellationToken)
                .ConfigureAwait(false);
        }

        // Tokenize once per page rather than once per comparison: the pairwise loop is O(n²) and
        // re-tokenizing inside it would make a large group crawl.
        var tokens = pages.ToDictionary(p => p.Id, p => TextSimilarity.Tokenize(p.OcrText));

        var candidates = new List<DuplicateCandidate>();
        for (var i = 0; i < pages.Count; i++)
        {
            for (var j = i + 1; j < pages.Count; j++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (Judge(pages[i], pages[j], tokens, imageThreshold, textThreshold) is { } candidate)
                {
                    candidates.Add(candidate);
                }
            }
        }

        return [.. candidates.OrderByDescending(c => c.Kind == DuplicateKind.Exact).ThenByDescending(c => c.Score)];
    }

    private static DuplicateCandidate? Judge(
        Page left, Page right, Dictionary<Guid, IReadOnlySet<string>> tokens,
        double imageThreshold, double textThreshold)
    {
        if (string.Equals(left.Checksum, right.Checksum, StringComparison.OrdinalIgnoreCase))
        {
            return Make(left, right, DuplicateKind.Exact, 1.0);
        }

        if (tokens[left.Id].Count >= TextSimilarity.MinimumTokens
            && tokens[right.Id].Count >= TextSimilarity.MinimumTokens)
        {
            // Both pages have real text, so text is the trustworthy signal — the image comparison is
            // not consulted at all, which keeps one pair from being reported twice for two reasons.
            var overlap = Overlap(tokens[left.Id], tokens[right.Id]);
            return overlap >= textThreshold ? Make(left, right, DuplicateKind.Text, overlap) : null;
        }

        var similarity = FgScanner.Core.Duplicates.ImageHashComparer.Compare(left.ImageHash, right.ImageHash);
        return similarity >= imageThreshold
            ? Make(left, right, DuplicateKind.Image, similarity.Value)
            : null;
    }

    private static double Overlap(IReadOnlySet<string> left, IReadOnlySet<string> right)
    {
        var intersection = left.Count(right.Contains);
        var union = left.Count + right.Count - intersection;
        return union == 0 ? 0 : (double)intersection / union;
    }

    private static DuplicateCandidate Make(Page left, Page right, DuplicateKind kind, double score) =>
        new(left.Id, left.FileName, left.DocumentId, right.Id, right.FileName, right.DocumentId, kind, score);

    private static async Task FillMissingHashesAsync(
        FgScannerDbContext db, Group group, List<Page> pages,
        Func<string, string> computeImageHash, CancellationToken cancellationToken)
    {
        var changed = false;
        foreach (var page in pages.Where(p => string.IsNullOrEmpty(p.ImageHash)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = Path.Combine(group.DirectoryPath, page.FileName);
            if (!File.Exists(path))
            {
                continue;
            }

            try
            {
                page.ImageHash = computeImageHash(path);
                changed = true;
            }
            catch (Exception)
            {
                // An unreadable or corrupt image must not abort the whole scan; it simply cannot
                // take part in image comparison, and its hash stays null meaning "cannot say".
            }
        }

        if (changed)
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}
