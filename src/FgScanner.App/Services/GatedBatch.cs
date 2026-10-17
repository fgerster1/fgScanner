namespace FgScanner.App.Services;

/// <summary>
/// The background readers' batch loop, in one place so it can be tested without OCR or Gemini
/// (SPEC-SCANNER-2026-011 Revision D, RR-3). One gate pass covers the whole batch and is released
/// however the batch ends (CR-1); once a backup asks, no further job is claimed, and the batch's
/// closing work — re-exporting a committed group's index — still runs inside the pass, so the
/// backup never copies a stale index (CR-6).
/// </summary>
public static class GatedBatch
{
    /// <param name="claim">The next job, or null when the queue is empty.</param>
    /// <param name="start">Handles one job; may return once it is under way rather than done.</param>
    /// <param name="finish">Runs after the last claim, inside the pass.</param>
    public static async Task RunAsync<TJob>(
        WorkGate gate,
        Func<CancellationToken, Task<TJob?>> claim,
        Func<TJob, Task> start,
        Func<Task> finish,
        CancellationToken cancellationToken)
        where TJob : class
    {
        using var pass = await gate.EnterAsync(cancellationToken);
        while (!cancellationToken.IsCancellationRequested && !gate.HoldRequested
               && await claim(cancellationToken) is { } job)
        {
            await start(job);
        }

        await finish();
    }
}
