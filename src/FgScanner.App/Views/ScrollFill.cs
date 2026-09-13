namespace FgScanner.App.Views;

/// <summary>A content size; a NaN height means the content decides its own height.</summary>
public readonly record struct ContentSize(double Width, double Height);

/// <summary>
/// How big a section's content should be inside its scroll host: exactly the space available, so
/// star-sized rows and virtualizing grids behave as if nothing wrapped them, but never below the
/// section's minimum, which is where scroll bars take over.
///
/// Decided from the host's own size and never from the viewport. A bar appearing shrinks the
/// viewport, and feeding that back in could remove the need for the bar — the bars then flicker on
/// and off at the boundary.
/// </summary>
public static class ScrollFill
{
    public static ContentSize Resolve(
        double hostWidth, double hostHeight, double minWidth, double minHeight, double verticalBarWidth, double horizontalBarHeight)
    {
        // A long page (Settings) is as tall as its content, so its vertical bar is reserved
        // permanently; otherwise the width would depend on whether the content happened to overflow.
        if (double.IsNaN(minHeight))
        {
            return new ContentSize(Math.Max(minWidth, hostWidth - verticalBarWidth), double.NaN);
        }

        var horizontal = hostWidth < minWidth;
        var vertical = hostHeight - (horizontal ? horizontalBarHeight : 0) < minHeight;

        // A vertical bar can take enough width to need the horizontal bar after all. One more pass
        // settles it: each flag can only turn on, and the second pass sees the other's final value.
        horizontal = hostWidth - (vertical ? verticalBarWidth : 0) < minWidth;
        vertical = hostHeight - (horizontal ? horizontalBarHeight : 0) < minHeight;

        return new ContentSize(
            Math.Max(minWidth, hostWidth - (vertical ? verticalBarWidth : 0)),
            Math.Max(minHeight, hostHeight - (horizontal ? horizontalBarHeight : 0)));
    }
}
