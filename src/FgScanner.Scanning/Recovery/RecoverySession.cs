using System.Text.Json;

namespace FgScanner.Scanning.Recovery;

/// <summary>
/// A crash-safe scan session folder (NAPS2 pattern): pages are written straight into it,
/// a .lock file is held open for the process lifetime (its release IS the crash signal),
/// and a throttled index.json records the page list so an orphaned folder can be recovered.
/// </summary>
public sealed class RecoverySession : IPageStorage, IDisposable
{
    public const string LockFileName = ".lock";
    public const string IndexFileName = "index.json";
    private static readonly TimeSpan IndexWriteThrottle = TimeSpan.FromMilliseconds(100);

    private readonly FileStream _lock;
    private readonly Lock _sync = new();
    private readonly List<ScannedPage> _pages = [];
    private readonly TimeProvider _time;
    private long _lastIndexWriteTicks;
    private bool _indexDirty;
    private int _nextSequence = 1;
    private bool _disposed;

    private RecoverySession(string folderPath, FileStream lockStream, TimeProvider time)
    {
        FolderPath = folderPath;
        _lock = lockStream;
        _time = time;
    }

    public string FolderPath { get; }

    public IReadOnlyList<ScannedPage> Pages
    {
        get
        {
            lock (_sync)
            {
                return [.. _pages];
            }
        }
    }

    public static RecoverySession Create(string recoveryRootPath, TimeProvider? time = null)
    {
        Directory.CreateDirectory(recoveryRootPath);
        var folder = Path.Combine(recoveryRootPath, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var lockStream = new FileStream(
            Path.Combine(folder, LockFileName),
            FileMode.CreateNew, FileAccess.Write, FileShare.None);
        return new RecoverySession(folder, lockStream, time ?? TimeProvider.System);
    }

    /// <summary>Re-opens an orphaned folder (lock acquired by RecoveryManager) as a live session.</summary>
    internal static RecoverySession Adopt(
        string folderPath, FileStream lockStream, IReadOnlyList<ScannedPage> pages, TimeProvider? time = null)
    {
        var session = new RecoverySession(folderPath, lockStream, time ?? TimeProvider.System);
        session._pages.AddRange(pages);
        session._nextSequence = pages.Count == 0 ? 1 : pages.Max(p => p.SequenceNumber) + 1;
        return session;
    }

    public string ReserveNextPagePath(string extension)
    {
        lock (_sync)
        {
            var sequence = _nextSequence++;
            return Path.Combine(FolderPath, $"page-{sequence:00000}.{extension.TrimStart('.')}");
        }
    }

    public void CommitPage(ScannedPage page)
    {
        lock (_sync)
        {
            _pages.Add(page);
            _indexDirty = true;
        }

        WriteIndexIfDue(force: false);
    }

    /// <summary>
    /// Drops pages the session no longer owns — the ones adoption moved into a group. Without
    /// this a partial save leaves the index naming files that have gone, so the next run offers to
    /// recover them and every retry stops at the first one that is missing.
    /// </summary>
    public void ForgetPages(IEnumerable<string> filePaths)
    {
        var gone = filePaths.ToHashSet(StringComparer.OrdinalIgnoreCase);
        lock (_sync)
        {
            if (_pages.RemoveAll(p => gone.Contains(p.FilePath)) == 0)
            {
                return;
            }

            _indexDirty = true;
        }

        WriteIndexIfDue(force: true);
    }

    /// <summary>Flushes the index immediately (end of a scan run, or before showing UI state).</summary>
    public void Flush() => WriteIndexIfDue(force: true);

    /// <summary>Clean shutdown: release the lock and delete the folder — nothing to recover.</summary>
    public void DiscardAndDelete()
    {
        Dispose();
        try
        {
            Directory.Delete(FolderPath, recursive: true);
        }
        catch (IOException)
        {
            // Another process (AV, indexer) briefly holds a file; orphan cleanup will get it later.
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        WriteIndexIfDue(force: true);
        _lock.Dispose();
    }

    private void WriteIndexIfDue(bool force)
    {
        List<ScannedPage> snapshot;
        lock (_sync)
        {
            if (!_indexDirty)
            {
                return;
            }

            var now = _time.GetTimestamp();
            var elapsed = _time.GetElapsedTime(_lastIndexWriteTicks, now);
            if (!force && _lastIndexWriteTicks != 0 && elapsed < IndexWriteThrottle)
            {
                return;
            }

            _lastIndexWriteTicks = now;
            _indexDirty = false;
            snapshot = [.. _pages];
        }

        var index = new RecoveryIndex(
            snapshot.Select(p => new RecoveryIndexPage(Path.GetFileName(p.FilePath), p.SequenceNumber)).ToList());
        var tmp = Path.Combine(FolderPath, IndexFileName + ".tmp");
        File.WriteAllText(tmp, JsonSerializer.Serialize(index));
        File.Move(tmp, Path.Combine(FolderPath, IndexFileName), overwrite: true);
    }
}

public sealed record RecoveryIndex(List<RecoveryIndexPage> Pages);

public sealed record RecoveryIndexPage(string FileName, int SequenceNumber);
