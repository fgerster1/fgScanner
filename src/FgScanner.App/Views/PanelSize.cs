using System.Globalization;

namespace FgScanner.App.Views;

/// <summary>
/// A pane width or height the user dragged, as stored in the Settings table. A panel that snaps
/// back on the next launch has not really been made resizable — but a bad stored value must not
/// wedge the layout either, so anything unparseable or below the minimum reads as the design size.
/// There is no upper bound here: each view limits what is shown to the room it has, so a size
/// saved on a large monitor survives a session on a small one.
/// </summary>
public static class PanelSize
{
    public static double Read(string? stored, double fallback, double minimum) =>
        double.TryParse(stored, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
        && double.IsFinite(value) && value >= minimum
            ? value
            : fallback;

    public static string Format(double size) => size.ToString("0", CultureInfo.InvariantCulture);

    /// <summary>
    /// The widest each side pane may be shown so both fit the room beside <paramref name="between"/>
    /// (the middle pane's minimum and the splitters); infinity where nothing needs limiting. Worked
    /// from the widths Jim chose, never the laid-out ones: a pane already cut off reports its cut
    /// width, and limiting against that limits nothing. Any overflow is taken from each pane in
    /// proportion to what it has above its minimum. Pass a zero second pane when there is only one.
    /// </summary>
    public static (double First, double Second) Fit(
        double room, double first, double second, double firstMin, double secondMin, double between)
    {
        var over = first + second + between - room;
        if (!(room > 0) || over <= 0)
        {
            return (double.PositiveInfinity, double.PositiveInfinity);
        }

        var firstSlack = Math.Max(0, first - firstMin);
        var secondSlack = Math.Max(0, second - secondMin);
        var slack = firstSlack + secondSlack;
        if (slack <= over)
        {
            return (Math.Min(first, firstMin), Math.Min(second, secondMin));
        }

        return (first - (over * firstSlack / slack), second - (over * secondSlack / slack));
    }
}
