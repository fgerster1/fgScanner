using System.IO;
using FgScanner.App.Services;
using FgScanner.Scanning;
using Xunit;

namespace FgScanner.App.Tests;

/// <summary>
/// Closing the app used to delete the scan session unconditionally, on the assumption that a clean
/// shutdown means everything was saved. It does not: a page that failed to adopt is still sitting
/// in that folder, and quitting destroyed it with no warning and no way back.
///
/// Observed for real: a save failed on a locked file, and closing the app deleted the page it had
/// left behind.
/// </summary>
public sealed class SessionShutdownTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "fgscanner-tests", Guid.NewGuid().ToString("N"));

    public SessionShutdownTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private static void AddPage(ScanSessionService service)
    {
        var path = service.Session.ReserveNextPagePath("jpg");
        File.WriteAllBytes(path, [1, 2, 3]);
        service.Session.CommitPage(new ScannedPage(path, 1));
        service.Session.Flush();
    }

    [Fact]
    public void Closing_with_a_page_still_unsaved_keeps_it_for_recovery()
    {
        string folder;
        using (var service = new ScanSessionService(_root))
        {
            folder = service.Session.FolderPath;
            AddPage(service);
        }

        Assert.True(Directory.Exists(folder), "the session folder must survive so the page can be recovered");
        Assert.Single(Directory.GetFiles(folder, "*.jpg"));
    }

    [Fact]
    public void An_unsaved_page_is_offered_back_on_the_next_launch()
    {
        using (var first = new ScanSessionService(_root))
        {
            AddPage(first);
        }

        using var second = new ScanSessionService(_root);
        var orphan = Assert.Single(second.FindOrphanedSessions());
        Assert.Single(orphan.Pages);
    }

    [Fact]
    public void Closing_with_nothing_pending_cleans_up_after_itself()
    {
        // The common case must not litter recovery folders that then prompt on every launch.
        string folder;
        using (var service = new ScanSessionService(_root))
        {
            folder = service.Session.FolderPath;
        }

        Assert.False(Directory.Exists(folder));
    }
}
