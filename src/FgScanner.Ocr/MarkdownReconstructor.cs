using System.Text;
using System.Text.RegularExpressions;

namespace FgScanner.Ocr;

/// <summary>
/// Geometric Markdown reconstruction over Tesseract TSV (PLAN §5.5). The LSTM engine emits no
/// font/bold/size data, so all structure is inferred from geometry: columns from vertical
/// ink-projection valleys (read column-major), headings from line height vs the body median,
/// lists from marker patterns confirmed by hanging indents, paragraphs from gap analysis, and
/// table-like blocks emitted as fenced preformatted text.
/// </summary>
public static partial class MarkdownReconstructor
{
    [GeneratedRegex(@"^(?<bullet>[-•*‣▪◦])$|^(?<number>\d{1,3})[.)]$|^(?<letter>[a-z])[.)]$")]
    private static partial Regex ListMarkerPattern();

    private sealed record Line(
        int Block, int Paragraph, IReadOnlyList<TsvWord> Words)
    {
        public int Left => Words.Min(w => w.Left);

        public int Right => Words.Max(w => w.Right);

        public int Top => Words.Min(w => w.Top);

        public int Bottom => Words.Max(w => w.Bottom);

        public double Height => Median(Words.Select(w => (double)w.Height));

        public double Center => (Left + Right) / 2.0;

        public string Text => string.Join(' ', Words.Select(w => w.Text));
    }

    public static string ToMarkdown(TsvPage page)
    {
        var lines = GroupLines(page);
        if (lines.Count == 0)
        {
            return "";
        }

        var output = new StringBuilder();
        foreach (var column in SplitColumns(lines, page.Width))
        {
            RenderColumn(column, output);
        }

        return output.ToString().TrimEnd() + "\n";
    }

    /// <summary>Reading-order plain text (for the database/FTS index).</summary>
    public static string ToPlainText(TsvPage page)
    {
        var lines = GroupLines(page);
        var builder = new StringBuilder();
        foreach (var column in SplitColumns(lines, page.Width))
        {
            foreach (var line in column)
            {
                builder.AppendLine(line.Text);
            }
        }

        return builder.ToString().TrimEnd();
    }

    private static List<Line> GroupLines(TsvPage page) =>
        [.. page.Words
            .GroupBy(w => (w.Block, w.Paragraph, w.Line))
            .Select(g => new Line(g.Key.Block, g.Key.Paragraph, [.. g.OrderBy(w => w.WordNum)]))
            .OrderBy(l => l.Top)];

    /// <summary>
    /// Vertical ink-projection: a horizontal gap with no words, wider than ~2.5 body heights and
    /// crossed by most of the page's vertical extent, splits the page into columns read
    /// column-major (all of column 1, then column 2).
    /// </summary>
    private static List<List<Line>> SplitColumns(List<Line> lines, int pageWidth)
    {
        if (lines.Count < 6 || pageWidth <= 0)
        {
            return [lines];
        }

        var coverage = new int[pageWidth];
        foreach (var word in lines.SelectMany(l => l.Words))
        {
            for (var x = Math.Max(0, word.Left); x < Math.Min(pageWidth, word.Right); x++)
            {
                coverage[x]++;
            }
        }

        var bodyHeight = Median(lines.Select(l => l.Height));
        var minValleyWidth = (int)(bodyHeight * 2.5);
        var contentStart = Array.FindIndex(coverage, c => c > 0);
        var contentEnd = Array.FindLastIndex(coverage, c => c > 0);
        var valleys = new List<(int Start, int End)>();
        var runStart = -1;
        for (var x = contentStart; x <= contentEnd; x++)
        {
            if (coverage[x] == 0)
            {
                if (runStart < 0)
                {
                    runStart = x;
                }
            }
            else if (runStart >= 0)
            {
                if (x - runStart >= minValleyWidth)
                {
                    valleys.Add((runStart, x));
                }

                runStart = -1;
            }
        }

        if (valleys.Count == 0)
        {
            return [lines];
        }

        var boundaries = valleys.Select(v => (v.Start + v.End) / 2).ToList();
        var columns = Enumerable.Range(0, boundaries.Count + 1).Select(_ => new List<Line>()).ToList();
        foreach (var line in lines)
        {
            var index = boundaries.Count(b => line.Center > b);
            columns[index].Add(line);
        }

        // A valley must actually separate text (both sides populated), or it was just a margin.
        columns = [.. columns.Where(c => c.Count > 0)];
        return columns.Count > 1 ? columns : [lines];
    }

