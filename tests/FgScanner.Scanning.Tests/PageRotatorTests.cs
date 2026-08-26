using System.Drawing;
using System.Drawing.Imaging;
using FgScanner.Scanning.Editing;
using Xunit;

namespace FgScanner.Scanning.Tests;

/// <summary>
/// The rotator the OCR pipeline uses to upright a misfed page. It has to preserve the image's
/// declared resolution: OcrWorker reads that value straight off the file and passes it to
/// Tesseract as --dpi, so a rotation that reset it to GDI's 96 default would corrupt the very
/// next recognition pass.
/// </summary>
public sealed class PageRotatorTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("fgs-rotate").FullName;
    private readonly ImageEditorPageRotator _rotator = new(new ImageEditor(TestImages.Context));

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private string CreatePage(string name = "page.jpg")
    {
        using var bitmap = new Bitmap(850, 1100);
        bitmap.SetResolution(300, 300);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(Color.White);
            using var font = new Font("Arial", 24, GraphicsUnit.Pixel);
            graphics.DrawString("Statement of Account", font, Brushes.Black, 60, 80);
        }

        var path = Path.Combine(_dir, name);
        bitmap.Save(path, ImageFormat.Jpeg);
        return path;
    }

    private static (int Width, int Height, float Dpi) Read(string path)
    {
        using var image = Image.FromFile(path);
        return (image.Width, image.Height, image.HorizontalResolution);
    }

    [Theory]
    [InlineData(90)]
    [InlineData(270)]
    public async Task A_quarter_turn_swaps_the_dimensions_and_keeps_the_resolution(int degrees)
    {
        var path = CreatePage();

        await _rotator.RotateAsync(path, degrees, Ct);

        var (width, height, dpi) = Read(path);
        Assert.Equal((1100, 850), (width, height));
        Assert.Equal(300, dpi);
    }

    [Fact]
    public async Task A_half_turn_keeps_the_dimensions_and_the_resolution()
    {
        var path = CreatePage();

        await _rotator.RotateAsync(path, 180, Ct);

        var (width, height, dpi) = Read(path);
        Assert.Equal((850, 1100), (width, height));
        Assert.Equal(300, dpi);
    }
}
