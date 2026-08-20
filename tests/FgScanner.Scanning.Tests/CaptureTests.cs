using System.Drawing;
using FgScanner.Core.Capture;
using FgScanner.Scanning.Capture;
using Xunit;

namespace FgScanner.Scanning.Tests;

public sealed class CaptureTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "fgscanner-tests", Guid.NewGuid().ToString("N"));

    public CaptureTests() => Directory.CreateDirectory(_root);

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

    private string CreateWhitePage(string name = "white.png", int width = 600, int height = 800)
    {
        using var bitmap = new Bitmap(width, height);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(Color.White);
        }

        var path = Path.Combine(_root, name);
        bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png);
        return path;
    }

    private string CreateSeparatorSheet(string name = "separator.png")
    {
        var path = Path.Combine(_root, name);
        SeparatorSheet.CreatePng(path);
        return path;
    }

    // ---- Patch-T ----

    [Fact]
    public void Generated_separator_sheet_round_trips_through_the_detector()
    {
        var sheet = CreateSeparatorSheet();

        Assert.True(new PatchTDetector().IsSeparator(sheet));
    }

    [Fact]
    public void Text_page_is_not_a_separator()
    {
        var page = TestImages.CreateLinedPage(_root);

        Assert.False(new PatchTDetector().IsSeparator(page));
    }

    [Fact]
    public void Oversized_sheet_still_decodes_after_downscaling()
    {
        var sheet = CreateSeparatorSheet();
        // Simulate a 300-DPI scan of the sheet: upscale well past the decode width cap.
        var big = Path.Combine(_root, "big.png");
        using (var original = new Bitmap(sheet))
        using (var scaled = new Bitmap(original, original.Width * 2, original.Height * 2))
        {
            scaled.Save(big, System.Drawing.Imaging.ImageFormat.Png);
        }

        Assert.True(new PatchTDetector().IsSeparator(big));
    }

    // ---- blank detection ----

    [Fact]
    public void White_page_is_blank()
    {
        Assert.True(new BlankPageDetector().IsBlank(CreateWhitePage()));
    }

    [Fact]
    public void Lined_page_is_not_blank()
    {
        Assert.False(new BlankPageDetector().IsBlank(TestImages.CreateLinedPage(_root)));
    }

    [Fact]
    public void Edge_shadow_inside_the_margin_stays_blank()
    {
        var path = Path.Combine(_root, "shadow.png");
        using (var bitmap = new Bitmap(600, 800))
        {
            using var graphics = Graphics.FromImage(bitmap);
            graphics.Clear(Color.White);
            // A feeder shadow strip hugging the left edge, inside the 3% ignore margin.
            graphics.FillRectangle(Brushes.Black, 0, 0, 10, 800);
            bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png);
        }

        Assert.True(new BlankPageDetector().IsBlank(path));
    }

    // ---- classifier ----

    [Fact]
    public void Classifier_honors_the_policy_switches()
    {
        var classifier = new PageClassifier();
        var white = CreateWhitePage();
        var sheet = CreateSeparatorSheet();
        var text = TestImages.CreateLinedPage(_root);

        Assert.Equal(PageKind.Content, classifier.Classify(white, CapturePolicy.Off));
        Assert.Equal(PageKind.Content, classifier.Classify(sheet, CapturePolicy.Off));

        var active = new CapturePolicy(DetectSeparators: true, KeepSeparatorPages: false, BlankPagePolicy.Flag);
        Assert.Equal(PageKind.Blank, classifier.Classify(white, active));
        Assert.Equal(PageKind.Separator, classifier.Classify(sheet, active));
        Assert.Equal(PageKind.Content, classifier.Classify(text, active));

        var blankAsSeparator = active with { BlankPolicy = BlankPagePolicy.Separator };
        Assert.Equal(PageKind.Separator, classifier.Classify(white, blankAsSeparator));
    }
}
