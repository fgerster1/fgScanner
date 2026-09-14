using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;

namespace FgScanner.App.Views.Dialogs;

/// <summary>
/// A page at full resolution, with the other pages a key away. The in-place preview is 300px wide
/// in the corner of a grid; reading a scan means seeing it properly. It takes image paths rather
/// than group rows so that scans not yet saved to a group can be checked the same way.
///
/// Exactly one page is decoded at a time. A 2480x3507 scan costs ~35 MB decoded, so pre-loading
/// neighbours to make paging feel snappier would spend hundreds of megabytes on a large group.
/// </summary>
public partial class PageViewerWindow : Window
{
    private readonly IReadOnlyList<string> _imagePaths;
    private readonly PageNavigator _navigator;
    private readonly ZoomController _zoom = new();
    private readonly FitPolicy _fit;

    public PageViewerWindow(IReadOnlyList<string> imagePaths, int startIndex)
    {
        InitializeComponent();
        _fit = new FitPolicy(_zoom);
        _imagePaths = imagePaths;
        _navigator = new PageNavigator(imagePaths.Count, startIndex);
        Loaded += (_, _) => Show(_navigator.Index);
    }

    /// <summary>The page left showing, so the caller's selection can follow the viewer.</summary>
    public int CurrentIndex => _navigator.Index;

    /// <summary>Shows the viewer over the main window and returns the index of the page it closed on.</summary>
    public static int ShowModal(IReadOnlyList<string> imagePaths, int startIndex)
    {
        var viewer = new PageViewerWindow(imagePaths, startIndex) { Owner = Application.Current?.MainWindow };
        viewer.ShowDialog();
        return viewer.CurrentIndex;
    }

    private void Show(int _)
    {
        var path = _imagePaths.Count == 0 ? null : _imagePaths[_navigator.Index];
        // Dropping the previous bitmap before decoding the next keeps one page in memory, not two.
        PageImage.Source = null;
        PageImage.Source = path is null ? null : LoadFullImage(path);
        FileNameText.Text = path ?? "";
        PositionText.Text = _navigator.Position;
        FirstButton.IsEnabled = _navigator.CanGoPrevious;
        PreviousButton.IsEnabled = _navigator.CanGoPrevious;
        NextButton.IsEnabled = _navigator.CanGoNext;
        LastButton.IsEnabled = _navigator.CanGoNext;

        // Each page opens showing all of itself; the user's zoom is theirs until they turn a page.
        FitToViewport();
    }

    private static BitmapImage? LoadFullImage(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = new Uri(path);
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            // Edits rewrite the same path; without this WPF serves the stale cached bitmap.
            bitmap.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or OutOfMemoryException)
        {
            return null;
        }
    }

    /// <summary>
    /// A fit asked for while the window is still laying out stays pending in the policy, and the
    /// first real size — <see cref="OnScrollerScrollChanged"/> — carries it out.
    /// </summary>
    private void FitToViewport()
    {
        if (PageImage.Source is not BitmapSource image)
        {
            ApplyZoom();
            return;
        }

        var layout = ImageLayout.Of(image);
        _fit.Fit(layout.Width, layout.Height, Scroller.ViewportWidth, Scroller.ViewportHeight, layout.MaxScale);
        ApplyZoom();
    }

    private void ApplyZoom()
    {
        PageScale.ScaleX = _zoom.Scale;
        PageScale.ScaleY = _zoom.Scale;

        // A page that failed to load has no scale to report, whatever zoom key was pressed since.
        ZoomText.Text = PageImage.Source is null
            ? ""
            : (_zoom.Scale * 100).ToString("0", CultureInfo.InvariantCulture) + "%";
    }

    /// <summary>
    /// Re-fits when the viewport changes size. ScrollChanged, not SizeChanged: the ScrollViewer
    /// publishes its new viewport only after SizeChanged has been raised, so a re-fit there used the
    /// previous size and a maximized viewer kept its small fit.
    /// </summary>
    private void OnScrollerScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if ((e.ViewportWidthChange == 0 && e.ViewportHeightChange == 0)
            || PageImage.Source is not BitmapSource image)
        {
            return;
        }

        var layout = ImageLayout.Of(image);
        _fit.ViewportResized(layout.Width, layout.Height, e.ViewportWidth, e.ViewportHeight, layout.MaxScale);
        ApplyZoom();
    }

    private void OnScrollerMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (Keyboard.Modifiers != ModifierKeys.Control)
        {
            return;
        }

        if (e.Delta > 0)
        {
            _fit.In();
        }
        else
        {
            _fit.Out();
        }

        ApplyZoom();
        e.Handled = true;
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        // Plain arrows page through: Ctrl+Shift+Left/Right are already rotate, app-wide, and a
        // viewer that rotated pages behind the user's back would be a nasty surprise.
        if (Keyboard.Modifiers == ModifierKeys.None)
        {
            switch (e.Key)
            {
                case Key.Left or Key.PageUp:
                    Move(_navigator.Previous);
                    break;
                case Key.Right or Key.PageDown:
                    Move(_navigator.Next);
                    break;
                case Key.Home:
                    Move(_navigator.First);
                    break;
                case Key.End:
                    Move(_navigator.Last);
                    break;
                default:
                    return;
            }

            e.Handled = true;
            return;
        }

        if (Keyboard.Modifiers != ModifierKeys.Control)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.OemPlus or Key.Add:
                _fit.In();
                break;
            case Key.OemMinus or Key.Subtract:
                _fit.Out();
                break;
            case Key.D0 or Key.NumPad0:
                _fit.Reset();
                break;
            default:
                return;
        }

        ApplyZoom();
        e.Handled = true;
    }

    private void OnFirst(object sender, RoutedEventArgs e) => Move(_navigator.First);

    private void OnPrevious(object sender, RoutedEventArgs e) => Move(_navigator.Previous);

    private void OnNext(object sender, RoutedEventArgs e) => Move(_navigator.Next);

    private void OnLast(object sender, RoutedEventArgs e) => Move(_navigator.Last);

    private void Move(Action step)
    {
        step();
        Show(_navigator.Index);
    }

    private void OnZoomIn(object sender, RoutedEventArgs e)
    {
        _fit.In();
        ApplyZoom();
    }

    private void OnZoomOut(object sender, RoutedEventArgs e)
    {
        _fit.Out();
        ApplyZoom();
    }

    private void OnFit(object sender, RoutedEventArgs e) => FitToViewport();

    private void OnActualSize(object sender, RoutedEventArgs e)
    {
        _fit.Reset();
        ApplyZoom();
    }
}
