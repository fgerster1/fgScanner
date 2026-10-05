using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;

namespace FgScanner.App.Views.Dialogs;

/// <summary>
/// A page beside its OCR text (SPEC-2026-009 §08-5), paged through the group like the page viewer.
/// Read-only: the text is shown from the .md or the database and never written, copied to temp, or
/// logged — it is case material. One page is decoded at a time, for the page viewer's reason.
/// </summary>
public partial class OcrViewerWindow : Window
{
    private readonly IReadOnlyList<DocumentRow> _rows;
    private readonly PageNavigator _navigator;
    private readonly ZoomController _zoom = new();
    private readonly FitPolicy _fit;

    public OcrViewerWindow(IReadOnlyList<DocumentRow> rows, int startIndex)
    {
        InitializeComponent();
        _fit = new FitPolicy(_zoom);
        _rows = rows;
        _navigator = new PageNavigator(rows.Count, startIndex);
        Loaded += (_, _) => ShowPage();
    }

    public int CurrentIndex => _navigator.Index;

    /// <summary>
    /// Shows the viewer over whichever window is active — the Groups page or the record editor,
    /// which is itself modal — and returns the index of the page it closed on.
    /// </summary>
    public static int ShowModal(IReadOnlyList<DocumentRow> rows, int startIndex)
    {
        var owner = Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive)
            ?? Application.Current?.MainWindow;
        var viewer = new OcrViewerWindow(rows, startIndex) { Owner = owner };
        viewer.ShowDialog();
        return viewer.CurrentIndex;
    }

    private void ShowPage()
    {
        if (_rows.Count == 0)
        {
            return;
        }

        var row = _rows[_navigator.Index];
        PageImage.Source = null;
        PageImage.Source = PageViewerWindow.LoadFullImage(row.ImagePath);
        OcrText.Text = OcrTextSource.Read(row.ImagePath, row.OcrText, row.OcrState, row.IsBlank);
        OcrText.ScrollToHome();
        FileNameText.Text = row.ImagePath;
        PositionText.Text = _navigator.Position;
        PreviousButton.IsEnabled = _navigator.CanGoPrevious;
        NextButton.IsEnabled = _navigator.CanGoNext;
        FitToViewport();
    }

    private void Move(Action step)
    {
        step();
        ShowPage();
    }

    private void OnPrevious(object sender, RoutedEventArgs e) => Move(_navigator.Previous);

    private void OnNext(object sender, RoutedEventArgs e) => Move(_navigator.Next);

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

    private void FitToViewport()
    {
        if (PageImage.Source is BitmapSource image)
        {
            var layout = ImageLayout.Of(image);
            _fit.Fit(layout.Width, layout.Height, Scroller.ViewportWidth, Scroller.ViewportHeight, layout.MaxScale);
        }

        ApplyZoom();
    }

    private void ApplyZoom()
    {
        PageScale.ScaleX = _zoom.Scale;
        PageScale.ScaleY = _zoom.Scale;
        ZoomText.Text = PageImage.Source is null
            ? ""
            : (_zoom.Scale * 100).ToString("0", CultureInfo.InvariantCulture) + "%";
    }

    /// <summary>ScrollChanged, not SizeChanged — see <see cref="PageViewerWindow"/>.</summary>
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

    /// <summary>Arrows page through — unless the text has focus, where they move the caret for
    /// selecting a passage; the text box consumes them before they get here.</summary>
    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.Modifiers != ModifierKeys.None)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Left or Key.PageUp:
                Move(_navigator.Previous);
                break;
            case Key.Right or Key.PageDown:
                Move(_navigator.Next);
                break;
            default:
                return;
        }

        e.Handled = true;
    }
}
