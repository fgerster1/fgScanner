using Xunit;

namespace FgScanner.Ocr.Tests;

public sealed class OcrPipelineTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("fgs-ocr-pipe").FullName;
    private readonly TesseractRunner _runner;
    private readonly OcrPipeline _pipeline;

    public OcrPipelineTests()
    {
        _runner = new TesseractRunner(tessdataDir: TestPages.PrepareTessdata(_dir));
        _pipeline = new OcrPipeline(_runner);
    }

    public void Dispose()
    {
        _runner.Dispose();
        Directory.Delete(_dir, recursive: true);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Page_gets_md_sidecar_with_front_matter_beside_the_image()
    {
        var image = TestPages.CreateSimplePage(_dir);

        var outcome = await _pipeline.ProcessPageAsync(image, 300, "eng", Ct);

        Assert.True(outcome.Success, outcome.Error);
        Assert.Equal(Path.Combine(_dir, "page.md"), outcome.MarkdownPath);
        var markdown = await File.ReadAllTextAsync(outcome.MarkdownPath!, Ct);
        Assert.StartsWith("---", markdown);
        Assert.Contains("engine: tesseract", markdown);
        Assert.Contains("tier: 0", markdown);
        Assert.Contains("mean_confidence:", markdown);
        Assert.Contains("duration_ms:", markdown);
        Assert.Contains("quick brown fox", markdown, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Quarterly Report", markdown);
    }

    [Fact]
    public async Task Plain_text_and_confidence_feed_the_database_columns()
    {
        var image = TestPages.CreateSimplePage(_dir, "p2.png");

        var outcome = await _pipeline.ProcessPageAsync(image, 300, "eng", Ct);

        Assert.True(outcome.Success, outcome.Error);
        Assert.Contains("fox", outcome.PlainText, StringComparison.OrdinalIgnoreCase);
        Assert.InRange(outcome.MeanConfidence, 50, 100);
        Assert.True(outcome.Duration > TimeSpan.Zero);
    }

    [Fact]
    public async Task Failure_reports_error_without_writing_a_sidecar()
    {
        var missing = Path.Combine(_dir, "nope.png");

        var outcome = await _pipeline.ProcessPageAsync(missing, 300, "eng", Ct);

        Assert.False(outcome.Success);
        Assert.NotNull(outcome.Error);
        Assert.False(File.Exists(Path.Combine(_dir, "nope.md")));
    }
}
