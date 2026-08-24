using FgScanner.Scanning;
using Xunit;

namespace FgScanner.Scanning.Tests;

/// <summary>
/// Regression cover for BUG-1 (docs/manual-tests.md): the first page of a TWAIN run arrived with
/// GDI's 96 DPI default stamped on 300 DPI pixels, which misfeeds Tesseract's --dpi and mis-sizes
/// exported PDF pages.
/// </summary>
public sealed class ScanResolutionPolicyTests
{
    [Theory]
    [InlineData(96f, 300)]  // the exact observed failure: page 1 of a 300 DPI TWAIN run
    [InlineData(96f, 150)]
    [InlineData(96f, 600)]
    public void StampsRequestedDpi_WhenDriverReportsTheGdiDefault(float reported, int requested)
    {
        Assert.Equal(requested, ScanResolutionPolicy.ResolveDpiToStamp(reported, requested));
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(-1f)]
    public void StampsRequestedDpi_WhenDriverReportsNothingAtAll(float reported)
    {
        Assert.Equal(300f, ScanResolutionPolicy.ResolveDpiToStamp(reported, 300));
    }

    [Theory]
    [InlineData(300f, 300)]  // driver agrees with the request
    [InlineData(600f, 1200)] // driver clamped to a supported DPI — that is the truth, keep it
    [InlineData(200f, 300)]
    public void KeepsDriverResolution_WhenItLooksDeliberate(float reported, int requested)
    {
        Assert.Null(ScanResolutionPolicy.ResolveDpiToStamp(reported, requested));
    }

    [Fact]
    public void KeepsDriverResolution_WhenNinetySixWasActuallyRequested()
    {
        // 96 is a legitimate scan resolution; it is only suspicious when we asked for something else.
        Assert.Null(ScanResolutionPolicy.ResolveDpiToStamp(96f, 96));
    }

    [Fact]
    public void KeepsDriverResolution_WhenNoValidDpiWasRequested()
    {
        Assert.Null(ScanResolutionPolicy.ResolveDpiToStamp(96f, 0));
    }
}
