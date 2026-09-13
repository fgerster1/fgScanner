using System.Globalization;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FgScanner.App.Views;
using Xunit;

namespace FgScanner.App.Tests;

/// <summary>
/// The Groups preview decodes a smaller copy of each page. That copy must still lay out at the page's
/// paper size, or the preview's zoom percentage means something different from the viewer's; and it
/// must never be larger than the file, or a narrow scan is blown up and Fit draws it soft.
/// </summary>
public sealed class PreviewImageConverterTests : IDisposable
{
    // PNG stores DPI as whole pixels per metre, so 96 DPI reads back as 95.99 and a layout lands a
    // tenth of a unit off. The bugs these tests catch are off by hundreds (384 for 816, 1200 for 900).
    private const double LayoutTolerance = 0.5;

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "fgscanner-preview-" + Guid.NewGuid().ToString("N"));

    public PreviewImageConverterTests() => Directory.CreateDirectory(_folder);

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Fact]
    public void A_shrunk_letter_page_still_lays_out_at_paper_size()
    {
        var preview = Convert(WritePage("letter.png", pixelWidth: 2550, pixelHeight: 3300, dpi: 300));
        var layout = ImageLayout.Of(preview);

        Assert.Equal(1200, preview.PixelWidth);
        Assert.InRange(layout.Width, 816 - LayoutTolerance, 816 + LayoutTolerance);
        Assert.InRange(layout.Height, 1056 - LayoutTolerance, 1056 + LayoutTolerance);
    }

    [Fact]
    public void A_scan_narrower_than_the_preview_width_is_not_enlarged()
    {
        var preview = Convert(WritePage("screenshot.png", pixelWidth: 800, pixelHeight: 1000, dpi: 96));
        var layout = ImageLayout.Of(preview);

        Assert.Equal(800, preview.PixelWidth);
        Assert.InRange(layout.Width, 800 - LayoutTolerance, 800 + LayoutTolerance);
        Assert.Equal(1.0, layout.MaxScale, 3);
    }

    [Fact]
    public void A_narrow_300_dpi_receipt_keeps_every_pixel_and_its_paper_size()
    {
        // 3 x 4 inches at 300 DPI: small enough to load whole, and Fit may enlarge it only to 3.125x.
        var preview = Convert(WritePage("receipt.png", pixelWidth: 900, pixelHeight: 1200, dpi: 300));
        var layout = ImageLayout.Of(preview);

        Assert.Equal(900, preview.PixelWidth);
        Assert.InRange(layout.Width, 288 - LayoutTolerance, 288 + LayoutTolerance);
        Assert.Equal(3.125, layout.MaxScale, 3);
    }

    private static BitmapSource Convert(string path) =>
        Assert.IsAssignableFrom<BitmapSource>(
            PreviewImageConverter.Instance.Convert(path, typeof(ImageSource), null, CultureInfo.InvariantCulture));

    private string WritePage(string name, int pixelWidth, int pixelHeight, double dpi)
    {
        var source = BitmapSource.Create(
            pixelWidth, pixelHeight, dpi, dpi, PixelFormats.Gray8, null,
            new byte[pixelWidth * pixelHeight], pixelWidth);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source));
        var path = Path.Combine(_folder, name);
        using (var stream = File.Create(path))
        {
            encoder.Save(stream);
        }

        return path;
    }
}
