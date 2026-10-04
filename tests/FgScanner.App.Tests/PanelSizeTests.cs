using FgScanner.App.Views;
using Xunit;

namespace FgScanner.App.Tests;

/// <summary>
/// SPEC-2026-009 AC-9. A remembered pane width is a plain number in the Settings table; a bad
/// value must never wedge the layout, so anything unusable falls back to the design size.
/// </summary>
public sealed class PanelSizeTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    public void An_unusable_stored_width_falls_back_to_the_design_size(string? stored) =>
        Assert.Equal(280, PanelSize.Read(stored, fallback: 280, minimum: 200));

    [Fact]
    public void A_stored_width_below_the_minimum_falls_back_to_the_design_size() =>
        Assert.Equal(280, PanelSize.Read("50", fallback: 280, minimum: 200));

    [Fact]
    public void A_usable_stored_width_is_read_in_invariant_culture() =>
        Assert.Equal(312.5, PanelSize.Read("312.5", fallback: 280, minimum: 200));

    [Fact]
    public void A_width_is_written_as_a_whole_invariant_number() =>
        Assert.Equal("313", PanelSize.Format(312.5));
}
