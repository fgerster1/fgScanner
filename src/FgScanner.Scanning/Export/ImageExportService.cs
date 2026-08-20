using System.Globalization;
using FgScanner.Core.Index;
using NAPS2.Images;
using NAPS2.Images.Gdi;

namespace FgScanner.Scanning.Export;

public enum ImageExportFormat
{
    Jpeg,
    Png,
    Tiff,
    Bmp,
}

public enum TiffCompression
{
    Auto,
    Lzw,
    Ccitt4,
    None,
}

public sealed record ImageExportOptions
{
    public ImageExportFormat Format { get; init; } = ImageExportFormat.Png;

    /// <summary>JPEG quality 0-100.</summary>
    public int JpegQuality { get; init; } = 90;

    public TiffCompression TiffCompression { get; init; } = TiffCompression.Auto;

    /// <summary>TIFF default is one multi-page file (NAPS2 parity); false = one file per page.</summary>
    public bool TiffMultiPage { get; init; } = true;
}

/// <summary>JPEG/PNG/TIFF/BMP export; multi-page TIFF via the SDK's TIFF writer. Writes are atomic.</summary>
public sealed class ImageExportService(ImageContext? imageContext = null)
{
    private readonly ImageContext _imageContext = imageContext ?? new GdiImageContext();
    private readonly AtomicFileWriter _writer = new();

    /// <summary>
    /// Exports pages to <paramref name="outputDirectory"/> named from <paramref name="baseName"/>;
    /// returns the files written. Multi-page TIFF yields a single file.
    /// </summary>
    public async Task<IReadOnlyList<string>> ExportAsync(
        IReadOnlyList<string> imagePaths, string outputDirectory, string baseName, ImageExportOptions options,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(outputDirectory);
        if (options.Format == ImageExportFormat.Tiff && options.TiffMultiPage)
        {
            var target = Path.Combine(outputDirectory, baseName + ".tiff");
            await WriteTiffAsync(imagePaths, target, options, cancellationToken).ConfigureAwait(false);
            return [target];
        }

        var extension = options.Format switch
        {
            ImageExportFormat.Jpeg => ".jpg",
            ImageExportFormat.Tiff => ".tiff",
            ImageExportFormat.Bmp => ".bmp",
            _ => ".png",
        };
        var written = new List<string>();
        for (var i = 0; i < imagePaths.Count; i++)
        {
            var suffix = imagePaths.Count == 1 ? "" : "_" + (i + 1).ToString("000", CultureInfo.InvariantCulture);
            var target = Path.Combine(outputDirectory, baseName + suffix + extension);
            if (options.Format == ImageExportFormat.Tiff)
            {
                await WriteTiffAsync([imagePaths[i]], target, options, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await WriteSingleAsync(imagePaths[i], target, options, cancellationToken).ConfigureAwait(false);
            }

            written.Add(target);
        }

        return written;
    }

    private async Task WriteSingleAsync(
        string sourcePath, string targetPath, ImageExportOptions options, CancellationToken cancellationToken)
    {
        using var image = _imageContext.Load(sourcePath);
        var format = options.Format switch
        {
            ImageExportFormat.Jpeg => ImageFileFormat.Jpeg,
            ImageExportFormat.Bmp => ImageFileFormat.Bmp,
            _ => ImageFileFormat.Png,
        };
        var saveOptions = new ImageSaveOptions { Quality = options.JpegQuality };
        await ThrowUnlessWritten(
            _writer.WriteAsync(
                targetPath,
                stream =>
                {
                    image.Save(stream, format, saveOptions);
                    return Task.CompletedTask;
                },
                cancellationToken),
            targetPath).ConfigureAwait(false);
    }

    private async Task WriteTiffAsync(
        IReadOnlyList<string> sourcePaths, string targetPath, ImageExportOptions options,
        CancellationToken cancellationToken)
    {
        var images = new List<IMemoryImage>();
        try
        {
            foreach (var path in sourcePaths)
            {
                images.Add(_imageContext.Load(path));
            }

            var compression = options.TiffCompression switch
            {
                TiffCompression.Lzw => TiffCompressionType.Lzw,
                TiffCompression.Ccitt4 => TiffCompressionType.Ccitt4,
                TiffCompression.None => TiffCompressionType.None,
                _ => TiffCompressionType.Auto,
            };
            await ThrowUnlessWritten(
                _writer.WriteAsync(
                    targetPath,
                    stream =>
                    {
                        _imageContext.TiffWriter.SaveTiff(images, stream, compression);
                        return Task.CompletedTask;
                    },
                    cancellationToken),
                targetPath).ConfigureAwait(false);
        }
        finally
        {
            foreach (var image in images)
            {
                image.Dispose();
            }
        }
    }

    private static async Task ThrowUnlessWritten(Task<(ExportOutcome Outcome, string? Message)> write, string path)
    {
        var (outcome, message) = await write.ConfigureAwait(false);
        if (outcome != ExportOutcome.Success)
        {
            throw new IOException(message ?? $"Could not write {path}.");
        }
    }
}
