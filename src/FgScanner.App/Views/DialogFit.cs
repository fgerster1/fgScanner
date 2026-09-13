using System.Windows;

namespace FgScanner.App.Views;

/// <summary>
/// Keeps a dialog on the screen it opens on: never taller or wider than that monitor's usable area,
/// and pulled back inside it if centring over its owner pushed an edge off. The fixed dialogs here
/// cannot be resized, so a button row that lands below the taskbar is simply unreachable.
///
/// The dialogs pair this with a scrolling body and a button row outside the scroll area, so capping
/// the height scrolls the fields rather than hiding the buttons.
/// </summary>
public static class DialogFit
{
    public static readonly DependencyProperty KeepOnScreenProperty = DependencyProperty.RegisterAttached(
        "KeepOnScreen", typeof(bool), typeof(DialogFit), new PropertyMetadata(false, OnKeepOnScreenChanged));

    public static bool GetKeepOnScreen(DependencyObject element) => (bool)element.GetValue(KeepOnScreenProperty);

    public static void SetKeepOnScreen(DependencyObject element, bool value) => element.SetValue(KeepOnScreenProperty, value);

    private static void OnKeepOnScreenChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not Window window || e.NewValue is not true)
        {
            return;
        }

        // SourceInitialized: the window has a handle, so its monitor is known. WPF has already sized it
        // to its content and centred it at that size; a cap set here shrinks it on the next layout, and
        // Place moves it back inside the screen once it has rendered.
        window.SourceInitialized += (_, _) =>
        {
            var area = MonitorWorkArea.For(window);
            if (window.SizeToContent == SizeToContent.Manual
                && window.ResizeMode is ResizeMode.CanResize or ResizeMode.CanResizeWithGrip)
            {
                // A resizable window (the page viewer) only opens small enough to fit. A MaxWidth or
                // MaxHeight cap would outlive the opening: it could no longer be enlarged or maximized
                // after being dragged to a bigger monitor.
                window.Width = Math.Min(window.Width, area.Width);
                window.Height = Math.Min(window.Height, area.Height);
                return;
            }

            window.MaxWidth = Math.Min(window.MaxWidth, area.Width);
            window.MaxHeight = Math.Min(window.MaxHeight, area.Height);
        };

        // ContentRendered: size and centring are final, so the real edges can be checked.
        window.ContentRendered += (_, _) => Place(window);

        // A dialog that sizes to its content can grow after it opens (Export images reveals its TIFF
        // options). It grows downwards from its top edge, pushing its buttons under the taskbar. A
        // window the user resizes is left where they put it.
        window.SizeChanged += (_, _) =>
        {
            if (window.IsLoaded && window.SizeToContent != SizeToContent.Manual)
            {
                Place(window);
            }
        };
    }

    /// <summary>Only the position changes: the cap from SourceInitialized already keeps the size inside the area.</summary>
    private static void Place(Window window)
    {
        var placed = WindowSizing.KeepInside(
            new WindowBounds(window.Left, window.Top, window.ActualWidth, window.ActualHeight),
            MonitorWorkArea.For(window));
        window.Left = placed.Left;
        window.Top = placed.Top;
    }
}
