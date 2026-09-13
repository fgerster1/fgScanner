namespace FgScanner.App.Views;

/// <summary>
/// Decides when a page re-fits on its own. A fit that ignores resizing looks broken the moment a
/// divider is dragged; one that re-fits after the user zoomed throws their zoom away. So a page keeps
/// fitting its viewport until the user picks a zoom, and pressing Fit hands control back.
///
/// This also covers the first fit: one asked for while the viewport is still unlaid-out (size zero)
/// is simply left pending, and the first real size fits it.
/// </summary>
public sealed class FitPolicy(ZoomController zoom)
{
    private bool _userZoomed;

    public void Fit(double contentWidth, double contentHeight, double viewportWidth, double viewportHeight, double maxScale)
    {
        _userZoomed = false;
        zoom.Fit(contentWidth, contentHeight, viewportWidth, viewportHeight, maxScale);
    }

    public void In()
    {
        _userZoomed = true;
        zoom.In();
    }

    public void Out()
    {
        _userZoomed = true;
        zoom.Out();
    }

    public void Reset()
    {
        _userZoomed = true;
        zoom.Reset();
    }

    public void ViewportResized(double contentWidth, double contentHeight, double viewportWidth, double viewportHeight, double maxScale)
    {
        if (!_userZoomed)
        {
            zoom.Fit(contentWidth, contentHeight, viewportWidth, viewportHeight, maxScale);
        }
    }
}
