using System.Globalization;
using System.IO;
using System.Windows.Data;
using System.Windows.Media.Imaging;

namespace FgScanner.App.Views;

/// <summary>Loads a small decoded thumbnail and releases the file handle (pages get re-written/deleted later).</summary>
public sealed class ThumbnailConverter : IValueConverter
{
    public static ThumbnailConverter Instance { get; } = new();

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
            bitmap.DecodePixelWidth = 180;
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
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
