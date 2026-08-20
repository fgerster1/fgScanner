using System.Globalization;
using System.Text;
using FgScanner.Core.Index;

namespace FgScanner.Ocr;

public sealed record OcrPageOutcome(
    bool Success,
    string? Error,
    string? MarkdownPath,
    string? PlainText,
    double MeanConfidence,
    TimeSpan Duration);

/// <summary>
/// Full per-page OCR (PLAN §5.5): one Tesseract pass → TSV → geometric Markdown, written as
/// &lt;imagebase&gt;.md beside the image with YAML front matter. The intermediate pdf/hocr/tsv
/// live in a temp work dir and are cleaned up.
/// </summary>
public sealed class OcrPipeline(TesseractRunner runner)
{
    public const double LowConfidenceThreshold = 65;

    private readonly AtomicFileWriter _writer = new();

    public async Task<OcrPageOutcome> ProcessPageAsync(
        string imagePath, int dpi = 300, string languages = "eng",
        CancellationToken cancellationToken = default)
    {
        var workDir = Directory.CreateTempSubdirectory("fgscanner-ocr").FullName;
        try
        {
            var outputBase = Path.Combine(workDir, "page");
            var run = await runner.RecognizeAsync(imagePath, outputBase, dpi, languages, cancellationToken)
                .ConfigureAwait(false);
            if (!run.Success)
            {
                return new OcrPageOutcome(false, run.Error, null, null, 0, run.Duration);
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
                return new OcrPageOutcome(false, message, null, null, meanConfidence, run.Duration);
            }

            return new OcrPageOutcome(true, null, markdownPath, plainText, meanConfidence, run.Duration);
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
}
