using FgScanner.Scanning;
using FgScanner.Scanning.Recovery;
using Xunit;

namespace FgScanner.Scanning.Tests;

public sealed class RecoveryTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "fgscanner-tests", Guid.NewGuid().ToString("N"));

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

    private static ScannedPage AddPage(RecoverySession session, int contentByte = 1)
    {
        var path = session.ReserveNextPagePath("png");
        File.WriteAllBytes(path, [(byte)contentByte]);
        var page = new ScannedPage(path, int.Parse(Path.GetFileNameWithoutExtension(path).Split('-')[1], System.Globalization.CultureInfo.InvariantCulture));
        session.CommitPage(page);
        return page;
    }

    [Fact]
    public void Session_reserves_sequential_page_paths()
    {
        using var session = RecoverySession.Create(_root);
        Assert.EndsWith("page-00001.png", session.ReserveNextPagePath("png"));
        Assert.EndsWith("page-00002.jpg", session.ReserveNextPagePath(".jpg"));
    }

    [Fact]
    public void Live_session_is_not_reported_as_orphaned()
    {
        using var session = RecoverySession.Create(_root);
        AddPage(session);
        session.Flush();

        var orphans = new RecoveryManager(_root).FindOrphanedSessions();

        Assert.Empty(orphans);
    }

    [Fact]
    public void Crashed_session_is_recoverable_with_its_pages_in_order()
    {
        // Simulate a crash: session goes away without DiscardAndDelete, lock released by "process death".
        var session = RecoverySession.Create(_root);
        AddPage(session);
        AddPage(session);
        AddPage(session);
        session.Flush();
        session.Dispose(); // lock released, folder left behind — exactly the post-crash state

        var orphans = new RecoveryManager(_root).FindOrphanedSessions();

        var orphan = Assert.Single(orphans);
        Assert.Equal([1, 2, 3], orphan.Pages.Select(p => p.SequenceNumber));
        Assert.All(orphan.Pages, p => Assert.True(File.Exists(p.FilePath)));
    }

    [Fact]
    public void Recovered_session_continues_numbering_after_existing_pages()
    {
        var crashed = RecoverySession.Create(_root);
        AddPage(crashed);
        AddPage(crashed);
        crashed.Flush();
        crashed.Dispose();

        var orphan = Assert.Single(new RecoveryManager(_root).FindOrphanedSessions());
        using var recovered = RecoveryManager.Recover(orphan);

        Assert.Equal(2, recovered.Pages.Count);
        Assert.EndsWith("page-00003.png", recovered.ReserveNextPagePath("png"));
        Assert.Empty(new RecoveryManager(_root).FindOrphanedSessions()); // lock is held again
    }

    [Fact]
    public void Discard_removes_the_orphaned_folder()
    {
        var crashed = RecoverySession.Create(_root);
        AddPage(crashed);
        crashed.Flush();
        crashed.Dispose();

        var orphan = Assert.Single(new RecoveryManager(_root).FindOrphanedSessions());
        RecoveryManager.Discard(orphan);

        Assert.False(Directory.Exists(orphan.FolderPath));
    }

    [Fact]
    public void Clean_shutdown_leaves_nothing_to_recover()
    {
        var session = RecoverySession.Create(_root);
        AddPage(session);
        session.DiscardAndDelete();

        Assert.Empty(new RecoveryManager(_root).FindOrphanedSessions());
        Assert.False(Directory.Exists(session.FolderPath));
    }

    [Fact]
    public void Empty_crashed_folder_is_cleaned_up_not_offered()
    {
        var session = RecoverySession.Create(_root);
        session.Dispose(); // crashed before any page arrived

        Assert.Empty(new RecoveryManager(_root).FindOrphanedSessions());
        Assert.False(Directory.Exists(session.FolderPath));
    }

    [Fact]
    public void Torn_index_json_is_treated_as_unrecoverable_debris()
    {
        var session = RecoverySession.Create(_root);
        AddPage(session);
        session.Dispose();
        File.WriteAllText(Path.Combine(session.FolderPath, RecoverySession.IndexFileName), "{\"Pages\":[{\"FileNa");

        Assert.Empty(new RecoveryManager(_root).FindOrphanedSessions());
    }

    [Fact]
    public void Index_lists_only_pages_whose_files_still_exist()
    {
        var session = RecoverySession.Create(_root);
        var kept = AddPage(session);
        var deleted = AddPage(session);
        session.Flush();
        session.Dispose();
        File.Delete(deleted.FilePath);

        var orphan = Assert.Single(new RecoveryManager(_root).FindOrphanedSessions());

        var page = Assert.Single(orphan.Pages);
        Assert.Equal(kept.FilePath, page.FilePath);
    }
    [Fact]
    public void Forgetting_adopted_pages_leaves_the_index_describing_only_what_is_left()
    {
        // After a partial save the adopted files have moved out of the session folder. If the
        // index still names them, the next run offers to recover files that are not there and
        // every retry dies on the first one.
        using var session = RecoverySession.Create(_root);
        var first = AddPage(session, 1);
        var second = AddPage(session, 2);
        var third = AddPage(session, 3);
        File.Delete(first.FilePath);
        File.Delete(third.FilePath);

        session.ForgetPages([first.FilePath, third.FilePath]);
        session.Flush();

        Assert.Equal([second.FilePath], session.Pages.Select(p => p.FilePath));
        var index = System.Text.Json.JsonSerializer.Deserialize<RecoveryIndex>(
            File.ReadAllText(Path.Combine(session.FolderPath, RecoverySession.IndexFileName)))!;
        Assert.Equal([Path.GetFileName(second.FilePath)], index.Pages.Select(p => p.FileName));
    }

    [Fact]
    public void Forgetting_a_page_that_is_not_in_the_session_changes_nothing()
    {
        using var session = RecoverySession.Create(_root);
        var only = AddPage(session, 1);

        session.ForgetPages([Path.Combine(_root, "never-here.png")]);

        Assert.Single(session.Pages);
        Assert.Equal(only.FilePath, session.Pages[0].FilePath);
    }
}
