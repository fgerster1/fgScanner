using System.Globalization;
using System.Text;
using Xunit;

namespace FgScanner.Ocr.Tests;

/// <summary>Deterministic reconstruction tests over synthetic TSV (no engine involved).</summary>
public class MarkdownReconstructorTests
{
    private sealed class TsvBuilder
    {
        private readonly StringBuilder _sb = new(
            "level\tpage_num\tblock_num\tpar_num\tline_num\tword_num\tleft\ttop\twidth\theight\tconf\ttext\n");
        private int _line;

        public TsvBuilder(int pageWidth = 1700, int pageHeight = 2200) =>
            _sb.AppendLine(Invariant($"1\t1\t0\t0\t0\t0\t0\t0\t{pageWidth}\t{pageHeight}\t-1\t"));

        public TsvBuilder Line(int top, int height, params (string Text, int Left, int Width)[] words)
        {
            _line++;
            var wordNum = 0;
            foreach (var (text, left, width) in words)
            {
                wordNum++;
                _sb.AppendLine(Invariant(
                    $"5\t1\t1\t1\t{_line}\t{wordNum}\t{left}\t{top}\t{width}\t{height}\t91.5\t{text}"));
            }

            return this;
        }

        public TsvPage Build() => TsvParser.Parse(_sb.ToString());

        private static string Invariant(FormattableString s) => s.ToString(CultureInfo.InvariantCulture);
    }

    private static (string Text, int Left, int Width) W(string text, int left, int width = 100) =>
        (text, left, width);

    [Fact]
    public void Large_lines_become_headings_bucketed_by_ratio()
    {
        var page = new TsvBuilder()
            .Line(100, 50, W("Big", 100), W("Title", 220))          // 50/24 > 1.9 → #
            .Line(200, 38, W("Section", 100))                        // 38/24 > 1.5 → ##
            .Line(300, 31, W("Subsection", 100))                     // 31/24 > 1.25 → ###
            .Line(400, 24, W("Body", 100), W("text", 220))
            .Line(430, 24, W("more", 100), W("body", 220))
            .Line(460, 24, W("and", 100), W("more", 220))
            .Line(490, 24, W("body", 100), W("lines", 220))
            .Build();

        var markdown = MarkdownReconstructor.ToMarkdown(page);

        Assert.Contains("# Big Title", markdown);
        Assert.Contains("## Section", markdown);
        Assert.Contains("### Subsection", markdown);
        Assert.Contains("Body text", markdown);
    }

    [Fact]
    public void Wide_vertical_gaps_split_paragraphs()
    {
        var page = new TsvBuilder()
            .Line(100, 24, W("First", 100), W("paragraph", 220))
            .Line(134, 24, W("continues", 100), W("here", 220))
            .Line(168, 24, W("and", 100), W("here", 220))
            .Line(300, 24, W("Second", 100), W("paragraph", 220))    // 108px gap ≫ 10px median
            .Line(334, 24, W("continues", 100), W("too", 220))
            .Build();

        var markdown = MarkdownReconstructor.ToMarkdown(page);

        Assert.Contains("First paragraph continues here and here", markdown);
        Assert.Contains("Second paragraph continues too", markdown);
        Assert.Contains("here\n\nSecond", markdown.Replace("\r\n", "\n", StringComparison.Ordinal));
    }

    [Fact]
    public void Bullet_markers_with_aligned_siblings_become_list_items()
    {
        var page = new TsvBuilder()
            .Line(100, 24, W("•", 100, 20), W("Apples", 140), W("are", 260), W("red", 340))
            .Line(134, 24, W("•", 100, 20), W("Bananas", 140), W("are", 260), W("yellow", 340))
            .Line(168, 24, W("•", 100, 20), W("Grapes", 140), W("are", 260), W("green", 340))
            .Build();

        var markdown = MarkdownReconstructor.ToMarkdown(page);

        Assert.Contains("- Apples are red", markdown);
        Assert.Contains("- Bananas are yellow", markdown);
        Assert.Contains("- Grapes are green", markdown);
    }

