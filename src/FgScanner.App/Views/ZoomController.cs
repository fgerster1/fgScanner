namespace FgScanner.App.Views;

/// <summary>
/// The zoom factor for a page image, shared by the in-place preview and the pop-out viewer so the
/// two behave identically. Deliberately free of WPF types: the arithmetic is where zoom goes
/// wrong — a runaway scale on a fast wheel, or a divide by a viewport that has not been laid out
/// yet — and that is worth testing without a window.
/// </summary>
public sealed class ZoomController
{
    /// <summary>
    /// Low enough that a whole 300-DPI page fits the short in-place preview panel. A friendlier
    /// floor like 0.25 would clamp Fit above the scale the page actually needs there, so "fit"
    /// would leave part of the page off-screen — a limit that quietly lies is worse than a small one.
    /// </summary>
    public const double Minimum = 0.05;

    public const double Maximum = 8.0;

    private const double Step = 1.25;

    public double Scale { get; private set; } = 1.0;

    public void In() => Scale = Math.Min(Maximum, Scale * Step);

    public void Out() => Scale = Math.Max(Minimum, Scale / Step);

    /// <summary>Actual paper size: the image at its DPI-scaled layout size (see <see cref="ImageLayout"/>).</summary>
    public void Reset() => Scale = 1.0;

    /// <summary>
    /// Scales the page so all of it is visible. Content is the image's layout size, never its pixel
    /// count — the two differ by the scan's DPI, and mixing them showed a 300-DPI page at a third of
    /// the space. Fit may enlarge, but stops at <paramref name="maxScale"/> (image pixels per layout
    /// unit): past that it only magnifies pixels. A viewport with no usable size is ignored rather
    /// than divided by — during layout it reports zero, which would yield an infinite scale.
    /// </summary>
    public void Fit(double contentWidth, double contentHeight, double viewportWidth, double viewportHeight, double maxScale)
    {
        if (!IsUsable(contentWidth) || !IsUsable(contentHeight)
            || !IsUsable(viewportWidth) || !IsUsable(viewportHeight) || !IsUsable(maxScale))
        {
            return;
        }

        var scale = Math.Min(viewportWidth / contentWidth, viewportHeight / contentHeight);
        Scale = Math.Clamp(scale, Minimum, Math.Max(Minimum, Math.Min(maxScale, Maximum)));
    }

    private static bool IsUsable(double value) => double.IsFinite(value) && value > 0;
}

/// <summary>
/// Which page of a group the viewer is showing. Indexes into the order the grid renders, so
/// paging forward in the viewer matches reading down the grid.
/// </summary>
public sealed class PageNavigator(int count, int startIndex)
{
    public int Count { get; } = Math.Max(0, count);

    public int Index { get; private set; } =
        count <= 0 ? 0 : Math.Clamp(startIndex, 0, count - 1);

    public bool CanGoPrevious => Index > 0;

    public bool CanGoNext => Index < Count - 1;

    /// <summary>Empty for a group with no pages: "Page 1 of 0" is worse than saying nothing.</summary>
    public string Position => Count == 0 ? "" : $"Page {Index + 1} of {Count}";

    public void First() => Index = 0;

    public void Previous() => Index = Math.Max(0, Index - 1);

    public void Next() => Index = Math.Min(Math.Max(0, Count - 1), Index + 1);

    public void Last() => Index = Math.Max(0, Count - 1);
}
