using System.Text.Json;

namespace FgScanner.Scanning.Recovery;

/// <summary>Finds crash-orphaned scan sessions: folders whose .lock can be taken over.</summary>
public sealed class RecoveryManager(string recoveryRootPath)
{
    public string RecoveryRootPath { get; } = recoveryRootPath;

    /// <summary>
    /// Enumerates orphaned session folders, newest first. A folder that refuses the lock belongs
    /// to a live instance and is skipped; a lockable folder with no pages is deleted as debris.
    /// </summary>
    public IReadOnlyList<OrphanedSession> FindOrphanedSessions()
    {
        if (!Directory.Exists(RecoveryRootPath))
        {
            return [];
        }

        var result = new List<OrphanedSession>();
        foreach (var folder in Directory.EnumerateDirectories(RecoveryRootPath)
                     .OrderByDescending(f => Directory.GetLastWriteTimeUtc(f)))
        {
            var lockPath = Path.Combine(folder, RecoverySession.LockFileName);
            FileStream? lockStream = null;
            try
            {
                // FileMode.OpenOrCreate: a folder can be orphaned before the lock file was created.
                lockStream = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.Write, FileShare.None);
            }
            catch (IOException)
            {
                continue; // live session in another process
            }

            var pages = ReadIndex(folder);
            if (pages.Count == 0)
            {
                lockStream.Dispose();
                TryDelete(folder);
                continue;
            }

            result.Add(new OrphanedSession(folder, pages, lockStream));
        }

        return result;
    }

    /// <summary>Adopts an orphaned folder as a live session; its pages stay exactly where they are.</summary>
    public static RecoverySession Recover(OrphanedSession orphan) =>
        RecoverySession.Adopt(orphan.FolderPath, orphan.TakeLock(), orphan.Pages);

    public static void Discard(OrphanedSession orphan)
    {
        orphan.TakeLock().Dispose();
        TryDelete(orphan.FolderPath);
    }

    private static List<ScannedPage> ReadIndex(string folder)
    {
        var indexPath = Path.Combine(folder, RecoverySession.IndexFileName);
        if (!File.Exists(indexPath))
        {
            return [];
        }

        try
        {
            var index = JsonSerializer.Deserialize<RecoveryIndex>(File.ReadAllText(indexPath));
            return index is null
                ? []
                : [.. index.Pages
                    .Select(p => new ScannedPage(Path.Combine(folder, p.FileName), p.SequenceNumber))
                    .Where(p => File.Exists(p.FilePath))
                    .OrderBy(p => p.SequenceNumber)];
        }
        catch (JsonException)
        {
            return []; // torn write at crash time; nothing trustworthy to recover
        }
    }

    private static void TryDelete(string folder)
    {
        try
        {
            Directory.Delete(folder, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}

public sealed class OrphanedSession(string folderPath, IReadOnlyList<ScannedPage> pages, FileStream lockStream)
{
    private FileStream? _lock = lockStream;

    public string FolderPath { get; } = folderPath;

    public IReadOnlyList<ScannedPage> Pages { get; } = pages;

    internal FileStream TakeLock() =>
        Interlocked.Exchange(ref _lock, null)
        ?? throw new InvalidOperationException("This orphaned session was already recovered or discarded.");
}
