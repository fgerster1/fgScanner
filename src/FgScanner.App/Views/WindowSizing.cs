namespace FgScanner.App.Views;

/// <summary>A window's position and size in layout units, free of WPF types so it can be tested.</summary>
public readonly record struct WindowBounds(double Left, double Top, double Width, double Height);

/// <summary>
/// Keeps windows and panels inside the screen they are on. The arithmetic lives here rather than in
/// the windows because it is the part that goes wrong silently: a window sized for the machine it was
/// designed on opens partly off a laptop screen with no visible sign anything is missing.
/// </summary>
public static class WindowSizing
{
    /// <summary>The requested size, shrunk to the work area if it does not fit, and centred in it.</summary>
    public static WindowBounds FitToWorkArea(double width, double height, WindowBounds workArea)
    {
        var fittedWidth = Math.Min(width, workArea.Width);
        var fittedHeight = Math.Min(height, workArea.Height);
        return new WindowBounds(
            workArea.Left + ((workArea.Width - fittedWidth) / 2),
            workArea.Top + ((workArea.Height - fittedHeight) / 2),
            fittedWidth,
            fittedHeight);
    }

    /// <summary>
    /// A dialog moved (and if need be shrunk) so all of it is on the screen. Centred over an owner
    /// dragged near an edge, a dialog's button row can land under the taskbar or off the monitor,
    /// and a fixed-size dialog cannot be resized to reach it.
    /// </summary>
    public static WindowBounds KeepInside(WindowBounds window, WindowBounds workArea)
    {
        var width = Math.Min(window.Width, workArea.Width);
        var height = Math.Min(window.Height, workArea.Height);
        return new WindowBounds(
            Math.Clamp(window.Left, workArea.Left, workArea.Left + workArea.Width - width),
            Math.Clamp(window.Top, workArea.Top, workArea.Top + workArea.Height - height),
            width,
            height);
    }

    /// <summary>
    /// A resizable panel's size, limited so the panel beside it keeps its minimum. A size saved on a
    /// large monitor would otherwise push its neighbour off a small one. A panel not yet laid out
    /// reports zero available, and the saved size is kept rather than thrown away.
    /// </summary>
    public static double ClampPanel(double desired, double available, double otherMinimum, double splitter, double panelMinimum)
    {
        if (!(available > 0))
        {
            return desired;
        }

        return Math.Max(panelMinimum, Math.Min(desired, available - otherMinimum - splitter));
    }
}
