using FgScanner.Core.Imaging;
using FgScanner.Scanning.Editing;
using Xunit;

namespace FgScanner.Scanning.Tests;

/// <summary>
/// Feature.PreserveOriginals: the untouched capture must survive the first pixel edit, whatever
/// path the edit takes (manual transform, auto-orient rotator, split, combine), and a later edit
/// must never replace it — first write wins, that copy IS the original.
/// </summary>
public sealed class PreserveOriginalsTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("fgs-orig").FullName;
    private readonly ImageEditor _editor = new(TestImages.Context, preserveOriginals: _ => Task.FromResult(true));

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task First_edit_archives_the_byte_identical_capture()
    {
        var path = TestImages.CreateLinedPage(_dir);
        var captureBytes = await File.ReadAllBytesAsync(path, Ct);

        await _editor.ApplyAsync(path, new PageEdit.Rotate(90), Ct);

        var archive = OriginalArchive.PathFor(path);
        Assert.True(File.Exists(archive));
        Assert.Equal(captureBytes, await File.ReadAllBytesAsync(archive, Ct));
        Assert.NotEqual(captureBytes, await File.ReadAllBytesAsync(path, Ct));
    }

    [Fact]
    public async Task Second_edit_does_not_overwrite_the_archive()
    {
        var path = TestImages.CreateLinedPage(_dir);
        var captureBytes = await File.ReadAllBytesAsync(path, Ct);

        await _editor.ApplyAsync(path, new PageEdit.Rotate(90), Ct);
        await _editor.ApplyAsync(path, new PageEdit.Rotate(90), Ct);

        Assert.Equal(captureBytes, await File.ReadAllBytesAsync(OriginalArchive.PathFor(path), Ct));
    }

    [Fact]
    public async Task Flag_off_archives_nothing()
    {
        var editorOff = new ImageEditor(TestImages.Context, preserveOriginals: _ => Task.FromResult(false));
        var path = TestImages.CreateLinedPage(_dir);

        await editorOff.ApplyAsync(path, new PageEdit.Rotate(90), Ct);

        Assert.False(File.Exists(OriginalArchive.PathFor(path)));
        Assert.False(Directory.Exists(Path.Combine(_dir, OriginalArchive.FolderName)));
    }

    [Fact]
    public async Task No_delegate_means_current_behavior()
    {
        var plain = new ImageEditor(TestImages.Context);
        var path = TestImages.CreateLinedPage(_dir);

        await plain.ApplyAsync(path, new PageEdit.Rotate(90), Ct);

        Assert.False(File.Exists(OriginalArchive.PathFor(path)));
    }

    [Fact]
    public async Task Auto_orient_rotator_path_archives_too()
    {
        var path = TestImages.CreateLinedPage(_dir);
        var captureBytes = await File.ReadAllBytesAsync(path, Ct);

        await new ImageEditorPageRotator(_editor).RotateAsync(path, 180, Ct);

        Assert.Equal(captureBytes, await File.ReadAllBytesAsync(OriginalArchive.PathFor(path), Ct));
    }

    [Fact]
    public async Task Split_archives_the_uncut_page_but_not_the_new_second_half()
    {
        var path = TestImages.CreateLinedPage(_dir);
        var captureBytes = await File.ReadAllBytesAsync(path, Ct);
        var secondHalf = Path.Combine(_dir, "second.png");

        await _editor.SplitAsync(path, vertical: true, secondHalf, Ct);

        Assert.Equal(captureBytes, await File.ReadAllBytesAsync(OriginalArchive.PathFor(path), Ct));
        // The second half is a brand-new file, not an edit of a capture.
        Assert.False(File.Exists(OriginalArchive.PathFor(secondHalf)));
    }

    [Fact]
    public async Task Combine_archives_the_overwritten_first_page()
    {
        var first = TestImages.CreateLinedPage(_dir, "first.png");
        var second = TestImages.CreateLinedPage(_dir, "second.png");
        var captureBytes = await File.ReadAllBytesAsync(first, Ct);

        await _editor.CombineAsync(first, second, vertical: true, Ct);

        Assert.Equal(captureBytes, await File.ReadAllBytesAsync(OriginalArchive.PathFor(first), Ct));
        Assert.False(File.Exists(OriginalArchive.PathFor(second)));
    }
}
