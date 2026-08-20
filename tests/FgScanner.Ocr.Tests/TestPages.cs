using System.Drawing;
using System.Drawing.Imaging;

namespace FgScanner.Ocr.Tests;

/// <summary>Renders deterministic text pages for real-Tesseract tests (never mock the engine).</summary>
internal static class TestPages
{
    /// <summary>A simple page: large title, two body paragraphs. 300-DPI-like sizing.</summary>
    public static string CreateSimplePage(string directory, string name = "page.png")
    {
        using var bitmap = new Bitmap(1700, 2200);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(Color.White);
            graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
            using var titleFont = new Font("Arial", 40, FontStyle.Regular, GraphicsUnit.Pixel);
            using var bodyFont = new Font("Arial", 26, FontStyle.Regular, GraphicsUnit.Pixel);
            graphics.DrawString("Quarterly Report", titleFont, Brushes.Black, 150, 150);
            graphics.DrawString(
                "The quick brown fox jumps over the lazy dog.", bodyFont, Brushes.Black, 150, 300);
            graphics.DrawString(
                "Revenue increased by twelve percent this year.", bodyFont, Brushes.Black, 150, 350);
        }

        var path = Path.Combine(directory, name);
        bitmap.Save(path, ImageFormat.Png);
        return path;
    }

    /// <summary>The writable tessdata dir for tests, seeded with the bundled eng.traineddata.</summary>
    public static string PrepareTessdata(string directory)
    {
        var tessdata = Path.Combine(directory, "tessdata");
        Directory.CreateDirectory(tessdata);
        var bundled = Path.Combine(TesseractPaths.BundledTessdataDir, "eng.traineddata");
        File.Copy(bundled, Path.Combine(tessdata, "eng.traineddata"), overwrite: true);
        return tessdata;
    }
}
