using System.IO;
using FgScanner.App.Views;
using FgScanner.Data;
using Xunit;

namespace FgScanner.App.Tests;

/// <summary>
/// SPEC-2026-009 AC-10. View OCR shows the page's .md — the file the rest of the pipeline uses —
/// without its front matter; the database's plain text when there is no .md; and otherwise a
/// sentence saying why there is no text, never an empty pane that looks like a page with no words.
/// </summary>
public sealed class OcrTextSourceTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "fgscanner-tests", Guid.NewGuid().ToString("N"));

    public OcrTextSourceTests()
    {
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private string Image(string name = "scan_00001.jpg") => Path.Combine(_root, name);

    /// <summary>The shape OcrPipeline writes beside the image.</summary>
    private void WriteMarkdown(string body, string name = "scan_00001")
    {
        File.WriteAllText(Path.Combine(_root, name + ".md"),
            "---\nengine: tesseract\ntier: 0\nmean_confidence: 91.5\nduration_ms: 1234\n---\n\n" + body);
    }

    [Fact]
    public void The_md_beside_the_image_is_shown_without_its_front_matter()
    {
        WriteMarkdown("# Notes\n\nDeposition 10/09/2023\n\n| a | b |\n|---|---|\n| 1 | 2 |\n");

        var text = OcrTextSource.Read(Image(), "plain text", OcrStatus.Yes, isBlank: false);

        Assert.Equal("# Notes\n\nDeposition 10/09/2023\n\n| a | b |\n|---|---|\n| 1 | 2 |", text.ReplaceLineEndings("\n"));
    }

    [Fact]
    public void A_md_with_windows_line_endings_loses_its_front_matter_too()
    {
        File.WriteAllText(Path.Combine(_root, "scan_00001.md"),
            "---\r\nengine: tesseract\r\n---\r\n\r\nHello\r\n");

        Assert.Equal("Hello", OcrTextSource.Read(Image(), null, OcrStatus.Yes, isBlank: false));
    }

    [Fact]
    public void Without_a_md_the_plain_text_is_shown()
    {
        Assert.Equal("plain text", OcrTextSource.Read(Image(), "plain text", OcrStatus.Yes, isBlank: false));
    }

    [Fact]
    public void A_md_that_cannot_be_read_falls_back_to_the_plain_text()
    {
        WriteMarkdown("from the file");
        using var held = new FileStream(
            Path.Combine(_root, "scan_00001.md"), FileMode.Open, FileAccess.Read, FileShare.None);

        Assert.Equal("plain text", OcrTextSource.Read(Image(), "plain text", OcrStatus.Yes, isBlank: false));
    }

    [Theory]
    [InlineData(OcrStatus.No, "Not OCRed yet — use OCR pages on the Groups page.")]
    [InlineData(OcrStatus.Pending, "OCR is still running for this page.")]
    [InlineData(OcrStatus.Failed, "OCR failed on this page — try Re-process.")]
    [InlineData(OcrStatus.Yes, "OCR found no text on this page.")]
    public void With_no_text_at_all_a_sentence_says_why(OcrStatus status, string expected)
    {
        Assert.Equal(expected, OcrTextSource.Read(Image(), null, status, isBlank: false));
        Assert.Equal(expected, OcrTextSource.Read(Image(), "  ", status, isBlank: false));
    }

    [Fact]
    public void A_blank_page_says_it_was_left_out()
    {
        Assert.Equal("Blank page — excluded from OCR.",
            OcrTextSource.Read(Image(), null, OcrStatus.No, isBlank: true));
    }
}
