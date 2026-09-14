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
        var file = Path.Combine(_session, "page-00001.png");
        File.WriteAllBytes(file, [1]);

        Assert.True(new RecycleBinDiscarder().TryDiscard(_session, file, out var reason), reason);
        Assert.False(File.Exists(file));
    }

    [Fact]
    public void The_session_folder_matches_whatever_its_letter_case()
    {
        var file = Path.Combine(_session, "page-00002.png");
        File.WriteAllBytes(file, [2]);

        Assert.True(new RecycleBinDiscarder().TryDiscard(_session.ToUpperInvariant(), file, out var reason), reason);
        Assert.False(File.Exists(file));
    }

    [Theory]
    [InlineData("abcd/page-00001.png")] // a sibling folder whose name starts with the session's
    [InlineData("abc/../abcd/page-00001.png")] // climbs out of the session with ..
    [InlineData("page-00001.png")] // the folder that holds every session
    public void A_file_outside_the_session_folder_is_refused_and_kept(string relative)
    {
        var file = Path.GetFullPath(Path.Combine(_root, relative));
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
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
}
