using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;

namespace FgScanner.App.Views;

public partial class IndexView : UserControl
{
    private readonly ZoomController _zoom = new();
    private readonly FitPolicy _fit;

    private const string DocumentListWidthKey = "Index.DocumentListWidth";
    private const string AnswerPanelWidthKey = "Index.AnswerPanelWidth";

    // Mirror the column minimums and splitter thickness in IndexView.xaml.
    private const double DocumentListMinWidth = 200;
    private const double PageMinWidth = 300;
    private const double AnswerPanelMinWidth = 300;
    private const double SplitterThickness = 6;

    private bool _paneSizesRestored;

    public IndexView()
    {
        InitializeComponent();
        _fit = new FitPolicy(_zoom);
        Loaded += async (_, _) =>
        {
            HookWindowClosing();
            await RestorePaneSizesAsync();
        };
        Unloaded += (_, _) => SavePaneSizes();
    }

    // ----- pane widths (SPEC-2026-009 §08-4) -----

    /// <summary>Once per run: the section host reloads the view each time Index is shown, and
    /// re-reading then would undo a drag made since the last save.</summary>
    private async Task RestorePaneSizesAsync()
    {
        if (_paneSizesRestored || DataContext is not IndexViewModel { Settings: { } settings })
        {
            return;
        }

        _paneSizesRestored = true;
        try
        {
            DocumentListColumn.Width = new GridLength(PanelSize.Read(
                await settings.GetAsync(DocumentListWidthKey, ""), 280, DocumentListMinWidth));
            AnswerPanelColumn.Width = new GridLength(PanelSize.Read(
                await settings.GetAsync(AnswerPanelWidthKey, ""), 340, AnswerPanelMinWidth));
            LimitPanes();
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Restoring Index pane sizes");
        }
    }

    /// <summary>
    /// Saved on release as well as on unload: closing the app while on this screen never unloads
    /// the view, so a width saved only on unload would be forgotten exactly when Jim quits.
    /// </summary>
    private void OnSplitterDragCompleted(
        object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e)
    {
        LimitPanes();
        SavePaneSizes();
    }

    private bool _closingHooked;

    /// <summary>
    /// The widths are saved again as the window closes. A drag's end is not the only way a width
    /// changes — a splitter moved with the arrow keys never raises DragCompleted — and closing the
    /// app while on this screen never unloads the view, so without this the last width set could be
    /// the one that is lost.
    /// </summary>
    private void HookWindowClosing()
    {
        if (!_closingHooked && Window.GetWindow(this) is { } window)
        {
            _closingHooked = true;
            window.Closing += (_, _) => SavePaneSizes(waitForWrite: true);
        }
    }

    /// <param name="waitForWrite">True while the window closes: an unawaited write can be cut off
    /// by the process exiting. Safe to block on — the settings service never resumes on the UI thread.</param>
    private void SavePaneSizes(bool waitForWrite = false)
    {
        if (!_paneSizesRestored || DataContext is not IndexViewModel { Settings: { } settings })
        {
            return;
        }

        try
        {
            var writes = Task.WhenAll(
                settings.SetAsync(DocumentListWidthKey, PanelSize.Format(DocumentListColumn.Width.Value)),
                settings.SetAsync(AnswerPanelWidthKey, PanelSize.Format(AnswerPanelColumn.Width.Value)));
            if (waitForWrite)
            {
                writes.GetAwaiter().GetResult();
            }
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Saving Index pane sizes");
        }
    }

    private void OnPaneRoomChanged(object sender, SizeChangedEventArgs e) => LimitPanes();

    /// <summary>
    /// Keeps the page pane at its minimum whatever the side panes were dragged to. The limit goes
    /// on MaxWidth, never Width, so a narrow window does not overwrite the width Jim chose on a wide one.
    /// </summary>
    private void LimitPanes()
    {
        var room = PaneGrid.ActualWidth;
        DocumentListColumn.MaxWidth = WindowSizing.ClampPanel(double.PositiveInfinity, room,
            PageMinWidth + SplitterThickness + AnswerPanelColumn.ActualWidth, SplitterThickness,
            DocumentListMinWidth);
        AnswerPanelColumn.MaxWidth = WindowSizing.ClampPanel(double.PositiveInfinity, room,
            PageMinWidth + SplitterThickness + DocumentListColumn.ActualWidth, SplitterThickness,
            AnswerPanelMinWidth);
    }

    // ----- page zoom: the Groups preview's pattern (SPEC-2026-009 §08-3) -----

    private void OnPageMouseWheel(object sender, MouseWheelEventArgs e)
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

    private void OnFit(object sender, RoutedEventArgs e) => FitPage();

    /// <summary>
    /// Each page opens showing all of itself; otherwise a 300-DPI scan would start at 1:1,
    /// scrolled to the middle of the paper.
    /// </summary>
    private void OnPageImageChanged(object sender, DataTransferEventArgs e) => FitPage();

    private void OnPageMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2 && DataContext is IndexViewModel viewModel)
        {
            viewModel.OpenPageViewerCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void FitPage()
    {
        if (PageImage.Source is System.Windows.Media.Imaging.BitmapSource image)
        {
            var layout = ImageLayout.Of(image);
            _fit.Fit(layout.Width, layout.Height,
                PageScroller.ViewportWidth, PageScroller.ViewportHeight, layout.MaxScale);
        }

        ApplyZoom();
    }

    /// <summary>
    /// ScrollChanged, not SizeChanged: the ScrollViewer publishes its new viewport only after
    /// SizeChanged, so a re-fit there would use the previous size, and a fit asked for before the
    /// pane had a size would never run.
    /// </summary>
    private void OnPageScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if ((e.ViewportWidthChange != 0 || e.ViewportHeightChange != 0)
            && PageImage.Source is System.Windows.Media.Imaging.BitmapSource image)
        {
            var layout = ImageLayout.Of(image);
            _fit.ViewportResized(layout.Width, layout.Height,
                e.ViewportWidth, e.ViewportHeight, layout.MaxScale);
            ApplyZoom();
        }
    }

    private void ApplyZoom()
    {
        PageScale.ScaleX = _zoom.Scale;
        PageScale.ScaleY = _zoom.Scale;
        ZoomText.Text = PageImage.Source is null
            ? ""
            : (_zoom.Scale * 100).ToString("0", System.Globalization.CultureInfo.InvariantCulture) + "%";
    }

    // The downloaded zip is the normal path (ADR-0015): picking the FILE
    // means Windows never offers the inside of the zip as if it were a folder.
    private async void OnOpenPackage(object sender, System.Windows.RoutedEventArgs e)
    {
        if (DataContext is not IndexViewModel viewModel)
        {
            return;
        }

        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Open the batch you downloaded",
            Filter = "Index batch (*.zip)|*.zip",
            InitialDirectory = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"),
        };
        if (dialog.ShowDialog() == true)
        {
            await viewModel.OpenPackageAsync(dialog.FileName);
        }
    }

    private async void OnOpenPackageFolder(object sender, System.Windows.RoutedEventArgs e)
    {
        if (DataContext is not IndexViewModel viewModel)
        {
            return;
        }

        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Open the index package folder",
        };
        if (dialog.ShowDialog() == true)
        {
            await viewModel.OpenPackageAsync(dialog.FolderName);
        }
    }
}
