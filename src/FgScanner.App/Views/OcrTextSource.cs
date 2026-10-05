using System.IO;
using FgScanner.Data;

namespace FgScanner.App.Views;

/// <summary>
/// The OCR text View OCR shows for a page (SPEC-2026-009 §08-5, Q3). The .md beside the image comes
/// first — it keeps headings and tables, and it is the file the rest of the pipeline reads — then the
/// database's plain text, then a sentence saying why there is none. An empty pane would read as "this
/// page has no words", which is a claim about evidence nobody made.
/// </summary>
public static class OcrTextSource
{
    public static string Read(string imagePath, string? ocrText, OcrStatus status, bool isBlank)
    {
        var markdown = ReadMarkdown(imagePath);
        if (!string.IsNullOrWhiteSpace(markdown))
        {
            return markdown;
        }

        if (!string.IsNullOrWhiteSpace(ocrText))
        {
            return ocrText.Trim();
        }

        if (isBlank)
        {
            return "Blank page — excluded from OCR.";
        }

        return status switch
        {
            OcrStatus.No => "Not OCRed yet — use OCR pages on the Groups page.",
            OcrStatus.Pending => "OCR is still running for this page.",
            OcrStatus.Failed => "OCR failed on this page — try Re-process.",
            _ => "OCR found no text on this page.",
        };
    }

    /// <summary>The .md body without the YAML front matter OcrPipeline writes; null if there is no
    /// readable .md. A file held open by a re-OCR is a reason to fall back, not to fail.</summary>
    private static string? ReadMarkdown(string imagePath)
    {
        var path = Path.ChangeExtension(imagePath, ".md");
        string text;
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            text = File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The path only: OCR text is case material and never goes to the log.
            Serilog.Log.Warning(ex, "OCR text file could not be read: {Path}", path);
            return null;
        }

        var lines = text.ReplaceLineEndings("\n").Split('\n');
        if (lines.Length > 0 && lines[0].Trim() == "---")
        {
            var end = Array.FindIndex(lines, 1, l => l.Trim() == "---");
            if (end > 0)
            {
                return string.Join(Environment.NewLine, lines.Skip(end + 1)).Trim();
            }
        }

        return text.Trim();
    }
}
