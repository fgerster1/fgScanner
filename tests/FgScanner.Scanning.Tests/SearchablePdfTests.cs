using System.Text;
using FgScanner.Ocr;
using FgScanner.Scanning.Export;
using FgScanner.Scanning.Import;
using Xunit;

namespace FgScanner.Scanning.Tests;

/// <summary>
/// Searchable-PDF export: real Tesseract supplies the invisible text layer through the phase-4
/// exporter (never mock the engine).
/// </summary>
public sealed class SearchablePdfTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("fgs-spdf").FullName;
    private readonly PdfExportService _service = new();
    private readonly PdfOcrSettings _ocr;

    public SearchablePdfTests()
    {
        var tessdata = Path.Combine(_dir, "tessdata");
        Directory.CreateDirectory(tessdata);
        File.Copy(
            Path.Combine(TesseractPaths.BundledTessdataDir, "eng.traineddata"),
            Path.Combine(tessdata, "eng.traineddata"));
        _ocr = new PdfOcrSettings(TesseractPaths.DefaultExePath, tessdata);
    }

    public void Dispose()
    {
        _service.Dispose();
        Directory.Delete(_dir, recursive: true);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string RawText(string path) => Encoding.Latin1.GetString(File.ReadAllBytes(path));

    [Fact]
    public async Task Ocr_export_embeds_a_text_layer_plain_export_does_not()
    {
        var image = TestImages.CreateTextPage(_dir);
        var plainPdf = Path.Combine(_dir, "plain.pdf");
        var ocrPdf = Path.Combine(_dir, "ocr.pdf");

        await _service.ExportAsync([image], plainPdf, new PdfExportOptions(), Ct);
        await _service.ExportAsync([image], ocrPdf, new PdfExportOptions { Ocr = _ocr }, Ct);

        Assert.DoesNotContain("/Font", RawText(plainPdf), StringComparison.Ordinal);
        Assert.Contains("/Font", RawText(ocrPdf), StringComparison.Ordinal);
    }

    /// <summary>
    /// NAPS2 issue #843's bug class: a page embedded at the wrong DPI misaligns the text layer.
    /// A 1700×2200 px image declared at 300 DPI must yield a 408×528 pt page box.
    /// </summary>
    [Fact]
    public async Task Page_box_matches_the_image_dpi()
    {
        var image = TestImages.CreateTextPage(_dir, "dpi.png", dpi: 300);
        var pdf = Path.Combine(_dir, "dpi.pdf");

        await _service.ExportAsync([image], pdf, new PdfExportOptions { Ocr = _ocr }, Ct);

        var raw = RawText(pdf);
        var mediaBoxAt = raw.IndexOf("/MediaBox", StringComparison.Ordinal);
        Assert.True(mediaBoxAt >= 0, "no /MediaBox found");
        var snippet = raw.Substring(mediaBoxAt, 48);
        Assert.Contains("408", snippet);
        Assert.Contains("528", snippet);
    }

    [Fact]
    public async Task Searchable_pdf_reimports_with_the_same_page_count()
    {
        var pages = new[]
        {
            TestImages.CreateTextPage(_dir, "a.png"),
            TestImages.CreateTextPage(_dir, "b.png"),
        };
        var pdf = Path.Combine(_dir, "two.pdf");
        await _service.ExportAsync(pages, pdf, new PdfExportOptions { Ocr = _ocr }, Ct);

        using var import = new FileImportService();
        var storage = new FakePageStorage(Path.Combine(_dir, "reimport"));
        var count = 0;
        await foreach (var _ in import.ImportAsync(pdf, storage, cancellationToken: Ct))
        {
            count++;
        }

        Assert.Equal(2, count);
    }
}
