using System.Drawing;
using NAPS2.Images;
using NAPS2.Images.Gdi;

namespace FgScanner.Scanning.Tests;

/// <summary>Synthetic deterministic page images: white ground with black "text line" bars.</summary>
internal static class TestImages
{
    public static readonly GdiImageContext Context = new();

    /// <summary>A page-like image with horizontal dark stripes every 40px (text-line stand-ins).</summary>
    public static string CreateLinedPage(string directory, string name = "page.png", int width = 600, int height = 800)
    {
        using var bitmap = new Bitmap(width, height);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(Color.White);
            using var brush = new SolidBrush(Color.Black);
            for (var y = 60; y < height - 60; y += 40)
            {
                graphics.FillRectangle(brush, 40, y, width - 80, 12);
            }
        }

        var path = Path.Combine(directory, name);
        bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png);
        return path;
    }

    /// <summary>A page with real words at a declared DPI (searchable-PDF tests need true text).</summary>
    public static string CreateTextPage(string directory, string name = "text.png", float dpi = 300)
    {
        using var bitmap = new Bitmap(1700, 2200);
        bitmap.SetResolution(dpi, dpi);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(Color.White);
            graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
            using var font = new Font("Arial", 26, FontStyle.Regular, GraphicsUnit.Pixel);
            graphics.DrawString("The quick brown fox jumps over the lazy dog.", font, Brushes.Black, 150, 300);
            graphics.DrawString("Pack my box with five dozen liquor jugs.", font, Brushes.Black, 150, 350);
        }

        var path = Path.Combine(directory, name);
        bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png);
        return path;
    }

    /// <summary>A solid-color image for pixel-level assertions.</summary>
    public static string CreateSolidPage(string directory, string name, Color color, int width = 200, int height = 100)
    {
        using var bitmap = new Bitmap(width, height);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(color);
        }

        var path = Path.Combine(directory, name);
        bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png);
        return path;
    }

    public static Color GetPixel(string path, int x, int y)
    {
        using var bitmap = new Bitmap(path);
        return bitmap.GetPixel(x, y);
    }

    public static (int Width, int Height) GetSize(string path)
    {
        using var bitmap = new Bitmap(path);
        return (bitmap.Width, bitmap.Height);
    }

    /// <summary>Mean absolute per-channel difference across two same-size images (tolerance testing).</summary>
    public static double MeanPixelDifference(string pathA, string pathB)
    {
        using var a = new Bitmap(pathA);
        using var b = new Bitmap(pathB);
        if (a.Width != b.Width || a.Height != b.Height)
        {
            return double.MaxValue;
        }

        double total = 0;
        for (var y = 0; y < a.Height; y++)
        {
            for (var x = 0; x < a.Width; x++)
            {
                var pa = a.GetPixel(x, y);
                var pb = b.GetPixel(x, y);
                total += Math.Abs(pa.R - pb.R) + Math.Abs(pa.G - pb.G) + Math.Abs(pa.B - pb.B);
            }
        }

        return total / (a.Width * a.Height * 3.0);
    }
}
