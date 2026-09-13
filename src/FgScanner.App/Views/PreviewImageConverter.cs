using System.Globalization;
using System.IO;
using System.Windows.Data;
using System.Windows.Media.Imaging;

namespace FgScanner.App.Views;

/// <summary>
/// Loads a page for the zoomable preview panel. Separate from <see cref="ThumbnailConverter"/>
/// rather than raising its decode size: that one also feeds the scan session's virtualized strip
/// of 140px thumbnails, where decoding every page at this width would cost memory for nothing.
///
/// 1200px is the compromise — sharp to roughly 3x in the side panel, at about a tenth of the
/// memory of a full 2480x3507 decode. The pop-out viewer loads the real thing instead.
///
/// The copy only ever shrinks, and keeps the page's paper size (see <see cref="AtPaperSize"/>), so
/// the preview's zoom percentage means the same as the viewer's.
/// </summary>
public sealed class PreviewImageConverter : IValueConverter
{
    public static PreviewImageConverter Instance { get; } = new();

    private const int DecodeWidth = 1200;

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string path || !File.Exists(path))
        {
            return null;
        }

        try
        {
            var (fileWidth, fileHeight, dpiX, dpiY) = ReadSize(path);
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = new Uri(path);

            // Only ever shrink. Decoding a narrow scan at 1200 would enlarge it, and Fit would then
            // draw invented pixels at a scale the file never had.
            if (fileWidth > DecodeWidth)
            {
                bitmap.DecodePixelWidth = DecodeWidth;
            }

            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            // Edits rewrite the same path; without this WPF serves the stale cached bitmap.
            bitmap.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap.PixelWidth == fileWidth ? bitmap : AtPaperSize(bitmap, fileWidth, fileHeight, dpiX, dpiY);
        }
        catch (IOException)
        {
            return null;
        }
        catch (NotSupportedException)
        {
            // A partially written or corrupt image must not take the whole grid down with it.
            return null;
        }
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();

    /// <summary>The file's own pixel size and DPI, read from its header without decoding the image.</summary>
    private static (int PixelWidth, int PixelHeight, double DpiX, double DpiY) ReadSize(string path)
    {
        using var stream = File.OpenRead(path);
        var frame = BitmapDecoder.Create(
            stream,
            BitmapCreateOptions.DelayCreation | BitmapCreateOptions.IgnoreColorProfile,
            BitmapCacheOption.None).Frames[0];
        return (frame.PixelWidth, frame.PixelHeight, frame.DpiX, frame.DpiY);
    }

    /// <summary>
    /// DecodePixelWidth keeps the file's DPI, so a 2550px page at 300 DPI decoded to 1200px lays out
    /// at 384 units instead of its 816 — the preview then reads about twice the viewer's zoom for
    /// the same page. Re-stamping the DPI by the decode ratio restores the paper size. The pixel copy
    /// is transient; only the re-stamped bitmap is kept, so steady memory is unchanged.
    ///
    /// Each axis gets its own ratio: the decoder rounds the shrunk height to whole rows, so reusing
    /// the width's ratio leaves the page a fraction of a unit short.
    /// </summary>
    private static BitmapSource AtPaperSize(BitmapSource decoded, int fileWidth, int fileHeight, double dpiX, double dpiY)
    {
        var stride = ((decoded.PixelWidth * decoded.Format.BitsPerPixel) + 7) / 8;
        var pixels = new byte[stride * decoded.PixelHeight];
        decoded.CopyPixels(pixels, stride, 0);
        var restamped = BitmapSource.Create(
            decoded.PixelWidth, decoded.PixelHeight,
            dpiX * decoded.PixelWidth / fileWidth, dpiY * decoded.PixelHeight / fileHeight,
            decoded.Format, decoded.Palette, pixels, stride);
        restamped.Freeze();
        return restamped;
    }
}
