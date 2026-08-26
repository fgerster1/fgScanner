using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;

namespace FgScanner.App.Views.Dialogs;

/// <summary>
/// A page at full resolution, with the group's other pages a key away. The in-place preview is
/// 300px wide in the corner of a grid; reading a scan means seeing it properly.
///
/// Exactly one page is decoded at a time. A 2480x3507 scan costs ~35 MB decoded, so pre-loading
/// neighbours to make paging feel snappier would spend hundreds of megabytes on a large group.
/// </summary>
public partial class PageViewerWindow : Window
{
    private readonly IReadOnlyList<DocumentRow> _pages;
    private readonly PageNavigator _navigator;
    private readonly ZoomController _zoom = new();
    private bool _fitOnNextLayout = true;

    public PageViewerWindow(IReadOnlyList<DocumentRow> pages, int startIndex)
    {
        InitializeComponent();
        _pages = pages;
        _navigator = new PageNavigator(pages.Count, startIndex);
        Loaded += (_, _) => Show(_navigator.Index);
    }

    /// <summary>The page left showing, so the grid's selection can follow the viewer.</summary>
    public DocumentRow? Current =>
        _pages.Count == 0 ? null : _pages[Math.Clamp(_navigator.Index, 0, _pages.Count - 1)];

    private void Show(int _)
    {
        var row = Current;
        // Dropping the previous bitmap before decoding the next keeps one page in memory, not two.
        PageImage.Source = null;
        PageImage.Source = row is null ? null : LoadFullImage(row.ImagePath);
        FileNameText.Text = row?.ImagePath ?? "";
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

    private void FitToViewport()
    {
        if (PageImage.Source is not BitmapImage image)
        {
            return;
        }

        if (Scroller.ViewportWidth <= 0 || Scroller.ViewportHeight <= 0)
        {
            // Still laying out — fit once the ScrollViewer knows its own size.
            _fitOnNextLayout = true;
            return;
        }

        _fitOnNextLayout = false;
        _zoom.Fit(image.PixelWidth, image.PixelHeight, Scroller.ViewportWidth, Scroller.ViewportHeight);
        ApplyZoom();
    }

    private void ApplyZoom()
    {
        PageScale.ScaleX = _zoom.Scale;
        PageScale.ScaleY = _zoom.Scale;
        ZoomText.Text = (_zoom.Scale * 100).ToString("0", CultureInfo.InvariantCulture) + "%";
    }

    private void OnScrollerSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_fitOnNextLayout)
        {
            FitToViewport();
        }
    }

    private void OnScrollerMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (Keyboard.Modifiers != ModifierKeys.Control)
        {
            return;
        }

        if (e.Delta > 0)
        {
            _zoom.In();
        }
        else
        {
            _zoom.Out();
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
                _zoom.In();
                break;
            case Key.OemMinus or Key.Subtract:
                _zoom.Out();
                break;
            case Key.D0 or Key.NumPad0:
                _zoom.Reset();
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
        _zoom.In();
        ApplyZoom();
    }

    private void OnZoomOut(object sender, RoutedEventArgs e)
    {
        _zoom.Out();
        ApplyZoom();
    }

    private void OnFit(object sender, RoutedEventArgs e) => FitToViewport();

    private void OnActualSize(object sender, RoutedEventArgs e)
    {
        _zoom.Reset();
        ApplyZoom();
    }
}
