using FgScanner.Data;
using Xunit;

namespace FgScanner.Data.Tests;

public sealed class AiQueueServiceTests : IDisposable
{
    private readonly TestDb _db = new();
    private readonly GroupService _groups;
    private readonly AiQueueService _queue;
    private readonly string _groupsRoot;

    public AiQueueServiceTests()
    {
        _groups = new GroupService(_db.Factory);
        _queue = new AiQueueService(_db.Factory);
        _groupsRoot = Path.Combine(_db.Root, "groups");
        Directory.CreateDirectory(_groupsRoot);
    }

    public void Dispose() => _db.Dispose();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<(Group Group, List<Page> Pages)> CreateGroupWithPagesAsync(int count)
    {
        var group = await _groups.CreateGroupAsync(_groupsRoot, "AiQ", null, Ct);
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
    public async Task Enqueue_marks_pages_pending_and_counts_billable()
    {
        var (group, _) = await CreateGroupWithPagesAsync(3);

        Assert.Equal(3, await _queue.CountBillablePagesAsync(group.Id, cancellationToken: Ct));
        Assert.Equal(3, await _queue.EnqueueGroupAsync(group.Id, cancellationToken: Ct));
        Assert.All(await _groups.GetPagesAsync(group.Id, Ct),
            p => Assert.Equal(AiStatus.Pending, p.AiStatus));
    }

    [Fact]
    public async Task Complete_writes_description_and_status_for_the_index()
    {
        var (group, _) = await CreateGroupWithPagesAsync(1);
        await _queue.EnqueueGroupAsync(group.Id, cancellationToken: Ct);

        var claimed = await _queue.ClaimNextAsync(Ct);
        Assert.NotNull(claimed);
        await _queue.CompleteAsync(claimed.JobId, "A 1987 letter from Acme Corp.", Ct);

        var page = Assert.Single(await _groups.GetPagesAsync(group.Id, Ct));
        Assert.Equal(AiStatus.Done, page.AiStatus);
        Assert.Equal("A 1987 letter from Acme Corp.", page.AiDescription);
    }

    [Fact]
    public async Task Skip_records_the_blank_sentinel_without_failing()
    {
        var (group, _) = await CreateGroupWithPagesAsync(1);
        await _queue.EnqueueGroupAsync(group.Id, cancellationToken: Ct);
        var claimed = await _queue.ClaimNextAsync(Ct);

        await _queue.SkipAsync(claimed!.JobId, "BLANK PAGE", Ct);

        var page = Assert.Single(await _groups.GetPagesAsync(group.Id, Ct));
        Assert.Equal(AiStatus.Skipped, page.AiStatus);
        Assert.Equal("BLANK PAGE", page.AiDescription);
        Assert.Equal(0, await _queue.PendingCountAsync(Ct));
    }

    [Fact]
    public async Task Permanent_failure_fails_immediately_without_retries()
    {
        var (group, _) = await CreateGroupWithPagesAsync(1);
        await _queue.EnqueueGroupAsync(group.Id, cancellationToken: Ct);
        var claimed = await _queue.ClaimNextAsync(Ct);

        await _queue.FailAsync(claimed!.JobId, "HTTP 403: bad key", retryable: false, Ct);

        Assert.Null(await _queue.ClaimNextAsync(Ct));
        var page = Assert.Single(await _groups.GetPagesAsync(group.Id, Ct));
        Assert.Equal(AiStatus.Failed, page.AiStatus);
    }

    [Fact]
    public async Task Retryable_failures_requeue_until_the_cap()
    {
        var (group, _) = await CreateGroupWithPagesAsync(1);
        await _queue.EnqueueGroupAsync(group.Id, cancellationToken: Ct);

        for (var attempt = 1; attempt <= AiQueueService.MaxAttempts; attempt++)
        {
            var claimed = await _queue.ClaimNextAsync(Ct);
            Assert.NotNull(claimed);
            Assert.Equal(attempt, claimed.Attempt);
            await _queue.FailAsync(claimed.JobId, "HTTP 429", retryable: true, Ct);
        }

        Assert.Null(await _queue.ClaimNextAsync(Ct));
        Assert.Equal(AiStatus.Failed, (await _groups.GetPagesAsync(group.Id, Ct))[0].AiStatus);
    }

    [Fact]
    public async Task Network_loss_mid_run_leaves_a_resumable_queue()
    {
        var (group, _) = await CreateGroupWithPagesAsync(3);
        await _queue.EnqueueGroupAsync(group.Id, cancellationToken: Ct);
        var first = await _queue.ClaimNextAsync(Ct);
        await _queue.CompleteAsync(first!.JobId, "done", Ct);
        await _queue.ClaimNextAsync(Ct); // in flight when the process "dies"

        var restarted = new AiQueueService(_db.Factory);
        Assert.Equal(1, await restarted.ResetInFlightAsync(Ct));

        // Two remain: the reset one and the never-claimed one; the completed one stays done.
        Assert.Equal(2, await restarted.PendingCountAsync(Ct));
        Assert.NotNull(await restarted.ClaimNextAsync(Ct));
    }

    [Fact]
    public async Task Claim_carries_ocr_text_for_the_blank_page_short_circuit()
    {
        var (group, pages) = await CreateGroupWithPagesAsync(1);
        var ocrQueue = new OcrQueueService(_db.Factory);
        await ocrQueue.EnqueueGroupAsync(group.Id, cancellationToken: Ct);
        var ocrJob = await ocrQueue.ClaimNextAsync(Ct);
        await ocrQueue.CompleteAsync(ocrJob!.JobId, "tiny", 90, Ct);

        await _queue.EnqueueGroupAsync(group.Id, cancellationToken: Ct);
        var claimed = await _queue.ClaimNextAsync(Ct);

        Assert.Equal(OcrStatus.Yes, claimed!.OcrStatus);
        Assert.Equal("tiny", claimed.OcrText);
        Assert.EndsWith("scan_00001.png", claimed.ImagePath);
    }
}
