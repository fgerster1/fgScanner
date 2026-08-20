using System.Globalization;

namespace FgScanner.Ocr;

/// <summary>One level-5 (word) row from Tesseract's TSV output.</summary>
public sealed record TsvWord(
    int Block, int Paragraph, int Line, int WordNum,
    int Left, int Top, int Width, int Height,
    double Confidence, string Text)
{
    public int Right => Left + Width;

    public int Bottom => Top + Height;
}

public sealed record TsvPage(int Width, int Height, IReadOnlyList<TsvWord> Words)
{
    public double MeanConfidence =>
        Words.Count == 0 ? 0 : Words.Average(w => w.Confidence);
}

/// <summary>
/// Parses Tesseract TSV: columns level/page/block/par/line/word/left/top/width/height/conf/text.
/// Level 1 rows carry the page box; level 5 rows are words (conf -1 rows are structure, skipped).
/// </summary>
public static class TsvParser
{
    public static TsvPage Parse(string tsvContent)
    {
        var words = new List<TsvWord>();
        var pageWidth = 0;
        var pageHeight = 0;
        foreach (var line in tsvContent.Split('\n'))
        {
            var columns = line.TrimEnd('\r').Split('\t');
            if (columns.Length < 12 || !int.TryParse(columns[0], out var level))
            {
                continue; // header or malformed row
            }

            var left = Int(columns[6]);
            var top = Int(columns[7]);
            var width = Int(columns[8]);
            var height = Int(columns[9]);
            if (level == 1)
            {
                pageWidth = width;
                pageHeight = height;
                continue;
            }

            if (level != 5)
            {
                continue;
            }

            var confidence = double.Parse(columns[10], CultureInfo.InvariantCulture);
            var text = columns[11];
            if (confidence < 0 || string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            words.Add(new TsvWord(
                Int(columns[2]), Int(columns[3]), Int(columns[4]), Int(columns[5]),
                left, top, width, height, confidence, text));
        }

        return new TsvPage(pageWidth, pageHeight, words);
    }

    private static int Int(string s) => int.Parse(s, CultureInfo.InvariantCulture);
}
