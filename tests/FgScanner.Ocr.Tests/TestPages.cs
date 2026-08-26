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

    /// <summary>
    /// A text-heavy page. Orientation detection reads the shape of many characters, so the
    /// three-line <see cref="CreateSimplePage"/> gives it too little to work with.
    /// </summary>
    public static string CreateDensePage(string directory, string name = "dense.png")
    {
        using var bitmap = RenderDense();
        var path = Path.Combine(directory, name);
        bitmap.Save(path, ImageFormat.Png);
        return path;
    }

    /// <summary>The same dense page turned clockwise, as a misfed sheet arrives from the feeder.</summary>
    public static string CreateRotatedPage(string directory, int clockwiseDegrees, string? name = null)
    {
        using var bitmap = RenderDense();
        bitmap.RotateFlip(clockwiseDegrees switch
        {
            90 => RotateFlipType.Rotate90FlipNone,
            180 => RotateFlipType.Rotate180FlipNone,
            270 => RotateFlipType.Rotate270FlipNone,
            _ => RotateFlipType.RotateNoneFlipNone,
        });
        var path = Path.Combine(directory, name ?? $"turned{clockwiseDegrees}.png");
        bitmap.Save(path, ImageFormat.Png);
        return path;
    }

    /// <summary>An empty sheet — orientation detection has nothing to measure on one.</summary>
    public static string CreateBlankPage(string directory, string name = "blank.png")
    {
        using var bitmap = new Bitmap(1700, 2200);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(Color.White);
        }

        var path = Path.Combine(directory, name);
        bitmap.Save(path, ImageFormat.Png);
        return path;
    }

    private static readonly string[] DenseLines =
    [
        "Notice of Assessment and Statement of Account",
        "This document confirms the amounts recorded against the account named below.",
        "Any person may rely on a copy of this document as though it were the original.",
        "Prepared for the fiscal period ending the thirtieth of September.",
        "The quick brown fox jumps over the lazy dog near the riverbank.",
        "Revenue increased by twelve percent compared with the previous year.",
        "Shipping and handling charges are calculated at the time of dispatch.",
        "Please retain this statement for your records and future reference.",
        "Payment remains due within thirty days of the date shown above.",
        "Interest accrues monthly on any balance outstanding after that date.",
        "Corrections must be submitted in writing to the address on the reverse.",
        "A duplicate copy may be requested at any branch office during hours.",
    ];

    private static Bitmap RenderDense()
    {
        var bitmap = new Bitmap(1700, 2200);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.Clear(Color.White);
        graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
        using var titleFont = new Font("Arial", 40, FontStyle.Regular, GraphicsUnit.Pixel);
        using var bodyFont = new Font("Arial", 28, FontStyle.Regular, GraphicsUnit.Pixel);
        graphics.DrawString("Quarterly Report", titleFont, Brushes.Black, 150, 120);
        var y = 240f;
        // Repeat the block so the sheet carries enough characters for orientation detection.
        for (var pass = 0; pass < 4; pass++)
        {
            foreach (var line in DenseLines)
            {
                graphics.DrawString(line, bodyFont, Brushes.Black, 150, y);
                y += 40f;
            }

            y += 20f;
        }

        return bitmap;
    }

    /// <summary>The writable tessdata dir for tests, seeded with the bundled traineddata files.</summary>
    public static string PrepareTessdata(string directory)
    {
        var tessdata = Path.Combine(directory, "tessdata");
        Directory.CreateDirectory(tessdata);
        foreach (var file in new[] { "eng.traineddata", "osd.traineddata" })
        {
            File.Copy(
                Path.Combine(TesseractPaths.BundledTessdataDir, file),
                Path.Combine(tessdata, file),
                overwrite: true);
        }

        return tessdata;
    }
}
