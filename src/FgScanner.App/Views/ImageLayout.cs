using System.Windows.Media.Imaging;

namespace FgScanner.App.Views;

/// <summary>
/// The space a page image takes when drawn with Stretch="None": its DPI-scaled size, not its pixel
/// count. Zoom maths fed PixelWidth showed a 300-DPI scan at a third of the room Fit had for it,
/// and every test used pixel numbers, so nothing noticed. MaxScale is image pixels per layout unit —
/// zooming past it only magnifies pixels.
/// </summary>
public readonly record struct ImageLayout(double Width, double Height, double MaxScale)
{
    public static ImageLayout Of(BitmapSource image) =>
        new(image.Width, image.Height, image.PixelWidth / image.Width);
}
