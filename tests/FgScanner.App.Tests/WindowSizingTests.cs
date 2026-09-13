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
    public void A_dialog_already_on_screen_is_left_where_it_is()
    {
        var placed = WindowSizing.KeepInside(new WindowBounds(300, 200, 440, 420), new WindowBounds(0, 0, 1280, 976));

        Assert.Equal(new WindowBounds(300, 200, 440, 420), placed);
    }

    [Fact]
    public void A_dialog_centred_over_a_window_near_the_bottom_is_pulled_up_to_show_its_buttons()
    {
        // Centred on an owner dragged low, the dialog's button row would sit below the taskbar.
        var placed = WindowSizing.KeepInside(new WindowBounds(300, 700, 440, 420), new WindowBounds(0, 0, 1280, 976));

        Assert.Equal(new WindowBounds(300, 556, 440, 420), placed);
    }

    [Fact]
    public void A_dialog_off_the_left_edge_of_a_second_monitor_is_pulled_back_onto_it()
    {
        var placed = WindowSizing.KeepInside(new WindowBounds(1800, 100, 440, 420), new WindowBounds(1920, 0, 1366, 728));

        Assert.Equal(new WindowBounds(1920, 100, 440, 420), placed);
    }

    [Fact]
    public void A_dialog_bigger_than_the_screen_is_shrunk_to_it_and_pinned_to_its_corner()
    {
        // The 1000x820 page viewer on a screen with 781 units above the taskbar.
        var placed = WindowSizing.KeepInside(new WindowBounds(100, 50, 1000, 820), new WindowBounds(0, 0, 1024, 781));

        Assert.Equal(new WindowBounds(24, 0, 1000, 781), placed);
    }

    [Fact]
    public void A_dialog_as_big_as_a_work_area_with_fractional_edges_is_placed_without_throwing()
    {
        // A 40-pixel taskbar docked on top at 150% scaling puts the work area's edge at 26.67 units.
        // With the dialog capped to that area, edge + size − size lands one floating-point step short of
        // the edge, and a clamp whose bounds cross throws out of ContentRendered and ends the app.
        var area = new WindowBounds(26.666666666666664, 26.666666666666664, 666.6666666666666, 666.6666666666666);

        var placed = WindowSizing.KeepInside(new WindowBounds(100, 50, 666.6666666666666, 666.6666666666666), area);

        Assert.Equal(area, placed);
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
