using Microsoft.EntityFrameworkCore;

namespace FgScanner.Data;

/// <summary>
/// Durable AI-description queue (PLAN §5.6): Pending → InFlight → Done | Failed(n≤3) | Skipped.
/// Survives restarts; blank pages are skipped by the worker without an API call; page status and
/// description feed the AIDescription/AIStatus index columns.
/// </summary>
public sealed class AiQueueService(IDbContextFactory<FgScannerDbContext> dbFactory)
{
    public const int MaxAttempts = 3;

    public event Action? JobsEnqueued;

    public sealed record ClaimedJob(
        Guid JobId, Guid PageId, Guid GroupId, string ImagePath, int Attempt,
        OcrStatus OcrStatus, string? OcrText);

    public async Task<int> EnqueueGroupAsync(
        Guid groupId, bool force = false, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var pages = await db.Pages
            .Where(p => p.Document!.GroupId == groupId)
            .Where(p => force || p.AiStatus == AiStatus.Off || p.AiStatus == AiStatus.Failed)
            .Where(p => !p.IsBlank)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var alreadyQueued = (await db.Jobs
                .Where(j => j.Type == JobType.AiDescription
                    && (j.State == JobState.Pending || j.State == JobState.InFlight))
                .Select(j => j.PageId)
                .ToListAsync(cancellationToken).ConfigureAwait(false))
            .ToHashSet();

        var created = 0;
        foreach (var page in pages.Where(p => !alreadyQueued.Contains(p.Id)))
        {
            db.Jobs.Add(new QueuedJob
            {
                Id = Guid.NewGuid(),
                Type = JobType.AiDescription,
                PageId = page.Id,
                State = JobState.Pending,
                CreatedUtc = DateTime.UtcNow,
                UpdatedUtc = DateTime.UtcNow,
            });
            page.AiStatus = AiStatus.Pending;
            created++;
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        if (created > 0)
        {
            JobsEnqueued?.Invoke();
        }

        return created;
    }

    /// <summary>Crash recovery: InFlight jobs from a dead process go back to Pending.</summary>
    public async Task<int> ResetInFlightAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var stranded = await db.Jobs
            .Where(j => j.Type == JobType.AiDescription && j.State == JobState.InFlight)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        foreach (var job in stranded)
        {
            job.State = JobState.Pending;
            job.UpdatedUtc = DateTime.UtcNow;
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return stranded.Count;
    }

    public async Task<ClaimedJob?> ClaimNextAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var job = await db.Jobs
            .Where(j => j.Type == JobType.AiDescription && j.State == JobState.Pending)
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
            Path.Combine(page.Document.Group!.DirectoryPath, page.FileName),
            job.Attempts, page.OcrStatus, page.OcrText);
    }

    public async Task CompleteAsync(
        Guid jobId, string description, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var job = await db.Jobs.FirstAsync(j => j.Id == jobId, cancellationToken).ConfigureAwait(false);
        var page = await db.Pages.FirstAsync(p => p.Id == job.PageId, cancellationToken).ConfigureAwait(false);
        job.State = JobState.Done;
        job.UpdatedUtc = DateTime.UtcNow;
        page.AiStatus = AiStatus.Done;
        page.AiDescription = description;
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Blank pages skip the API entirely (PLAN §5.6) but still get the sentinel text.</summary>
    public async Task SkipAsync(Guid jobId, string sentinel, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var job = await db.Jobs.FirstAsync(j => j.Id == jobId, cancellationToken).ConfigureAwait(false);
        var page = await db.Pages.FirstAsync(p => p.Id == job.PageId, cancellationToken).ConfigureAwait(false);
        job.State = JobState.Skipped;
        job.UpdatedUtc = DateTime.UtcNow;
        page.AiStatus = AiStatus.Skipped;
        page.AiDescription = sentinel;
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Retryable failures requeue below the attempt cap; permanent ones (400/403/safety) and
    /// capped ones mark the page Failed.
    /// </summary>
    public async Task FailAsync(
        Guid jobId, string error, bool retryable, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var job = await db.Jobs.FirstAsync(j => j.Id == jobId, cancellationToken).ConfigureAwait(false);
        var page = await db.Pages.FirstAsync(p => p.Id == job.PageId, cancellationToken).ConfigureAwait(false);
        job.LastError = error;
        job.UpdatedUtc = DateTime.UtcNow;
        if (!retryable || job.Attempts >= MaxAttempts)
        {
            job.State = JobState.Failed;
            page.AiStatus = AiStatus.Failed;
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
                j => j.Type == JobType.AiDescription
                    && (j.State == JobState.Pending || j.State == JobState.InFlight),
                cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Pages a fresh run would send to the API (for the pre-run cost estimate).</summary>
    public async Task<int> CountBillablePagesAsync(
        Guid groupId, bool force = false, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        return await db.Pages
            .Where(p => p.Document!.GroupId == groupId)
            .Where(p => force || p.AiStatus == AiStatus.Off || p.AiStatus == AiStatus.Failed)
            .Where(p => !p.IsBlank)
            .CountAsync(cancellationToken).ConfigureAwait(false);
    }
}
