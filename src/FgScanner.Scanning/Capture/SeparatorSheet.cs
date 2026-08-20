using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.Versioning;
using ZXing;
using ZXing.Windows.Compatibility;

namespace FgScanner.Scanning.Capture;

/// <summary>
/// Renders a printable Patch-T separator sheet: a Letter-size page carrying the Code 39 "PATCHT"
/// barcode twice (top and bottom), so it decodes whichever way the sheet is fed. Compatible with
/// NAPS2/Paperless-ngx separator sheets, which use the same encoding.
/// </summary>
[SupportedOSPlatform("windows")]
public static class SeparatorSheet
{
    // 150 DPI Letter: crisp enough for any barcode reader, small enough to email.
    public const int Dpi = 150;
    private const int Width = (int)(8.5 * Dpi);
    private const int Height = 11 * Dpi;

    public static void CreatePng(string outputPath)
    {
        var writer = new BarcodeWriter
        {
            Format = BarcodeFormat.CODE_39,
            Options = new ZXing.Common.EncodingOptions
            {
                Width = (int)(Width * 0.6),
                Height = Dpi,
                Margin = 0,
                PureBarcode = true,
            },
        };
        using var barcode = writer.Write(PatchTDetector.PatchTValue);
        using var page = new Bitmap(Width, Height);
        page.SetResolution(Dpi, Dpi);
        using (var graphics = Graphics.FromImage(page))
        {
            graphics.Clear(Color.White);
            // Pixel-exact rects: the plain DrawImage(x, y) overload rescales by the DPI mismatch
            // (96-DPI barcode onto a 150-DPI page) and the blurred bars no longer decode.
            graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
            var x = (Width - barcode.Width) / 2;
            graphics.DrawImage(
                barcode, new Rectangle(x, (int)(Height * 0.12), barcode.Width, barcode.Height));
            graphics.DrawImage(
                barcode, new Rectangle(x, (int)(Height * 0.72), barcode.Width, barcode.Height));
            using var font = new Font("Segoe UI", 14f);
            using var format = new StringFormat { Alignment = StringAlignment.Center };
            graphics.DrawString(
                "FG Scanner document separator (Patch T)\nPlace between documents; the sheet itself is not saved.",
                font, Brushes.Black,
                new RectangleF(0, Height * 0.45f, Width, Height * 0.15f), format);
        }

        page.Save(outputPath, ImageFormat.Png);
    }
}