    [Fact]
    public void Numbered_markers_keep_their_numbers()
    {
        var page = new TsvBuilder()
            .Line(100, 24, W("1.", 100, 30), W("First", 150), W("step", 270))
            .Line(134, 24, W("2.", 100, 30), W("Second", 150), W("step", 270))
            .Build();

        var markdown = MarkdownReconstructor.ToMarkdown(page);

        Assert.Contains("1. First step", markdown);
        Assert.Contains("2. Second step", markdown);
    }

    [Fact]
    public void Wrapped_list_items_confirmed_by_hanging_indent_absorb_continuations()
    {
        var page = new TsvBuilder()
            .Line(100, 24, W("•", 100, 20), W("This", 140), W("item", 260), W("wraps", 380))
            .Line(134, 24, W("onto", 140), W("another", 260), W("line", 400))   // hanging indent at 140
            .Line(168, 24, W("•", 100, 20), W("Short", 140), W("item", 260))
            .Build();

        var markdown = MarkdownReconstructor.ToMarkdown(page);

        Assert.Contains("- This item wraps onto another line", markdown);
        Assert.Contains("- Short item", markdown);
    }

    [Fact]
    public void Two_columns_are_read_column_major()
    {
        var builder = new TsvBuilder(pageWidth: 1700);
        // Left column x 100–700; right column x 1100–1700 — 400px valley between them.
        builder
            .Line(100, 24, W("Left", 100), W("one", 260))
            .Line(134, 24, W("Left", 100), W("two", 260))
            .Line(168, 24, W("Left", 100), W("three", 260))
            .Line(100, 24, W("Right", 1100), W("one", 1260))
            .Line(134, 24, W("Right", 1100), W("two", 1260))
            .Line(168, 24, W("Right", 1100), W("three", 1260));

        var text = MarkdownReconstructor.ToPlainText(builder.Build());

        var leftIndex = text.IndexOf("Left three", StringComparison.Ordinal);
        var rightIndex = text.IndexOf("Right one", StringComparison.Ordinal);
        Assert.True(leftIndex >= 0 && rightIndex > leftIndex,
            $"expected column-major order, got:\n{text}");
    }

    [Fact]
    public void Grid_like_blocks_are_emitted_as_fenced_preformatted()
    {
        var page = new TsvBuilder()
            .Line(100, 24, W("Item", 100), W("Qty", 600), W("Price", 1100))
            .Line(134, 24, W("Widget", 100), W("2", 600), W("9.99", 1100))
            .Line(168, 24, W("Gadget", 100), W("5", 600), W("19.99", 1100))
            .Build();

        var markdown = MarkdownReconstructor.ToMarkdown(page);

        Assert.Contains("```", markdown);
        Assert.Contains("Widget", markdown);
        var fenced = markdown.Split("```")[1];
        Assert.Contains("Qty", fenced);
        Assert.Contains("19.99", fenced);
    }

    [Fact]
    public void Empty_page_produces_empty_markdown_and_zero_confidence()
    {
        var page = new TsvBuilder().Build();

        Assert.Equal("", MarkdownReconstructor.ToMarkdown(page));
        Assert.Equal("", MarkdownReconstructor.ToPlainText(page));
        Assert.Equal(0, page.MeanConfidence);
    }

    [Fact]
    public void Structure_rows_with_negative_confidence_are_ignored()
    {
        var tsv = "level\tpage_num\tblock_num\tpar_num\tline_num\tword_num\tleft\ttop\twidth\theight\tconf\ttext\n"
            + "1\t1\t0\t0\t0\t0\t0\t0\t1000\t1000\t-1\t\n"
            + "4\t1\t1\t1\t1\t0\t90\t95\t400\t40\t-1\t\n"
            + "5\t1\t1\t1\t1\t1\t100\t100\t80\t24\t95.0\tHello\n";

        var page = TsvParser.Parse(tsv);

        var word = Assert.Single(page.Words);
        Assert.Equal("Hello", word.Text);
        Assert.Equal(95.0, page.MeanConfidence);
    }
}
