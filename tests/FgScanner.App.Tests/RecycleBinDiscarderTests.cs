using System.IO;
using FgScanner.App.Services;
using Xunit;

namespace FgScanner.App.Tests;

/// <summary>
/// The discarder deletes files named by the recovery index, which is read from disk. A corrupt or
/// hand-edited index must not be able to aim it anywhere outside the scan session (SPEC-2026-003 AC-6).
/// These do not look inside the Recycle Bin, whose contents differ per machine.
/// </summary>
public sealed class RecycleBinDiscarderTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "fgscanner-tests", Guid.NewGuid().ToString("N"));
    private readonly string _session;

    public RecycleBinDiscarderTests()
    {
        _session = Directory.CreateDirectory(Path.Combine(_root, "abc")).FullName;
    }

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

    [Fact]
    public void A_page_inside_the_session_folder_leaves_the_folder()
    {
        // Not named like a scan: it lands in this machine's real Recycle Bin, where an operator
        // restoring a deleted page by hand should not mistake it for one.
        var file = Path.Combine(_session, "fgscanner-discarder-test-1.bin");
        File.WriteAllBytes(file, [1]);

        var (discarded, reason) = DiscardThroughTheShell(_session, file);

        Assert.True(discarded, reason);
        Assert.False(File.Exists(file));
    }

    [Fact]
    public void The_session_folder_matches_whatever_its_letter_case()
    {
        var file = Path.Combine(_session, "fgscanner-discarder-test-2.bin");
        File.WriteAllBytes(file, [2]);

        var (discarded, reason) = DiscardThroughTheShell(_session.ToUpperInvariant(), file);

        Assert.True(discarded, reason);
        Assert.False(File.Exists(file));
    }

    /// <summary>
    /// A real shell call, given a deadline. Where the machine's Recycle Bin will not take the file, the
    /// shell asks before deleting it for good, and on an unattended build agent nobody answers: fail
    /// the test rather than hang the run. The waiting thread is a background one, so it cannot keep
    /// the test process alive, and it hands back any exception: one left on a raw thread kills the
    /// whole test host without naming a test.
    /// </summary>
    private static (bool Discarded, string Reason) DiscardThroughTheShell(string sessionFolder, string file)
    {
        (bool, string) outcome = (false, "");
        Exception? error = null;
        var worker = new Thread(() =>
        {
            try
            {
                var discarded = new RecycleBinDiscarder().TryDiscard(sessionFolder, file, out var reason);
                outcome = (discarded, reason);
            }
            catch (Exception ex)
            {
                error = ex;
            }
        })
        {
            IsBackground = true,
        };
        worker.Start();

        Assert.True(
            worker.Join(TimeSpan.FromSeconds(30)),
            "The shell did not return within 30 s; it is probably asking whether to delete the file permanently.");
        if (error is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw();
        }

        return outcome;
    }

    [Theory]
    [InlineData("abcd/page-00001.png")] // a sibling folder whose name starts with the session's
    [InlineData("abc/../abcd/page-00001.png")] // climbs out of the session with ..
    [InlineData("page-00001.png")] // the folder that holds every session
    public void A_file_outside_the_session_folder_is_refused_and_kept(string relative)
    {
        // Handed over as written, not normalized first, so the ".." case reaches the guard as "..".
        var file = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(file))!);
        File.WriteAllBytes(file, [1]);

        Assert.False(new RecycleBinDiscarder().TryDiscard(_session, file, out var reason));
        Assert.NotEmpty(reason);
        Assert.True(File.Exists(file));
    }

    [Fact]
    public void The_session_folder_itself_is_refused()
    {
        Assert.False(new RecycleBinDiscarder().TryDiscard(_session, _session, out _));
        Assert.True(Directory.Exists(_session));
    }

    /// <summary>The shell expands wildcards in the paths it is given, so one of these would take every page.</summary>
    [Theory]
    [InlineData("*")]
    [InlineData("page-0000?.png")]
    public void A_wildcard_is_refused_and_every_page_is_kept(string name)
    {
        var first = Path.Combine(_session, "page-00001.png");
        var second = Path.Combine(_session, "page-00002.png");
        File.WriteAllBytes(first, [1]);
        File.WriteAllBytes(second, [2]);

        Assert.False(new RecycleBinDiscarder().TryDiscard(_session, Path.Combine(_session, name), out var reason));
        Assert.NotEmpty(reason);
        Assert.True(File.Exists(first));
        Assert.True(File.Exists(second));
    }
}
