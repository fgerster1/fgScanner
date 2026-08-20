using Xunit;

namespace FgScanner.Ocr.Tests;

public sealed class TesseractRunnerTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("fgs-ocr-run").FullName;
    private readonly TesseractRunner _runner;

    public TesseractRunnerTests() =>
        _runner = new TesseractRunner(tessdataDir: TestPages.PrepareTessdata(_dir));

    public void Dispose()
    {
        _runner.Dispose();
        Directory.Delete(_dir, recursive: true);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void Bundled_tesseract_exe_exists() => Assert.True(File.Exists(TesseractPaths.DefaultExePath));

    [Fact]
    public async Task One_pass_emits_pdf_hocr_and_tsv()
    {
        var image = TestPages.CreateSimplePage(_dir);

        var result = await _runner.RecognizeAsync(image, Path.Combine(_dir, "out"), 300, "eng", Ct);

        Assert.True(result.Success, result.Error);
        Assert.True(File.Exists(result.TsvPath));
        Assert.True(File.Exists(result.HocrPath));
        Assert.True(File.Exists(result.PdfPath));
        var tsv = await File.ReadAllTextAsync(result.TsvPath!, Ct);
        Assert.Contains("quick", tsv, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("fox", tsv, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("eng; rm -rf /")]
    [InlineData("../evil")]
    [InlineData("ENG")]
    [InlineData("en")]
    [InlineData("eng+")]
    public async Task Language_whitelist_rejects_injection_shapes(string languages)
    {
        var image = TestPages.CreateSimplePage(_dir);

        await Assert.ThrowsAsync<ArgumentException>(
            () => _runner.RecognizeAsync(image, Path.Combine(_dir, "out"), 300, languages, Ct));
    }

    [Fact]
    public async Task Wellformed_but_missing_language_fails_in_tesseract_not_validation()
    {
        // "zzz" passes the whitelist (valid shape) but is not installed, so tesseract itself
        // must reject it — proving validation only guards shape, not availability.
        var image = TestPages.CreateSimplePage(_dir);

        var result = await _runner.RecognizeAsync(image, Path.Combine(_dir, "out2"), 300, "zzz", Ct);

        Assert.False(result.Success);
        Assert.Contains("zzz", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Timeout_kills_the_child_and_reports_failure()
    {
        using var impatient = new TesseractRunner(
            tessdataDir: Path.Combine(_dir, "tessdata"), timeout: TimeSpan.FromMilliseconds(1));
        var image = TestPages.CreateSimplePage(_dir);

        var result = await impatient.RecognizeAsync(image, Path.Combine(_dir, "out3"), 300, "eng", Ct);

        Assert.False(result.Success);
        Assert.Contains("Timed out", result.Error);
    }
}
