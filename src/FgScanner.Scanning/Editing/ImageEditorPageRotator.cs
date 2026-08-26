using FgScanner.Core.Imaging;

namespace FgScanner.Scanning.Editing;

/// <summary>
/// Lets the OCR pipeline turn a misfed page upright using the same editor the user's own rotate
/// commands go through, so the write stays atomic and the file ends up in exactly the state a
/// manual rotation would have produced.
/// </summary>
public sealed class ImageEditorPageRotator(ImageEditor editor) : IPageRotator
{
    public Task RotateAsync(string imagePath, int clockwiseDegrees, CancellationToken cancellationToken = default) =>
        editor.ApplyAsync(imagePath, new PageEdit.Rotate(clockwiseDegrees), cancellationToken);
}
