namespace FgScanner.App.Services;

/// <summary>
/// Lets a backup stop the background readers from touching the station while it is copied
/// (SPEC-SCANNER-2026-011): OCR and AI write sidecars, index files and rows, and a file changed
/// mid-copy fails the backup's check. A reader enters around each job and each re-export; a hold
/// waits for the ones inside to finish and keeps new ones out until it is disposed.
/// </summary>
public sealed class WorkGate
{
    private readonly Lock _lock = new();
    private int _inside;
    private bool _held;
    private TaskCompletionSource _released = Completed();
    private TaskCompletionSource? _drained;

    public async Task<IDisposable> EnterAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            Task released;
            lock (_lock)
            {
                if (!_held)
                {
                    _inside++;
                    return new Release(Leave);
                }

                released = _released.Task;
            }

            await released.WaitAsync(cancellationToken);
        }
    }

    public async Task<IDisposable> HoldAsync(CancellationToken cancellationToken)
    {
        Task drained;
        lock (_lock)
        {
            if (_held)
            {
                throw new InvalidOperationException("A backup is already holding the readers.");
            }

            _held = true;
            _released = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            if (_inside == 0)
            {
                return new Release(Unhold);
            }

            _drained = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            drained = _drained.Task;
        }

        try
        {
            await drained.WaitAsync(cancellationToken);
        }
        catch
        {
            Unhold();
            throw;
        }

        return new Release(Unhold);
    }

    private void Leave()
    {
        lock (_lock)
        {
            _inside--;
            if (_held && _inside == 0)
            {
                _drained?.TrySetResult();
            }
        }
    }

    private void Unhold()
    {
        lock (_lock)
        {
            _held = false;
            _drained = null;
            _released.TrySetResult();
        }
    }

    private static TaskCompletionSource Completed()
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        done.SetResult();
        return done;
    }

    private sealed class Release(Action release) : IDisposable
    {
        private int _done;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _done, 1) == 0)
            {
                release();
            }
        }
    }
}
