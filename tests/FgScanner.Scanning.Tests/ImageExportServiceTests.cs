using System.Drawing;
using System.Drawing.Imaging;
using FgScanner.Scanning.Export;
using Xunit;

namespace FgScanner.Scanning.Tests;

public sealed class ImageExportServiceTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("fgs-imgexp").FullName;
    private readonly string _outDir;
    private readonly ImageExportService _service = new(TestImages.Context);
    private readonly List<string> _pages;

    public ImageExportServiceTests()
    {
        _outDir = Path.Combine(_dir, "out");
        _pages =
        [
            TestImages.CreateLinedPage(_dir, "p1.png"),
            TestImages.CreateLinedPage(_dir, "p2.png", width: 500, height: 700),
        ];
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Png_round_trip_is_pixel_exact()
    {
        var written = await _service.ExportAsync([_pages[0]], _outDir, "page", new ImageExportOptions(), Ct);

        var file = Assert.Single(written);
        Assert.Equal("page.png", Path.GetFileName(file));
        Assert.Equal(0.0, TestImages.MeanPixelDifference(_pages[0], file));
    }

    [Fact]
    public async Task Jpeg_quality_trades_size_for_fidelity()
    {
        var high = await _service.ExportAsync(
            [_pages[0]], _outDir, "hq", new ImageExportOptions { Format = ImageExportFormat.Jpeg, JpegQuality = 95 }, Ct);
        var low = await _service.ExportAsync(
            [_pages[0]], _outDir, "lq", new ImageExportOptions { Format = ImageExportFormat.Jpeg, JpegQuality = 15 }, Ct);

        Assert.True(new FileInfo(high[0]).Length > new FileInfo(low[0]).Length);
        Assert.True(TestImages.MeanPixelDifference(_pages[0], high[0]) < 3.0);
        Assert.True(TestImages.MeanPixelDifference(_pages[0], low[0]) < 25.0);
    }

    [Fact]
    public async Task Multiple_pages_get_numbered_files()
    {
        var written = await _service.ExportAsync(_pages, _outDir, "doc", new ImageExportOptions(), Ct);

        Assert.Equal(["doc_001.png", "doc_002.png"], written.Select(Path.GetFileName));
    }

    [Fact]
    public async Task Tiff_default_is_one_multipage_file()
    {
        var written = await _service.ExportAsync(
            _pages, _outDir, "doc", new ImageExportOptions { Format = ImageExportFormat.Tiff }, Ct);

        var file = Assert.Single(written);
        using var image = Image.FromFile(file);
        Assert.Equal(2, image.GetFrameCount(FrameDimension.Page));
    }

    [Fact]
    public async Task Tiff_single_page_mode_writes_one_file_per_page()
    {
        var written = await _service.ExportAsync(
            _pages, _outDir, "doc",
            new ImageExportOptions { Format = ImageExportFormat.Tiff, TiffMultiPage = false }, Ct);

        Assert.Equal(2, written.Count);
        Assert.All(written, f => Assert.True(File.Exists(f)));
    }

    [Fact]
    public async Task Tiff_ccitt4_compresses_black_and_white()
    {
        var none = await _service.ExportAsync(
            [_pages[0]], _outDir, "none",
            new ImageExportOptions { Format = ImageExportFormat.Tiff, TiffCompression = TiffCompression.None }, Ct);
        var ccitt = await _service.ExportAsync(
            [_pages[0]], _outDir, "ccitt",
            new ImageExportOptions { Format = ImageExportFormat.Tiff, TiffCompression = TiffCompression.Ccitt4 }, Ct);

        Assert.True(new FileInfo(ccitt[0]).Length < new FileInfo(none[0]).Length);
    }

    [Fact]
    public async Task Bmp_export_preserves_dimensions()
    {
        var written = await _service.ExportAsync(
            [_pages[1]], _outDir, "page", new ImageExportOptions { Format = ImageExportFormat.Bmp }, Ct);

        Assert.Equal((500, 700), TestImages.GetSize(written[0]));
    }
}