    private static void RenderColumn(List<Line> lines, StringBuilder output)
    {
        var bodyHeight = Median(lines.Select(l => l.Height));
        var gaps = lines.Zip(lines.Skip(1), (a, b) => (double)(b.Top - a.Bottom)).Where(g => g > 0).ToList();
        var medianGap = gaps.Count > 0 ? Median(gaps) : bodyHeight * 0.5;

        var index = 0;
        var paragraph = new List<string>();
        void FlushParagraph()
        {
            if (paragraph.Count > 0)
            {
                output.AppendLine(string.Join(' ', paragraph));
                output.AppendLine();
                paragraph.Clear();
            }
        }

        while (index < lines.Count)
        {
            var line = lines[index];

            var tableEnd = DetectTableBlock(lines, index, bodyHeight);
            if (tableEnd > index)
            {
                FlushParagraph();
                output.AppendLine("```");
                for (var i = index; i < tableEnd; i++)
                {
                    output.AppendLine(RenderPreformatted(lines[i], bodyHeight));
                }

                output.AppendLine("```");
                output.AppendLine();
                index = tableEnd;
                continue;
            }

            var headingLevel = HeadingLevel(line.Height, bodyHeight);
            if (headingLevel > 0)
            {
                FlushParagraph();
                output.AppendLine(new string('#', headingLevel) + " " + line.Text);
                output.AppendLine();
                index++;
                continue;
            }

            if (TryRenderListItem(lines, ref index, output, medianGap, paragraph, FlushParagraph))
            {
                continue;
            }

            // Paragraph gap analysis: a clearly larger-than-usual gap starts a new paragraph.
            if (index > 0 && paragraph.Count > 0)
            {
                var gap = line.Top - lines[index - 1].Bottom;
                if (gap > medianGap * 1.75)
                {
                    FlushParagraph();
                }
            }

            paragraph.Add(line.Text);
            index++;
        }

        FlushParagraph();
    }

    private static int HeadingLevel(double lineHeight, double bodyHeight)
    {
        var ratio = lineHeight / bodyHeight;
        return ratio switch
        {
            > 1.9 => 1,
            > 1.5 => 2,
            > 1.25 => 3,
            _ => 0,
        };
    }

    private static bool TryRenderListItem(
        List<Line> lines, ref int index, StringBuilder output, double medianGap,
        List<string> paragraph, Action flushParagraph)
    {
        var line = lines[index];
        if (line.Words.Count < 2)
        {
            return false;
        }

        var match = ListMarkerPattern().Match(line.Words[0].Text);
        if (!match.Success)
        {
            return false;
        }

        // Hanging-indent confirmation: a wrapped continuation aligns with the text, not the
        // marker; a following sibling starts with a marker at the same x. Either confirms a list.
        var textLeft = line.Words[1].Left;
        var confirmed = false;
        if (index + 1 < lines.Count)
        {
            var next = lines[index + 1];
            var gap = next.Top - line.Bottom;
            if (gap < medianGap * 1.75)
            {
                confirmed = Math.Abs(next.Left - textLeft) < line.Height
                    || (ListMarkerPattern().IsMatch(next.Words[0].Text)
                        && Math.Abs(next.Left - line.Left) < line.Height);
            }
        }
        else
        {
            confirmed = true; // sole trailing line with a marker — keep it as a list item
        }

        if (!confirmed)
        {
            return false;
        }

        flushParagraph();
        var content = string.Join(' ', line.Words.Skip(1).Select(w => w.Text));
        var item = match.Groups["number"].Success
            ? $"{match.Groups["number"].Value}. {content}"
            : $"- {content}";

        // Absorb wrapped continuation lines (indented to the text edge, no own marker).
        var lookahead = index + 1;
        while (lookahead < lines.Count)
        {
            var next = lines[lookahead];
            if (next.Top - lines[lookahead - 1].Bottom >= medianGap * 1.75
                || ListMarkerPattern().IsMatch(next.Words[0].Text)
                || Math.Abs(next.Left - textLeft) >= line.Height)
            {
                break;
            }

            item += " " + next.Text;
            lookahead++;
        }

        output.AppendLine(item);
        if (lookahead >= lines.Count || !ListMarkerPattern().IsMatch(lines[lookahead].Words[0].Text))
        {
            output.AppendLine();
        }

        index = lookahead;
        return true;
    }

    /// <summary>
    /// Table-ish block: two or more consecutive lines that each contain two or more wide
    /// intra-line gaps (cell separators). Emitted fenced with spacing preserved (v1 — PLAN §5.5).
    /// </summary>
    private static int DetectTableBlock(List<Line> lines, int start, double bodyHeight)
    {
        var end = start;
        while (end < lines.Count && CountCellGaps(lines[end], bodyHeight) >= 2)
        {
            end++;
        }

        return end - start >= 2 ? end : start;
    }

    private static int CountCellGaps(Line line, double bodyHeight)
    {
        var gaps = 0;
        for (var i = 1; i < line.Words.Count; i++)
        {
            if (line.Words[i].Left - line.Words[i - 1].Right > bodyHeight * 2)
            {
                gaps++;
            }
        }

        return gaps;
    }

    private static string RenderPreformatted(Line line, double bodyHeight)
    {
        var averageCharWidth = Math.Max(1.0, bodyHeight * 0.5);
        var builder = new StringBuilder();
        var cursor = line.Left;
        foreach (var word in line.Words)
        {
            var spaces = builder.Length == 0 ? 0 : Math.Max(1, (int)((word.Left - cursor) / averageCharWidth));
            builder.Append(' ', spaces).Append(word.Text);
            cursor = word.Right;
        }

        return builder.ToString();
    }

    private static double Median(IEnumerable<double> values)
    {
        var sorted = values.Order().ToList();
        if (sorted.Count == 0)
        {
            return 0;
        }

        var mid = sorted.Count / 2;
        return sorted.Count % 2 == 1 ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) / 2;
    }
}
