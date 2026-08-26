using System.Globalization;
using System.Text;
using FgScanner.Core.Imaging;
using FgScanner.Core.Index;

namespace FgScanner.Ocr;

public sealed record OcrPageOutcome(
    bool Success,
    string? Error,
    string? MarkdownPath,
    string? PlainText,
    double MeanConfidence,
    TimeSpan Duration,
    int RotatedClockwiseDegrees = 0);

/// <summary>
/// Full per-page OCR (PLAN §5.5): one Tesseract pass → TSV → geometric Markdown, written as
/// &lt;imagebase&gt;.md beside the image with YAML front matter. The intermediate pdf/hocr/tsv
/// live in a temp work dir and are cleaned up.
///
/// When a <paramref name="rotator"/> is supplied, the page is first turned upright. A sheet fed
/// into the ADF the wrong way round OCRs to confident-looking reversed gibberish — measured at
/// 23-42% mean confidence against 80-96% for the same sheets upright — and no downstream consumer
/// can tell the two apart, so this has to be corrected before recognition rather than flagged after.
/// </summary>
public sealed class OcrPipeline(
    TesseractRunner runner,
    IPageRotator? rotator = null,
    Func<CancellationToken, Task<bool>>? autoOrientEnabled = null)
{
    public const double LowConfidenceThreshold = 65;

    private readonly AtomicFileWriter _writer = new();

    public async Task<OcrPageOutcome> ProcessPageAsync(
        string imagePath, int dpi = 300, string languages = "eng",
        CancellationToken cancellationToken = default)
    {
        var rotated = await UprightAsync(imagePath, cancellationToken).ConfigureAwait(false);
        var workDir = Directory.CreateTempSubdirectory("fgscanner-ocr").FullName;
        try
        {
            var outputBase = Path.Combine(workDir, "page");
            var run = await runner.RecognizeAsync(imagePath, outputBase, dpi, languages, cancellationToken)
                .ConfigureAwait(false);
            if (!run.Success)
            {
                return new OcrPageOutcome(false, run.Error, null, null, 0, run.Duration, rotated);
            }

            var tsv = await File.ReadAllTextAsync(run.TsvPath!, cancellationToken).ConfigureAwait(false);
            var page = TsvParser.Parse(tsv);
            var markdown = MarkdownReconstructor.ToMarkdown(page);
            var plainText = MarkdownReconstructor.ToPlainText(page);
            var meanConfidence = Math.Round(page.MeanConfidence, 2);

            var frontMatter = new StringBuilder()
                .AppendLine("---")
                .AppendLine("engine: tesseract")
                .AppendLine("tier: 0")
                .AppendLine(string.Create(CultureInfo.InvariantCulture, $"mean_confidence: {meanConfidence}"))
                .AppendLine(string.Create(
                    CultureInfo.InvariantCulture, $"duration_ms: {(long)run.Duration.TotalMilliseconds}"))
                .AppendLine("---")
                .AppendLine()
                .Append(markdown)
                .ToString();

            var markdownPath = Path.Combine(
                Path.GetDirectoryName(Path.GetFullPath(imagePath))!,
                Path.GetFileNameWithoutExtension(imagePath) + ".md");
            var (outcome, message) = await _writer.WriteAsync(
                markdownPath,
                async stream =>
                {
                    await using var writer = new StreamWriter(stream, new UTF8Encoding(false), leaveOpen: true);
                    await writer.WriteAsync(frontMatter).ConfigureAwait(false);
                },
                cancellationToken).ConfigureAwait(false);
            if (outcome != ExportOutcome.Success)
            {
                return new OcrPageOutcome(false, message, null, null, meanConfidence, run.Duration, rotated);
            }

            return new OcrPageOutcome(
                true, null, markdownPath, plainText, meanConfidence, run.Duration, rotated);
        }
        finally
        {
            try
            {
                Directory.Delete(workDir, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }

    /// <summary>
    /// Turns the page upright if it is not already, returning the clockwise degrees applied.
    /// An undetectable orientation leaves the page alone: a blank or near-blank sheet has nothing
    /// to measure, and guessing would rewrite a file for no reason.
    /// </summary>
    private async Task<int> UprightAsync(string imagePath, CancellationToken cancellationToken)
    {
        if (rotator is null)
        {
            return 0;
        }

        // Checked per page rather than once at construction, so turning the flag off in Settings
        // takes effect on the next page instead of the next launch.
        if (autoOrientEnabled is not null
            && !await autoOrientEnabled(cancellationToken).ConfigureAwait(false))
        {
            return 0;
        }

        var orientation = await runner.DetectOrientationAsync(imagePath, cancellationToken).ConfigureAwait(false);
        if (orientation is null || orientation.RotateClockwiseDegrees == 0)
        {
            return 0;
        }

        await rotator.RotateAsync(imagePath, orientation.RotateClockwiseDegrees, cancellationToken)
            .ConfigureAwait(false);
        return orientation.RotateClockwiseDegrees;
    }
}
