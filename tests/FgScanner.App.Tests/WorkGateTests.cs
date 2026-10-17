using FgScanner.App.Services;
using Xunit;

namespace FgScanner.App.Tests;

/// <summary>
/// SPEC-SCANNER-2026-011 AC-5: while a backup copies the station, page reading neither starts a
/// page nor writes an index — a file changed mid-copy fails the backup's check. The page being
/// read when the backup starts finishes first; held work goes on afterwards.
/// </summary>
public sealed class WorkGateTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task No_work_starts_while_the_gate_is_held()
    {
        var gate = new WorkGate();
        var hold = await gate.HoldAsync(Token);

        var entering = gate.EnterAsync(Token);
        await Task.Delay(100, Token);

        Assert.False(entering.IsCompleted);
        hold.Dispose();
        (await entering.WaitAsync(TimeSpan.FromSeconds(5), Token)).Dispose();
    }

    [Fact]
    public async Task A_hold_waits_for_the_work_in_progress_to_finish()
    {
        var gate = new WorkGate();
        var working = await gate.EnterAsync(Token);

        var holding = gate.HoldAsync(Token);
        await Task.Delay(100, Token);
        Assert.False(holding.IsCompleted);

        working.Dispose();
        (await holding.WaitAsync(TimeSpan.FromSeconds(5), Token)).Dispose();
    }

    [Fact]
    public async Task Work_arriving_while_a_hold_waits_does_not_jump_ahead_of_it()
    {
        var gate = new WorkGate();
        var working = await gate.EnterAsync(Token);
        var holding = gate.HoldAsync(Token);

        var late = gate.EnterAsync(Token);
        working.Dispose();
        var hold = await holding.WaitAsync(TimeSpan.FromSeconds(5), Token);

        Assert.False(late.IsCompleted);
        hold.Dispose();
        (await late.WaitAsync(TimeSpan.FromSeconds(5), Token)).Dispose();
    }

    [Fact]
    public async Task A_cancelled_hold_lets_work_go_on()
    {
        var gate = new WorkGate();
        var working = await gate.EnterAsync(Token);
        using var cancel = new CancellationTokenSource();

        var holding = gate.HoldAsync(cancel.Token);
        await cancel.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => holding);
        working.Dispose();
        (await gate.EnterAsync(Token).WaitAsync(TimeSpan.FromSeconds(5), Token)).Dispose();
    }

    [Fact]
    public async Task Only_one_hold_at_a_time()
    {
        var gate = new WorkGate();
        using var hold = await gate.HoldAsync(Token);

        await Assert.ThrowsAsync<InvalidOperationException>(() => gate.HoldAsync(Token));
    }
}
