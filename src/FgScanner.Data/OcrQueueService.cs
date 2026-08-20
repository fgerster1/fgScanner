using Microsoft.EntityFrameworkCore;

namespace FgScanner.Data;

/// <summary>
/// Durable OCR work queue over the Jobs table (PLAN §5.5, §8): enqueued pages survive restarts,
/// in-flight jobs from a crashed run are re-queued at startup, and page status feeds the OCRed
/// index column. Retries cap at three attempts before a job stays Failed.
/// </summary>
public sealed class OcrQueueService(IDbContextFactory<FgScannerDbContext> dbFactory)
{
    public const int MaxAttempts = 3;

    /// <summary>Raised after jobs are added so a worker can wake without polling.</summary>
    public event Action? JobsEnqueued;

    /// <summary>
    /// Queues OCR for the group's pages: all when <paramref name="force"/>, otherwise only pages
    /// not yet OCRed (No/Failed). Returns the number of jobs created.
    /// </summary>
    public async Task<int> EnqueueGroupAsync(
        Guid groupId, bool force = false, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var pages = await db.Pages
            .Where(p => p.Document!.GroupId == groupId)
            .Where(p => force || p.OcrStatus == OcrStatus.No || p.OcrStatus == OcrStatus.Failed)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var alreadyQueued = (await db.Jobs
                .Where(j => j.Type == JobType.Ocr && (j.State == JobState.Pending || j.State == JobState.InFlight))
                .Select(j => j.PageId)
                .ToListAsync(cancellationToken).ConfigureAwait(false))
            .ToHashSet();

        var created = 0;
        foreach (var page in pages.Where(p => !alreadyQueued.Contains(p.Id)))
        {
            db.Jobs.Add(new QueuedJob
            {
                Id = Guid.NewGuid(),
                Type = JobType.Ocr,
                PageId = page.Id,
                State = JobState.Pending,
                CreatedUtc = DateTime.UtcNow,
                UpdatedUtc = DateTime.UtcNow,
            });
            page.OcrStatus = OcrStatus.Pending;
            created++;
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        if (created > 0)
        {
            JobsEnqueued?.Invoke();
        }

        return created;
    }

    /// <summary>Crash recovery: anything left InFlight by a dead process goes back to Pending.</summary>
    public async Task<int> ResetInFlightAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var stranded = await db.Jobs
            .Where(j => j.Type == JobType.Ocr && j.State == JobState.InFlight)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        foreach (var job in stranded)
        {
            job.State = JobState.Pending;
            job.UpdatedUtc = DateTime.UtcNow;
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return stranded.Count;
    }

    /// <summary>The claimed job with everything the worker needs, or null when the queue is idle.</summary>
    public sealed record ClaimedJob(
        Guid JobId, Guid PageId, Guid GroupId, string ImagePath, int Attempt);

    public async Task<ClaimedJob?> ClaimNextAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var job = await db.Jobs
            .Where(j => j.Type == JobType.Ocr && j.State == JobState.Pending)
            .OrderBy(j => j.CreatedUtc)
            .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        if (job is null)
        {
            return null;
        }

        var page = await db.Pages
            .Include(p => p.Document!).ThenInclude(d => d.Group)
            .FirstAsync(p => p.Id == job.PageId, cancellationToken).ConfigureAwait(false);
        job.State = JobState.InFlight;
        job.Attempts++;
        job.UpdatedUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return new ClaimedJob(
            job.Id, page.Id, page.Document!.GroupId,
            Path.Combine(page.Document.Group!.DirectoryPath, page.FileName), job.Attempts);
    }

    public async Task CompleteAsync(
        Guid jobId, string plainText, double meanConfidence, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var job = await db.Jobs.FirstAsync(j => j.Id == jobId, cancellationToken).ConfigureAwait(false);
        var page = await db.Pages.FirstAsync(p => p.Id == job.PageId, cancellationToken).ConfigureAwait(false);
        job.State = JobState.Done;
        job.UpdatedUtc = DateTime.UtcNow;
        page.OcrStatus = OcrStatus.Yes;
        page.OcrText = plainText;
        page.OcrMeanConfidence = meanConfidence;
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Failure below MaxAttempts re-queues; at the cap the job and page go Failed.</summary>
    public async Task FailAsync(Guid jobId, string error, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var job = await db.Jobs.FirstAsync(j => j.Id == jobId, cancellationToken).ConfigureAwait(false);
        var page = await db.Pages.FirstAsync(p => p.Id == job.PageId, cancellationToken).ConfigureAwait(false);
        job.LastError = error;
        job.UpdatedUtc = DateTime.UtcNow;
        if (job.Attempts >= MaxAttempts)
        {
            job.State = JobState.Failed;
            page.OcrStatus = OcrStatus.Failed;
        }
        else
        {
            job.State = JobState.Pending;
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<int> PendingCountAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        return await db.Jobs
            .CountAsync(
                j => j.Type == JobType.Ocr && (j.State == JobState.Pending || j.State == JobState.InFlight),
                cancellationToken).ConfigureAwait(false);
    }
}
