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

    public ScanSessionService(string recoveryRoot)
    {
        _recoveryManager = new RecoveryManager(recoveryRoot);
        Session = RecoverySession.Create(recoveryRoot);
    }

    public RecoverySession Session { get; }

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

    public void Dispose() => Session.DiscardAndDelete();
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
