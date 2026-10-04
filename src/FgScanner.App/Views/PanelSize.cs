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
}
