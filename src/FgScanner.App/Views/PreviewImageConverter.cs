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
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = new Uri(path);
            bitmap.DecodePixelWidth = DecodeWidth;
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            // Edits rewrite the same path; without this WPF serves the stale cached bitmap.
            bitmap.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
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
}
