using NAPS2.Images;
using NAPS2.Scan;
using Xunit;

namespace FgScanner.Scanning.Tests;

/// <summary>
/// What the operator's profile becomes by the time it reaches the driver. ScanProfileOptions is on
/// the legal-evidence capture path, so a new property that changed any existing capture would
/// change every group scanned since — each one already committed, exported and handed to the
/// portal. Every addition defaults to today's behaviour (SPEC-2026-006 §16 R2).
/// </summary>
public class ScanOptionMappingTests
{
    private static readonly ScanDeviceInfo AnyDevice = new(ScanDriver.Wia, "wia-1", "A scanner");

    [Fact]
    public void Duplex_and_flip_reach_the_scan_options()
    {
        var mapped = Naps2ScanService.BuildOptions(new ScanProfileOptions
        {
            Device = AnyDevice,
            Source = ScanSource.Duplex,
            FlipDuplexedPages = true,
        });

        Assert.Equal(PaperSource.Duplex, mapped.PaperSource);
        Assert.True(mapped.FlipDuplexedPages);
    }

    /// <summary>
    /// Flipping is a correction for scanners that hand back upside-down backs, not a default. A
    /// scanner that already orients them correctly would have every back rotated 180 degrees.
    /// </summary>
    [Fact]
    public void Flip_is_off_unless_it_is_asked_for()
    {
        var mapped = Naps2ScanService.BuildOptions(new ScanProfileOptions
        {
            Device = AnyDevice,
            Source = ScanSource.Duplex,
        });

        Assert.Equal(PaperSource.Duplex, mapped.PaperSource);
        Assert.False(mapped.FlipDuplexedPages);
    }

    /// <summary>
    /// The evidence constraint, as an assertion. Every value a default capture sends to the driver,
    /// written out: if a future property changes one of these, this test says so rather than the
    /// next box of exhibits saying it.
    /// </summary>
    [Fact]
    public void A_default_capture_sends_exactly_what_it_sent_before()
    {
        var mapped = Naps2ScanService.BuildOptions(new ScanProfileOptions { Device = AnyDevice });

        Assert.Equal(PaperSource.Flatbed, mapped.PaperSource);
        Assert.Equal(300, mapped.Dpi);
        Assert.Equal(BitDepth.Color, mapped.BitDepth);
        Assert.Equal(PageSize.Letter, mapped.PageSize);
        Assert.Equal(0, mapped.Brightness);
        Assert.Equal(0, mapped.Contrast);
        Assert.False(mapped.FlipDuplexedPages);
    }

    [Theory]
    [InlineData(ScanSource.Flatbed)]
    [InlineData(ScanSource.Feeder)]
    public void Flip_is_never_sent_for_a_single_sided_source(ScanSource source)
    {
        var mapped = Naps2ScanService.BuildOptions(new ScanProfileOptions
        {
            Device = AnyDevice,
            Source = source,
            FlipDuplexedPages = true,
        });

        Assert.False(mapped.FlipDuplexedPages);
    }
}
