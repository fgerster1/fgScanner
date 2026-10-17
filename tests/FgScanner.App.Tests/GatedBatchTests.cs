using FgScanner.App.Services;
using Xunit;

namespace FgScanner.App.Tests;

/// <summary>
/// The readers' batch loop (SPEC-SCANNER-2026-011 Revision D, RR-3): whatever ends a batch, its
/// gate pass is released (CR-1); a backup asking mid-batch stops further claims, and the batch's
/// closing work (a committed group's re-export) runs before the backup gets the station (CR-6).
/// </summary>
public sealed class GatedBatchTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_claim_that_throws_releases_the_pass()
    {
        var gate = new WorkGate();

        await Assert.ThrowsAsync<InvalidOperationException>(() => GatedBatch.RunAsync<string>(
            gate, _ => throw new InvalidOperationException("database is locked"), _ => Task.CompletedTask, () => Task.CompletedTask, Token));

        (await gate.HoldAsync(Token).WaitAsync(TimeSpan.FromSeconds(5), Token)).Dispose();
    }

    [Fact]
    public async Task A_hold_mid_batch_stops_claims_and_waits_for_the_closing_work()
    {
        var gate = new WorkGate();
        var claims = 0;
        var finished = false;
        Task<IDisposable>? holding = null;
        var queue = new Queue<string>(["one", "two", "three"]);

        await GatedBatch.RunAsync(
            gate,
            _ =>
            {
                claims++;
                return Task.FromResult(queue.TryDequeue(out var job) ? job : null);
            },
            _ =>
            {
                // The backup asks while the first job is in hand.
                holding ??= gate.HoldAsync(Token);
                return Task.CompletedTask;
            },
            () =>
            {
                Assert.False(holding!.IsCompleted);
                finished = true;
                return Task.CompletedTask;
            },
            Token);

        Assert.Equal(1, claims);
        Assert.True(finished);
        (await holding!.WaitAsync(TimeSpan.FromSeconds(5), Token)).Dispose();
    }
}
