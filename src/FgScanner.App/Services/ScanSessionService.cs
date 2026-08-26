using System.IO;
using FgScanner.Scanning;
using FgScanner.Scanning.Recovery;
using Serilog;

namespace FgScanner.App.Services;

/// <summary>
/// Owns the app's active crash-safe scan session. Pages recovered from a previous crash are
/// copied into the fresh session folder so exactly one folder owns the live pages.
/// </summary>
public sealed class ScanSessionService : IDisposable
{
    private readonly RecoveryManager _recoveryManager;

    public ScanSessionService()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "FGScanner", "recovery"))
    {
    }

    private readonly string _recoveryRoot;

    public ScanSessionService(string recoveryRoot)
    {
        _recoveryRoot = recoveryRoot;
        _recoveryManager = new RecoveryManager(recoveryRoot);
        Session = RecoverySession.Create(recoveryRoot);
    }

    public RecoverySession Session { get; private set; }

    /// <summary>After pages were adopted into a group (moved out), start a fresh session folder.</summary>
    public void ResetSession()
    {
        Session.DiscardAndDelete();
        Session = RecoverySession.Create(_recoveryRoot);
    }

    public IReadOnlyList<OrphanedSession> FindOrphanedSessions() => _recoveryManager.FindOrphanedSessions();

    /// <summary>Copies an orphan's pages into the active session (ordered) and deletes the orphan folder.</summary>
    public IReadOnlyList<ScannedPage> RecoverInto(OrphanedSession orphan)
    {
        var recovered = new List<ScannedPage>();
        foreach (var page in orphan.Pages.OrderBy(p => p.SequenceNumber))
        {
            var target = Session.ReserveNextPagePath(Path.GetExtension(page.FilePath));
            File.Copy(page.FilePath, target);
            var adopted = new ScannedPage(target, PageSequence.FromPath(target));
            Session.CommitPage(adopted);
            recovered.Add(adopted);
        }

        Session.Flush();
        RecoveryManager.Discard(orphan);
        Log.Information("Recovered {Count} page(s) from {Folder}", recovered.Count, orphan.FolderPath);
        return recovered;
    }

    public static void Discard(OrphanedSession orphan) => RecoveryManager.Discard(orphan);

    /// <summary>
    /// Throws the session away only when it holds nothing. A clean shutdown does not mean every
    /// page was saved — a page that failed to adopt is still sitting in that folder, and deleting
    /// it on exit destroys a scan the user never agreed to lose. Leaving the folder makes it an
    /// orphan, which the next launch offers to recover.
    /// </summary>
    public void Dispose()
    {
        if (Session.Pages.Count == 0)
        {
            Session.DiscardAndDelete();
            return;
        }

        Log.Information(
            "Leaving {Count} unsaved page(s) in {Folder} to recover on the next run",
            Session.Pages.Count, Session.FolderPath);
        Session.Dispose();
    }
}

internal static class PageSequence
{
    public static int FromPath(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        var dash = name.LastIndexOf('-');
        return dash >= 0 && int.TryParse(name[(dash + 1)..], out var n) ? n : 0;
    }
}
