using FgScanner.Scanning.Editing;
using NAPS2.Images.Transforms;
using Xunit;

namespace FgScanner.Scanning.Tests;

public sealed class ImageEditorTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("fgs-edit").FullName;
    private readonly ImageEditor _editor = new(TestImages.Context);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Rotate_90_swaps_dimensions()
    {
        var path = TestImages.CreateLinedPage(_dir, width: 600, height: 800);

        await _editor.ApplyAsync(path, new PageEdit.Rotate(90), Ct);

        Assert.Equal((800, 600), TestImages.GetSize(path));
    }

    [Fact]
    public async Task Rotate_180_twice_round_trips_within_tolerance()
    {
        var path = TestImages.CreateLinedPage(_dir);
        var reference = TestImages.CreateLinedPage(_dir, "reference.png");

        await _editor.ApplyAsync(path, new PageEdit.Rotate(180), Ct);
        await _editor.ApplyAsync(path, new PageEdit.Rotate(180), Ct);

        var diff = TestImages.MeanPixelDifference(path, reference);
        Assert.True(diff < 8.0, $"mean diff {diff}");
    }

    [Fact]
    public async Task Crop_trims_the_requested_pixels()
    {
        var path = TestImages.CreateLinedPage(_dir, width: 600, height: 800);

        await _editor.ApplyAsync(path, new PageEdit.Crop(Left: 10, Top: 20, Right: 30, Bottom: 40), Ct);

        Assert.Equal((560, 740), TestImages.GetSize(path));
    }

    [Fact]
    public async Task Brightness_lightens_a_gray_image()
    {
        var path = TestImages.CreateSolidPage(_dir, "gray.png", System.Drawing.Color.FromArgb(100, 100, 100));

        await _editor.ApplyAsync(path, new PageEdit.Brightness(500), Ct);

        Assert.True(TestImages.GetPixel(path, 50, 50).R > 150);
    }

    [Fact]
    public async Task BlackWhite_binarizes()
    {
        var path = TestImages.CreateLinedPage(_dir);

        await _editor.ApplyAsync(path, new PageEdit.BlackWhite(0), Ct);

        var linePixel = TestImages.GetPixel(path, 300, 66);
        var ground = TestImages.GetPixel(path, 300, 30);
        Assert.True(linePixel.R < 20, $"ink should stay black, was {linePixel}");
        Assert.True(ground.R > 235, $"ground should stay white, was {ground}");
    }

    [Fact]
    public async Task Deskew_straightens_a_skewed_page()
    {
        var path = TestImages.CreateLinedPage(_dir, width: 800, height: 1000);
        using (var image = TestImages.Context.Load(path))
        using (var skewed = TestImages.Context.PerformTransform(image, new RotationTransform(3.0)))
        {
            skewed.Save(path);
        }

        using (var check = TestImages.Context.Load(path))
        {
            var est = SkewEstimator.EstimateSkewDegrees(check);
            Assert.True(Math.Abs(est - 3.0) < 0.5, $"estimated {est}");
        }

        await _editor.ApplyAsync(path, new PageEdit.Deskew(), Ct);

        using var straightened = TestImages.Context.Load(path);
        var residual = SkewEstimator.EstimateSkewDegrees(straightened);
        Assert.True(Math.Abs(residual) < 0.5, $"residual {residual}");
    }

    [Fact]
    public async Task Split_vertical_halves_and_second_half_lands_separately()
    {
        var path = TestImages.CreateLinedPage(_dir, width: 600, height: 800);
        var secondPath = Path.Combine(_dir, "second.png");

        await _editor.SplitAsync(path, vertical: true, secondPath, Ct);

        Assert.Equal((300, 800), TestImages.GetSize(path));
        Assert.Equal((300, 800), TestImages.GetSize(secondPath));
    }

    [Fact]
    public async Task Combine_reassembles_split_halves()
    {
        var path = TestImages.CreateLinedPage(_dir, width: 600, height: 800);
        var secondPath = Path.Combine(_dir, "second.png");
        await _editor.SplitAsync(path, vertical: true, secondPath, Ct);

        await _editor.CombineAsync(path, secondPath, vertical: false, Ct);

        Assert.Equal((600, 800), TestImages.GetSize(path));
    }

    [Fact]
    public async Task Edits_are_atomic_no_temp_files_remain()
    {
        var path = TestImages.CreateLinedPage(_dir);

        await _editor.ApplyAsync(path, new PageEdit.Sharpen(500), Ct);

        Assert.Empty(Directory.GetFiles(_dir, "*.tmp"));
    }
}
