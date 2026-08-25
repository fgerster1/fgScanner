using System.Drawing;
using System.Drawing.Imaging;
using FgScanner.Core.Duplicates;
using FgScanner.Scanning.Capture;
using Xunit;

namespace FgScanner.Scanning.Tests;

/// <summary>
/// The two hand-rolled similarity measures behind duplicate detection. Both are hand-rolled because
/// ImageSharp and Emgu.CV — the obvious libraries — are on the CLAUDE.md forbidden list.
/// </summary>
public sealed class DuplicateDetectionTests : IDisposable
{
    /// <summary>A well-formed all-zero hash, for pairing against malformed input.</summary>
    private const string Zeroes = "0000000000000000000000000000000000000000000000000000000000000000";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "fgscanner-tests", Guid.NewGuid().ToString("N"));

    public DuplicateDetectionTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private static readonly string[] Words =
        ("invoice total distributor flame thrower vacuum mechanical advance carbureted summit racing "
        + "equipment tallmadge ohio shipping handling taxes cores oversize hazardous ignition coil "
        + "canister round epoxy black park lamp assembly clear lens value line right side front "
        + "chevrolet gmc each selected shipped customer number order date extended price").Split(' ');

    /// <summary>
    /// A realistic page: rendered text, not solid bars. An earlier version of this fixture drew
    /// black rectangles, which produce far fewer gradient transitions than text and made unrelated
    /// pages look near-identical to the hasher — a property of the fixture, not of the algorithm.
    /// </summary>
    private static Bitmap RenderPage(int seed)
    {
        var bitmap = new Bitmap(850, 1100);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.Clear(Color.White);
        graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
        var random = new Random(seed);
        using var font = new Font("Arial", 9f);
        for (var y = 30; y < 1070; y += 18)
        {
            var line = string.Join(
                ' ', Enumerable.Range(0, random.Next(6, 12)).Select(_ => Words[random.Next(Words.Length)]));
            graphics.DrawString(line, font, Brushes.Black, random.Next(20, 50), y);
        }

        return bitmap;
    }

    private string Save(Bitmap bitmap, string name, int quality)
    {
        var path = Path.Combine(_root, name);
        var encoder = ImageCodecInfo.GetImageEncoders().First(e => e.MimeType == "image/jpeg");
        using var parameters = new EncoderParameters(1);
        using var setting = new EncoderParameter(Encoder.Quality, (long)quality);
        parameters.Param[0] = setting;
        bitmap.Save(path, encoder, parameters);
        return path;
    }

    /// <summary>
    /// The SAME image resampled and recompressed — what re-scanning one sheet at another DPI looks
    /// like. Re-rendering onto a smaller canvas instead fits fewer lines and quietly compares two
    /// genuinely different documents, which is how an earlier version of this test lied.
    /// </summary>
    private string SaveRescaled(Bitmap source, string name, double factor, int quality)
    {
        using var scaled = new Bitmap((int)(source.Width * factor), (int)(source.Height * factor));
        using (var graphics = Graphics.FromImage(scaled))
        {
            graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            graphics.DrawImage(source, 0, 0, scaled.Width, scaled.Height);
        }

        return Save(scaled, name, quality);
    }

    [Fact]
    public void A_rescanned_page_lands_above_the_threshold_and_a_different_page_below_it()
    {
        // The one property that matters: the threshold separates the two populations. Measured over
        // 12 pairs, same-page scored 95.3% at worst and different-page 91.8% at best.
        for (var seed = 1; seed <= 6; seed++)
        {
            using var page = RenderPage(seed);
            using var unrelated = RenderPage(seed + 500);

            var original = ImageHasher.Compute(Save(page, $"a{seed}.jpg", 90));
            var rescanned = ImageHasher.Compute(SaveRescaled(page, $"b{seed}.jpg", 0.75, 55));
            var other = ImageHasher.Compute(Save(unrelated, $"c{seed}.jpg", 90));

            var same = ImageHasher.Compare(original, rescanned)!.Value;
            var different = ImageHasher.Compare(original, other)!.Value;

            Assert.True(same >= ImageHasher.DefaultThreshold, $"seed {seed}: same page scored {same:P1}");
            Assert.True(
                different < ImageHasher.DefaultThreshold, $"seed {seed}: different pages scored {different:P1}");
        }
    }

    [Fact]
    public void Unrelated_document_scans_still_agree_on_most_bits()
    {
        // Documents are all "dark text on white", so the floor is high. This is why an 80% threshold
        // would report every page as a duplicate of every other, and why the default is 93%.
        using var one = RenderPage(1);
        using var other = RenderPage(2);

        var similarity = ImageHasher.Compare(
            ImageHasher.Compute(Save(one, "f1.jpg", 90)),
            ImageHasher.Compute(Save(other, "f2.jpg", 90)))!.Value;

        Assert.True(similarity > 0.80, $"expected the floor to sit above 80%, got {similarity:P1}");
    }

    [Fact]
    public void An_identical_file_scores_a_perfect_match()
    {
        using var page = RenderPage(4);
        var hash = ImageHasher.Compute(Save(page, "same.jpg", 90));

        Assert.Equal(1.0, ImageHasher.Compare(hash, hash));
    }

    [Fact]
    public void A_hash_is_stable_across_calls()
    {
        using var page = RenderPage(5);
        var path = Save(page, "stable.jpg", 90);

        Assert.Equal(ImageHasher.Compute(path), ImageHasher.Compute(path));
    }

    [Theory]
    [InlineData(null, Zeroes)]
    [InlineData(Zeroes, null)]
    [InlineData("not-a-hash", Zeroes)]
    [InlineData("00ff", Zeroes)] // right length matters: a truncated hash is not a weak match
    public void An_unusable_hash_compares_to_null_rather_than_zero(string? left, string? right)
    {
        // Null means "cannot say". Returning 0 would read as "definitely different".
        Assert.Null(ImageHasher.Compare(left, right));
    }

    [Fact]
    public void Ocr_of_the_same_page_twice_scores_above_the_threshold()
    {
        const string first =
            "Summit Racing Equipment 1200 Southeast Ave Tallmadge Ohio invoice total 208.60 " +
            "distributor flame thrower vacuum mechanical advance carbureted";
        // The same page re-OCRed: a few characters misread, one token dropped.
        const string second =
            "Summit Racing Equipment 12OO Southeast Ave Tallmadge Ohio invoice total 208.60 " +
            "distributor flame thrower vacuum mechanical advance";

        var similarity = TextSimilarity.Compare(first, second);

        Assert.NotNull(similarity);
        Assert.True(similarity >= 0.80, $"expected a high score for the same page, got {similarity:P0}");
    }

    [Fact]
    public void Two_unrelated_documents_score_low()
    {
        var similarity = TextSimilarity.Compare(
            "Summit Racing Equipment invoice total distributor flame thrower vacuum advance",
            "Windows printer test page driver version spooler subsystem application succeeded");

        Assert.NotNull(similarity);
        Assert.True(similarity < 0.30, $"expected a low score, got {similarity:P0}");
    }

    [Fact]
    public void Identical_text_scores_one()
    {
        const string text = "alpha bravo charlie delta echo foxtrot golf hotel india juliet kilo";

        Assert.Equal(1.0, TextSimilarity.Compare(text, text));
    }

    [Theory]
    [InlineData("too short", "too short")]
    [InlineData("", "")]
    [InlineData(null, null)]
    public void Short_or_empty_text_is_not_judged_at_all(string? left, string? right)
    {
        // Two nearly-empty pages share their handful of tokens and would otherwise score 1.0,
        // reporting every blank page as a duplicate of every other.
        Assert.Null(TextSimilarity.Compare(left, right));
    }

    [Fact]
    public void Punctuation_and_case_do_not_affect_the_score()
    {
        var similarity = TextSimilarity.Compare(
            "Alpha, bravo. Charlie; delta! Echo? Foxtrot golf hotel india juliet kilo",
            "alpha bravo charlie delta echo foxtrot golf hotel india juliet kilo");

        Assert.Equal(1.0, similarity);
    }
}
