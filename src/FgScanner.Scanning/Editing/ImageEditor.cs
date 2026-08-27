using FgScanner.Core.Imaging;
using FgScanner.Core.Index;
using NAPS2.Images;
using NAPS2.Images.Gdi;
using NAPS2.Images.Transforms;

namespace FgScanner.Scanning.Editing;

/// <summary>
/// Applies NAPS2.Sdk transforms to page images on disk. Every write is atomic (temp + replace with
/// lock-retry) so a crash or an open viewer never corrupts a page.
/// This is the single seam every pixel-modifying write passes through — manual edits, auto-orient
/// (via ImageEditorPageRotator), split and combine — so Feature.PreserveOriginals is enforced here:
/// when <paramref name="preserveOriginals"/> reports true, the current bytes of an existing target
/// are copied to originals\&lt;name&gt; before its first overwrite. First write wins; that copy IS
/// the capture and is never replaced by a later edit.
/// </summary>
public sealed class ImageEditor(
    ImageContext? imageContext = null,
    Func<CancellationToken, Task<bool>>? preserveOriginals = null)
{
    private readonly ImageContext _imageContext = imageContext ?? new GdiImageContext();
    private readonly AtomicFileWriter _writer = new();

    public async Task ApplyAsync(string imagePath, PageEdit edit, CancellationToken cancellationToken = default)
    {
        using var image = _imageContext.Load(imagePath);
        var transform = ToTransform(image, edit);
        using var edited = _imageContext.PerformTransform(image, transform);
        await SaveAtomicAsync(edited, imagePath, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Splits a page in half: the first half replaces the original file, the second half lands in
    /// <paramref name="secondHalfPath"/> for adoption as a new page.
    /// </summary>
    public async Task SplitAsync(
        string imagePath, bool vertical, string secondHalfPath, CancellationToken cancellationToken = default)
    {
        using var image = _imageContext.Load(imagePath);
        var (width, height) = (image.Width, image.Height);
        var (firstCrop, secondCrop) = vertical
            ? (new CropTransform(0, width - (width / 2), 0, 0), new CropTransform(width / 2, 0, 0, 0))
            : (new CropTransform(0, 0, 0, height - (height / 2)), new CropTransform(0, 0, height / 2, 0));

        using (var second = _imageContext.PerformTransform(image.Clone(), secondCrop))
        {
            await SaveAtomicAsync(second, secondHalfPath, cancellationToken).ConfigureAwait(false);
        }

        using var first = _imageContext.PerformTransform(image, firstCrop);
        await SaveAtomicAsync(first, imagePath, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Combines two pages into one image, written over <paramref name="firstPath"/>.</summary>
    public async Task CombineAsync(
        string firstPath, string secondPath, bool vertical, CancellationToken cancellationToken = default)
    {
        using var first = _imageContext.Load(firstPath);
        using var second = _imageContext.Load(secondPath);
        using var combined = MoreImageTransforms.Combine(
            first, second, vertical ? CombineOrientation.Vertical : CombineOrientation.Horizontal);
        await SaveAtomicAsync(combined, firstPath, cancellationToken).ConfigureAwait(false);
    }

    public (int Width, int Height) GetDimensions(string imagePath)
    {
        using var image = _imageContext.Load(imagePath);
        return (image.Width, image.Height);
    }

    private static Transform ToTransform(IMemoryImage image, PageEdit edit) => edit switch
    {
        PageEdit.Rotate r => new RotationTransform(r.Degrees),
        PageEdit.Deskew => new RotationTransform(-SkewEstimator.EstimateSkewDegrees(image)),
        PageEdit.Crop c => new CropTransform(c.Left, c.Right, c.Top, c.Bottom),
        PageEdit.Brightness b => new BrightnessTransform(b.Value),
        PageEdit.Contrast c => new TrueContrastTransform(c.Value),
        PageEdit.Hue h => new HueTransform(h.Value),
        PageEdit.Saturation s => new SaturationTransform(s.Value),
        PageEdit.Sharpen s => new SharpenTransform(s.Value),
        PageEdit.BlackWhite bw => new BlackWhiteTransform(bw.Threshold),
        _ => throw new ArgumentOutOfRangeException(nameof(edit)),
    };

    private async Task SaveAtomicAsync(IMemoryImage image, string targetPath, CancellationToken cancellationToken)
    {
        await ArchiveOriginalAsync(targetPath, cancellationToken).ConfigureAwait(false);
        var format = ImageContext.GetFileFormatFromExtension(targetPath);
        var (outcome, message) = await _writer.WriteAsync(
            targetPath,
            stream =>
            {
                image.Save(stream, format);
                return Task.CompletedTask;
            },
            cancellationToken).ConfigureAwait(false);
        if (outcome != ExportOutcome.Success)
        {
            throw new IOException(message ?? $"Could not write {targetPath}.");
        }
    }

    private async Task ArchiveOriginalAsync(string targetPath, CancellationToken cancellationToken)
    {
        // A target that does not exist yet (the second half of a split) is a new file, not an
        // edit of a capture — there is nothing to preserve.
        if (preserveOriginals is null || !File.Exists(targetPath))
        {
            return;
        }

        var archivePath = OriginalArchive.PathFor(targetPath);
        if (File.Exists(archivePath) || !await preserveOriginals(cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(archivePath)!);
        File.Copy(targetPath, archivePath);
    }
}
