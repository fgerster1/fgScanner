using FgScanner.App.Views;
using Xunit;

namespace FgScanner.App.Tests;

/// <summary>
/// The shell used to open at a fixed 1200x760 — taller than a 1366x768 laptop can show once the
/// taskbar is counted, with nothing on screen to reach what fell off.
/// </summary>
public sealed class WindowSizingTests
{
    [Fact]
    public void A_window_that_fits_keeps_its_size_and_is_centred()
    {
        var bounds = WindowSizing.FitToWorkArea(1200, 760, new WindowBounds(0, 0, 1920, 1040));

        Assert.Equal(new WindowBounds(360, 140, 1200, 760), bounds);
    }

    [Fact]
    public void A_window_bigger_than_the_screen_shrinks_to_the_work_area()
    {
        // 1366x768 at 125% scaling leaves roughly 1093x614 layout units above the taskbar.
        var bounds = WindowSizing.FitToWorkArea(1200, 760, new WindowBounds(0, 0, 1093, 614));

        Assert.Equal(new WindowBounds(0, 0, 1093, 614), bounds);
    }

    [Fact]
    public void A_window_on_a_second_monitor_stays_inside_that_monitor()
    {
        var bounds = WindowSizing.FitToWorkArea(1200, 760, new WindowBounds(1920, 0, 1366, 728));

        Assert.Equal(new WindowBounds(2003, 0, 1200, 728), bounds);
    }

    [Fact]
    public void A_saved_preview_width_leaves_the_grid_its_minimum()
    {
        // Saved on a big monitor, restored where only 1000 units are available: the grid keeps 240
        // and the splitter 6, so the preview gets at most 754.
        Assert.Equal(754, WindowSizing.ClampPanel(desired: 900, available: 1000, otherMinimum: 240, splitter: 6, panelMinimum: 200));
    }

    [Fact]
    public void A_preview_never_shrinks_below_its_own_minimum()
    {
        Assert.Equal(200, WindowSizing.ClampPanel(desired: 900, available: 300, otherMinimum: 240, splitter: 6, panelMinimum: 200));
    }

    [Fact]
    public void A_preview_width_that_fits_is_kept()
    {
        Assert.Equal(300, WindowSizing.ClampPanel(desired: 300, available: 1000, otherMinimum: 240, splitter: 6, panelMinimum: 200));
    }

    [Fact]
    public void Before_layout_the_saved_width_is_kept()
    {
        // A panel not yet laid out reports zero; clamping against that would throw the saved size away.
        Assert.Equal(900, WindowSizing.ClampPanel(desired: 900, available: 0, otherMinimum: 240, splitter: 6, panelMinimum: 200));
    }
}

/// <summary>
/// How big a section's content is inside its scroll host. Sized from the host's own size, never the
/// viewport: a scroll bar that shrinks the viewport would otherwise change the input that decided
/// whether the bar was needed, and the bars flicker at the boundary.
/// </summary>
public sealed class ScrollFillTests
{
    private const double Bar = 17;

    [Fact]
    public void A_host_bigger_than_the_minimum_fills_exactly()
    {
        Assert.Equal(new ContentSize(1200, 800), ScrollFill.Resolve(1200, 800, 760, 520, Bar, Bar));
    }

    [Fact]
    public void A_narrow_host_scrolls_sideways_and_the_bar_takes_height()
    {
        Assert.Equal(new ContentSize(760, 783), ScrollFill.Resolve(700, 800, 760, 520, Bar, Bar));
    }

    [Fact]
    public void A_short_host_scrolls_down_and_the_bar_takes_width()
    {
        Assert.Equal(new ContentSize(1183, 520), ScrollFill.Resolve(1200, 500, 760, 520, Bar, Bar));
    }

    [Fact]
    public void A_vertical_bar_that_pushes_the_width_under_the_minimum_brings_the_horizontal_bar()
    {
        // 770 is wide enough on its own, but the vertical bar leaves 753 — under 760.
        Assert.Equal(new ContentSize(760, 520), ScrollFill.Resolve(770, 510, 760, 520, Bar, Bar));
    }

    [Fact]
    public void Unbounded_height_always_reserves_the_vertical_bar_and_leaves_height_to_the_content()
    {
        // Settings is a long page: its height is its content, so the vertical bar is shown throughout
        // and the width never depends on whether the content happens to overflow.
        var size = ScrollFill.Resolve(1000, 600, 700, double.NaN, Bar, Bar);

        Assert.Equal(983, size.Width);
        Assert.True(double.IsNaN(size.Height));
    }
}
