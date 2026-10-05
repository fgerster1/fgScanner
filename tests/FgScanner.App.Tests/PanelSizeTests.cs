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

    [Fact]
    public void Side_panes_that_fit_are_not_limited()
    {
        var (first, second) = PanelSize.Fit(room: 1010, first: 280, second: 340,
            firstMin: 200, secondMin: 300, between: 312);

        Assert.Equal(double.PositiveInfinity, first);
        Assert.Equal(double.PositiveInfinity, second);
    }

    /// <summary>Widths saved maximized, then the window reopens at 1010: 400 + 480 + 312 is 182 too
    /// many, taken from each pane in proportion to what it has above its minimum.</summary>
    [Fact]
    public void Side_panes_wider_than_the_room_are_shrunk_to_fit_it()
    {
        var (first, second) = PanelSize.Fit(room: 1010, first: 400, second: 480,
            firstMin: 200, secondMin: 300, between: 312);

        Assert.Equal(1010, first + second + 312, precision: 6);
        Assert.InRange(first, 200, 400);
        Assert.InRange(second, 300, 480);
        Assert.Equal((400 - first) / 200, (480 - second) / 180, precision: 6);
    }

    [Fact]
    public void A_room_smaller_than_every_minimum_leaves_the_panes_at_their_minimums()
    {
        var (first, second) = PanelSize.Fit(room: 600, first: 400, second: 480,
            firstMin: 200, secondMin: 300, between: 312);

        Assert.Equal((200d, 300d), (first, second));
    }

    [Fact]
    public void A_single_side_pane_is_limited_the_same_way()
    {
        var (first, _) = PanelSize.Fit(room: 1010, first: 1000, second: 0,
            firstMin: 200, secondMin: 0, between: 452);

        Assert.Equal(558, first, precision: 6);
    }

    [Fact]
    public void A_view_not_yet_laid_out_limits_nothing()
    {
        var (first, second) = PanelSize.Fit(room: 0, first: 400, second: 480,
            firstMin: 200, secondMin: 300, between: 312);

        Assert.Equal((double.PositiveInfinity, double.PositiveInfinity), (first, second));
    }
}
