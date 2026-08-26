using FgScanner.Core.Imaging;
using Xunit;

namespace FgScanner.Ocr.Tests;

/// <summary>
/// Page-orientation detection (docs/scope-auto-orientation.md). A sheet fed into the ADF upside
/// down OCRs to confident-looking reversed gibberish — measured on real scans at 23-42% mean
/// confidence against 80-96% for the same sheets upright — and nothing downstream can tell the
/// difference. Real Tesseract, never a mock: the engine is deterministic.
/// </summary>
public sealed class OrientationTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("fgs-ocr-osd").FullName;
    private readonly TesseractRunner _runner;

    public OrientationTests() => _runner = new TesseractRunner(tessdataDir: TestPages.PrepareTessdata(_dir));

    public void Dispose()
    {
        _runner.Dispose();
        Directory.Delete(_dir, recursive: true);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void Osd_traineddata_ships_beside_the_app() =>
        Assert.True(File.Exists(Path.Combine(TesseractPaths.BundledTessdataDir, "osd.traineddata")));

    [Fact]
    public async Task An_upright_page_needs_no_rotation()
    {
        var page = TestPages.CreateDensePage(_dir);

        var result = await _runner.DetectOrientationAsync(page, Ct);

        Assert.NotNull(result);
        Assert.Equal(0, result.RotateClockwiseDegrees);
    }

    [Theory]
    [InlineData(90)]
    [InlineData(180)]
    [InlineData(270)]
    public async Task A_turned_page_reports_the_rotation_that_uprights_it(int turnedClockwise)
    {
        var page = TestPages.CreateRotatedPage(_dir, turnedClockwise);

        var result = await _runner.DetectOrientationAsync(page, Ct);

        Assert.NotNull(result);
        // The reported angle is what must be applied, so turning the sheet back the way it came.
        Assert.Equal((360 - turnedClockwise) % 360, result.RotateClockwiseDegrees);
    }

    [Fact]
    public async Task A_blank_page_reports_nothing_rather_than_guessing()
    {
        // Tesseract exits non-zero with "Too few characters" when there is nothing to measure.
        // That must read as "cannot say", never as "upright" — a wrong 0 would silently keep a
        // misfed page as-is.
        var blank = TestPages.CreateBlankPage(_dir);

        Assert.Null(await _runner.DetectOrientationAsync(blank, Ct));
    }

    [Fact]
    public async Task A_missing_file_reports_nothing()
    {
        Assert.Null(await _runner.DetectOrientationAsync(Path.Combine(_dir, "absent.png"), Ct));
    }

    [Theory]
    [InlineData(90)]
    [InlineData(180)]
    [InlineData(270)]
    public async Task The_pipeline_turns_a_misfed_page_upright_and_then_reads_it(int turnedClockwise)
    {
        var page = TestPages.CreateRotatedPage(_dir, turnedClockwise);
        var pipeline = new OcrPipeline(_runner, new TestRotator());

        var outcome = await pipeline.ProcessPageAsync(page, 300, "eng", Ct);

        Assert.True(outcome.Success, outcome.Error);
        Assert.Equal((360 - turnedClockwise) % 360, outcome.RotatedClockwiseDegrees);
        Assert.Contains("Quarterly", outcome.PlainText!, StringComparison.OrdinalIgnoreCase);
        Assert.True(outcome.MeanConfidence > 70, $"expected a confident read, got {outcome.MeanConfidence}");
    }

    [Fact]
    public async Task An_upright_page_is_left_byte_identical()
    {
        // Rewriting every page would re-encode it for nothing — on JPEG that is silent generation
        // loss on each scan, and it would churn the checksum and perceptual hash too.
        var page = TestPages.CreateDensePage(_dir);
        var before = await File.ReadAllBytesAsync(page, Ct);
        var pipeline = new OcrPipeline(_runner, new TestRotator());

        var outcome = await pipeline.ProcessPageAsync(page, 300, "eng", Ct);

        Assert.Equal(0, outcome.RotatedClockwiseDegrees);
        Assert.Equal(before, await File.ReadAllBytesAsync(page, Ct));
    }

    [Fact]
    public async Task Without_a_rotator_the_page_is_read_as_it_lies()
    {
        // The feature flag turns the rotator off; OCR must still run, just uncorrected.
        var page = TestPages.CreateRotatedPage(_dir, 180);
        var pipeline = new OcrPipeline(_runner);

        var outcome = await pipeline.ProcessPageAsync(page, 300, "eng", Ct);

        Assert.True(outcome.Success, outcome.Error);
        Assert.Equal(0, outcome.RotatedClockwiseDegrees);
    }

    /// <summary>Rotation as the real editor does it, without pulling FgScanner.Scanning in here.</summary>
    private sealed class TestRotator : IPageRotator
    {
        public Task RotateAsync(string imagePath, int clockwiseDegrees, CancellationToken cancellationToken)
        {
            using (var bitmap = new System.Drawing.Bitmap(imagePath))
            {
                bitmap.RotateFlip(clockwiseDegrees switch
                {
                    90 => System.Drawing.RotateFlipType.Rotate90FlipNone,
                    180 => System.Drawing.RotateFlipType.Rotate180FlipNone,
                    _ => System.Drawing.RotateFlipType.Rotate270FlipNone,
                });
                var temp = imagePath + ".tmp";
                bitmap.Save(temp, System.Drawing.Imaging.ImageFormat.Png);
                bitmap.Dispose();
                File.Move(temp, imagePath, overwrite: true);
            }

            return Task.CompletedTask;
        }
    }
}
