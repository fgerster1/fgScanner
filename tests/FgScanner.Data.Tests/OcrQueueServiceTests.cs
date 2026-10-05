using FgScanner.Data;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FgScanner.Data.Tests;

public sealed class OcrQueueServiceTests : IDisposable
{
    private readonly TestDb _db = new();
    private readonly GroupService _groups;
    private readonly OcrQueueService _queue;
    private readonly string _groupsRoot;

    public OcrQueueServiceTests()
    {
        _groups = new GroupService(_db.Factory);
        _queue = new OcrQueueService(_db.Factory);
        _groupsRoot = Path.Combine(_db.Root, "groups");
        Directory.CreateDirectory(_groupsRoot);
    }

    public void Dispose() => _db.Dispose();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<(Group Group, List<Page> Pages)> CreateGroupWithPagesAsync(int count)
    {
        var group = await _groups.CreateGroupAsync(_groupsRoot, "Q", null, Ct);
        var incoming = Path.Combine(_db.Root, "in-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(incoming);
        var files = new List<string>();
        for (var i = 1; i <= count; i++)
        {
            var f = Path.Combine(incoming, $"p{i}.png");
            await File.WriteAllBytesAsync(f, [(byte)i], Ct);
            files.Add(f);
        }

        var adopted = await _groups.AdoptPagesAsync(group.Id, files, Ct);
        return (group, [.. adopted.Adopted]);
    }

    [Fact]
    public async Task Enqueue_creates_jobs_and_marks_pages_pending()
    {
        var (group, _) = await CreateGroupWithPagesAsync(3);

        var created = await _queue.EnqueueGroupAsync(group.Id, cancellationToken: Ct);

        Assert.Equal(3, created);
        Assert.Equal(3, await _queue.PendingCountAsync(Ct));
        Assert.All(await _groups.GetPagesAsync(group.Id, Ct),
            p => Assert.Equal(OcrStatus.Pending, p.OcrStatus));
    }

    [Fact]
    public async Task Enqueue_is_idempotent_while_jobs_are_open()
    {
        var (group, _) = await CreateGroupWithPagesAsync(2);

        await _queue.EnqueueGroupAsync(group.Id, cancellationToken: Ct);
        var second = await _queue.EnqueueGroupAsync(group.Id, force: true, cancellationToken: Ct);

        Assert.Equal(0, second);
        Assert.Equal(2, await _queue.PendingCountAsync(Ct));
    }

    [Fact]
    public async Task Claim_complete_updates_job_and_page_and_fts()
    {
        var (group, _) = await CreateGroupWithPagesAsync(1);
        await _queue.EnqueueGroupAsync(group.Id, cancellationToken: Ct);

        var claimed = await _queue.ClaimNextAsync(Ct);
        Assert.NotNull(claimed);
        Assert.EndsWith("scan_00001.png", claimed.ImagePath);
        await _queue.CompleteAsync(claimed.JobId, "invoice from acme corporation", 88.5, Ct);

        var page = Assert.Single(await _groups.GetPagesAsync(group.Id, Ct));
        Assert.Equal(OcrStatus.Yes, page.OcrStatus);
        Assert.Equal(88.5, page.OcrMeanConfidence);
        Assert.Equal("invoice from acme corporation", page.OcrText);
        Assert.Equal(0, await _queue.PendingCountAsync(Ct));

        // The FTS5 external-content index picks the text up via triggers (PLAN §5.1).
        await using var db = _db.Factory.CreateDbContext();
        var hits = await db.Database
            .SqlQueryRaw<int>("SELECT count(*) AS Value FROM PagesFts WHERE PagesFts MATCH 'acme'")
            .ToListAsync(Ct);
        Assert.Equal(1, hits[0]);
    }

    [Fact]
    public async Task Failure_requeues_until_the_attempt_cap_then_fails_the_page()
    {
        var (group, _) = await CreateGroupWithPagesAsync(1);
        await _queue.EnqueueGroupAsync(group.Id, cancellationToken: Ct);

        for (var attempt = 1; attempt <= OcrQueueService.MaxAttempts; attempt++)
        {
            var claimed = await _queue.ClaimNextAsync(Ct);
            Assert.NotNull(claimed);
            Assert.Equal(attempt, claimed.Attempt);
            await _queue.FailAsync(claimed.JobId, $"boom {attempt}", Ct);
        }

        Assert.Null(await _queue.ClaimNextAsync(Ct));
        var page = Assert.Single(await _groups.GetPagesAsync(group.Id, Ct));
        Assert.Equal(OcrStatus.Failed, page.OcrStatus);
    }

    [Fact]
    public async Task InFlight_jobs_from_a_crashed_run_are_requeued_on_restart()
    {
        var (group, _) = await CreateGroupWithPagesAsync(2);
        await _queue.EnqueueGroupAsync(group.Id, cancellationToken: Ct);
        await _queue.ClaimNextAsync(Ct); // claimed, then the process "dies"

        // A new service instance over the same database simulates the restart.
        var restarted = new OcrQueueService(_db.Factory);
        var reset = await restarted.ResetInFlightAsync(Ct);

        Assert.Equal(1, reset);
        Assert.Equal(2, await restarted.PendingCountAsync(Ct));
        Assert.NotNull(await restarted.ClaimNextAsync(Ct));
    }

    [Fact]
    public async Task Force_reenqueues_already_ocred_pages()
    {
        var (group, _) = await CreateGroupWithPagesAsync(1);
        await _queue.EnqueueGroupAsync(group.Id, cancellationToken: Ct);
        var claimed = await _queue.ClaimNextAsync(Ct);
        await _queue.CompleteAsync(claimed!.JobId, "text", 90, Ct);

        Assert.Equal(0, await _queue.EnqueueGroupAsync(group.Id, cancellationToken: Ct));
        Assert.Equal(1, await _queue.EnqueueGroupAsync(group.Id, force: true, cancellationToken: Ct));
    }

    /// <summary>One misread page should not cost a whole group's re-run: the chosen pages are OCRed
    /// if they never were, and re-OCRed if they were — and nothing else is touched.</summary>
    [Fact]
    public async Task Chosen_pages_are_queued_whatever_their_status_and_others_are_left_alone()
    {
        var (group, pages) = await CreateGroupWithPagesAsync(3);
        await _queue.EnqueuePagesAsync([pages[0].Id], Ct);
        var claimed = await _queue.ClaimNextAsync(Ct);
        await _queue.CompleteAsync(claimed!.JobId, "first reading", 60, Ct);

        var result = await _queue.EnqueuePagesAsync([pages[0].Id, pages[1].Id], Ct);

        Assert.Equal(new OcrPageRequest(Queued: 2, Blank: 0, AlreadyQueued: 0), result);
        var after = (await _groups.GetPagesAsync(group.Id, Ct)).ToDictionary(p => p.Id);
        Assert.Equal(OcrStatus.Pending, after[pages[0].Id].OcrStatus);
        Assert.Equal(OcrStatus.Pending, after[pages[1].Id].OcrStatus);
        Assert.Equal(OcrStatus.No, after[pages[2].Id].OcrStatus);
        Assert.Equal(2, await _queue.PendingCountAsync(Ct));
    }

    [Fact]
    public async Task A_chosen_blank_page_is_skipped_and_counted()
    {
        var (group, pages) = await CreateGroupWithPagesAsync(2);
        await using (var db = _db.Factory.CreateDbContext())
        {
            var blank = await db.Pages.SingleAsync(p => p.Id == pages[1].Id, Ct);
            blank.IsBlank = true;
            await db.SaveChangesAsync(Ct);
        }

        var result = await _queue.EnqueuePagesAsync([pages[0].Id, pages[1].Id], Ct);

        Assert.Equal(new OcrPageRequest(Queued: 1, Blank: 1, AlreadyQueued: 0), result);
        var after = (await _groups.GetPagesAsync(group.Id, Ct)).ToDictionary(p => p.Id);
        Assert.Equal(OcrStatus.No, after[pages[1].Id].OcrStatus);
    }

    [Fact]
    public async Task A_chosen_page_already_queued_gets_no_second_job()
    {
        var (_, pages) = await CreateGroupWithPagesAsync(1);
        await _queue.EnqueuePagesAsync([pages[0].Id], Ct);

        var again = await _queue.EnqueuePagesAsync([pages[0].Id], Ct);

        Assert.Equal(new OcrPageRequest(Queued: 0, Blank: 0, AlreadyQueued: 1), again);
        Assert.Equal(1, await _queue.PendingCountAsync(Ct));
    }
}
