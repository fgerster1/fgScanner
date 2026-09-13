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

        // SourceInitialized: the window has a handle, so its monitor is known, but has not been sized
        // to its content yet — the cap is in place before SizeToContent measures.
        window.SourceInitialized += (_, _) =>
        {
            var area = MonitorWorkArea.For(window);
            window.MaxWidth = Math.Min(window.MaxWidth, area.Width);
            window.MaxHeight = Math.Min(window.MaxHeight, area.Height);
        };

        // ContentRendered: size and centring are final, so the real edges can be checked.
        window.ContentRendered += (_, _) =>
        {
            var placed = WindowSizing.KeepInside(
                new WindowBounds(window.Left, window.Top, window.ActualWidth, window.ActualHeight),
                MonitorWorkArea.For(window));
            window.Left = placed.Left;
            window.Top = placed.Top;
        };
    }
}
