using FgScanner.Scanning.Export;
using FgScanner.Scanning.Import;
using Xunit;

namespace FgScanner.Scanning.Tests;

public sealed class FileImportServiceTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("fgs-import").FullName;
    private readonly FileImportService _service = new();

    public void Dispose()
    {
        _service.Dispose();
        Directory.Delete(_dir, recursive: true);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<List<ScannedPage>> ImportAsync(string source, string? password = null)
    {
        var storage = new FakePageStorage(Path.Combine(_dir, "adopted-" + Guid.NewGuid().ToString("N")));
        var pages = new List<ScannedPage>();
        await foreach (var page in _service.ImportAsync(source, storage, password, Ct))
        {
            pages.Add(page);
        }

        Assert.Equal(pages.Count, storage.Committed.Count);
        return pages;
    }

    [Fact]
    public async Task Single_image_imports_as_one_page()
    {
        var source = TestImages.CreateLinedPage(_dir, "single.png");

        var pages = await ImportAsync(source);

        var page = Assert.Single(pages);
        Assert.True(File.Exists(page.FilePath));
        Assert.Equal((600, 800), TestImages.GetSize(page.FilePath));
    }

    [Fact]
    public async Task Multipage_tiff_imports_every_frame()
    {
        var p1 = TestImages.CreateLinedPage(_dir, "t1.png");
        var p2 = TestImages.CreateLinedPage(_dir, "t2.png", width: 500, height: 700);
        var exporter = new ImageExportService(TestImages.Context);
        var tiff = (await exporter.ExportAsync(
            [p1, p2], _dir, "multi", new ImageExportOptions { Format = ImageExportFormat.Tiff }, Ct))[0];

        var pages = await ImportAsync(tiff);

        Assert.Equal(2, pages.Count);
    }

    [Fact]
    public async Task Pdf_imports_through_the_scan_adoption_path()
    {
        var p1 = TestImages.CreateLinedPage(_dir, "d1.png");
        using var pdfService = new PdfExportService();
        var pdf = Path.Combine(_dir, "doc.pdf");
        await pdfService.ExportAsync([p1], pdf, new PdfExportOptions(), Ct);

        var pages = await ImportAsync(pdf);

        var page = Assert.Single(pages);
        var (width, height) = TestImages.GetSize(page.FilePath);
        Assert.True(width > 100 && height > 100);
        Assert.EndsWith(".png", page.FilePath);
    }

    [Fact]
    public async Task Password_pdf_needs_its_password()
    {
        var p1 = TestImages.CreateLinedPage(_dir, "s1.png");
        using var pdfService = new PdfExportService();
        var pdf = Path.Combine(_dir, "secret.pdf");
        await pdfService.ExportAsync([p1], pdf, new PdfExportOptions
        {
            Security = new PdfSecurity { OwnerPassword = "o", UserPassword = "u" },
        }, Ct);

        Assert.Single(await ImportAsync(pdf, "u"));
        await Assert.ThrowsAnyAsync<Exception>(() => ImportAsync(pdf, null));
    }
}
