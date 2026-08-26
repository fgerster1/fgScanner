using FgScanner.App.Views;
using Xunit;

namespace FgScanner.App.Tests;

/// <summary>
/// The arithmetic behind the page viewer, kept out of the window so it can be checked without a
/// UI. Zoom limits and page navigation are exactly the parts that go wrong silently — an
/// off-by-one at the end of a group, or a scale that runs away on a fast scroll wheel.
/// </summary>
public sealed class ZoomControllerTests
{
    [Fact]
    public void Starts_at_actual_size()
    {
        Assert.Equal(1.0, new ZoomController().Scale);
    }

    [Fact]
    public void Zooming_in_and_back_out_returns_to_where_it_started()
    {
        var zoom = new ZoomController();

        zoom.In();
        zoom.Out();

        Assert.Equal(1.0, zoom.Scale, 6);
    }

    [Fact]
    public void Zooming_in_stops_at_the_maximum()
    {
        var zoom = new ZoomController();

        for (var i = 0; i < 100; i++)
        {
            zoom.In();
        }

        Assert.Equal(ZoomController.Maximum, zoom.Scale);
    }

    [Fact]
    public void Zooming_out_stops_at_the_minimum()
    {
        var zoom = new ZoomController();

        for (var i = 0; i < 100; i++)
        {
            zoom.Out();
        }

        Assert.Equal(ZoomController.Minimum, zoom.Scale);
    }

    [Fact]
    public void Fitting_a_tall_page_uses_the_dimension_that_runs_out_first()
    {
        var zoom = new ZoomController();

        // A 300-DPI portrait page in the short preview panel: height is the binding constraint,
        // and the scale it needs there is well below any comfortable-looking floor.
        zoom.Fit(contentWidth: 2550, contentHeight: 3300, viewportWidth: 1000, viewportHeight: 660);

        Assert.Equal(0.2, zoom.Scale, 6);
        Assert.True(zoom.Scale >= ZoomController.Minimum, "Fit must stay inside the zoom limits");
    }

    [Fact]
    public void Fitting_a_wide_page_uses_the_width()
    {
        var zoom = new ZoomController();

        zoom.Fit(contentWidth: 2000, contentHeight: 500, viewportWidth: 500, viewportHeight: 1000);

        Assert.Equal(0.25, zoom.Scale, 6);
    }

    [Fact]
    public void Fitting_never_enlarges_a_page_that_already_fits()
    {
        // Blowing a small image up to fill the window makes it blurry and tells the user nothing.
        var zoom = new ZoomController();

        zoom.Fit(contentWidth: 100, contentHeight: 100, viewportWidth: 1000, viewportHeight: 1000);

        Assert.Equal(1.0, zoom.Scale);
    }

    [Theory]
    [InlineData(0, 100)]
    [InlineData(100, 0)]
    [InlineData(-5, 100)]
    [InlineData(double.NaN, 100)]
    public void Fitting_against_a_viewport_with_no_size_leaves_the_scale_alone(double width, double height)
    {
        // A window still being laid out reports zero. Dividing by it would produce infinity and
        // hand the layout an image of unbounded size.
        var zoom = new ZoomController();
        zoom.In();
        var before = zoom.Scale;

        zoom.Fit(2550, 3300, width, height);

        Assert.Equal(before, zoom.Scale);
    }

    [Fact]
    public void Resetting_returns_to_actual_size()
    {
        var zoom = new ZoomController();
        zoom.In();
        zoom.In();

        zoom.Reset();

        Assert.Equal(1.0, zoom.Scale);
    }
}

public sealed class PageNavigatorTests
{
    [Fact]
    public void Opens_on_the_page_that_was_double_clicked()
    {
        Assert.Equal(2, new PageNavigator(5, 2).Index);
    }

    [Fact]
    public void Next_advances_one_page()
    {
        var nav = new PageNavigator(5, 0);

        nav.Next();

        Assert.Equal(1, nav.Index);
    }

    [Fact]
    public void Next_stops_at_the_last_page()
    {
        var nav = new PageNavigator(3, 2);

        nav.Next();

        Assert.Equal(2, nav.Index);
        Assert.False(nav.CanGoNext);
    }

    [Fact]
    public void Previous_stops_at_the_first_page()
    {
        var nav = new PageNavigator(3, 0);

        nav.Previous();

        Assert.Equal(0, nav.Index);
        Assert.False(nav.CanGoPrevious);
    }

    [Fact]
    public void First_and_last_jump_to_the_ends()
    {
        var nav = new PageNavigator(10, 4);

        nav.Last();
        Assert.Equal(9, nav.Index);

        nav.First();
        Assert.Equal(0, nav.Index);
    }

    [Fact]
    public void A_single_page_group_can_go_nowhere()
    {
        var nav = new PageNavigator(1, 0);

        Assert.False(nav.CanGoNext);
        Assert.False(nav.CanGoPrevious);
    }

    [Fact]
    public void The_position_reads_from_one_not_zero()
    {
        Assert.Equal("Page 3 of 7", new PageNavigator(7, 2).Position);
    }

    [Theory]
    [InlineData(5, -1, 0)]
    [InlineData(5, 99, 4)]
    public void A_start_index_outside_the_group_is_pulled_back_inside(int count, int start, int expected)
    {
        // The grid can be re-sorted or a row deleted between opening and rendering.
        Assert.Equal(expected, new PageNavigator(count, start).Index);
    }

    [Fact]
    public void An_empty_group_reports_no_position_rather_than_page_one_of_zero()
    {
        var nav = new PageNavigator(0, 0);

        Assert.Equal("", nav.Position);
        Assert.False(nav.CanGoNext);
        Assert.False(nav.CanGoPrevious);
    }
}
