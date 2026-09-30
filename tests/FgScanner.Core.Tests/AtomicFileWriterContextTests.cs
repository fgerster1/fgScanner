using FgScanner.Core.Index;
using Xunit;

namespace FgScanner.Core.Tests;

/// <summary>
/// SPEC-2026-008 review finding 1. Callers block on AtomicFileWriter from
/// the WPF UI thread (the index draft store, PackageWriter). If any await
/// inside it resumes on the captured context, the continuation is posted
/// to a dispatcher that is blocked waiting for it — a permanent hang,
/// invisible to xunit, which runs with no synchronization context. This
/// test runs the write on a thread whose context queues posts and never
/// runs them, exactly like a blocked dispatcher.
/// </summary>
public sealed class AtomicFileWriterContextTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "fgscanner-tests", Guid.NewGuid().ToString("N"));

    public AtomicFileWriterContextTests()
    {
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private sealed class BlockedDispatcherContext : SynchronizationContext
    {
        public int Posted;

        public override void Post(SendOrPostCallback d, object? state) =>
            Interlocked.Increment(ref Posted);
    }

    [Fact]
    public void A_small_write_blocked_on_from_a_UI_like_thread_completes()
    {
        var target = Path.Combine(_root, "draft.json");
        var bytes = new byte[300];
        var context = new BlockedDispatcherContext();
        ExportOutcome? outcome = null;

        var thread = new Thread(() =>
        {
            SynchronizationContext.SetSynchronizationContext(context);
            outcome = new AtomicFileWriter()
                .WriteAsync(target, s => s.WriteAsync(bytes, 0, bytes.Length))
                .GetAwaiter().GetResult().Outcome;
        })
        { IsBackground = true };
        thread.Start();

        Assert.True(thread.Join(TimeSpan.FromSeconds(10)),
            $"the write never returned ({context.Posted} continuation(s) posted to the blocked context)");
        Assert.Equal(ExportOutcome.Success, outcome);
    }
}
