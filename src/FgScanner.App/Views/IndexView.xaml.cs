using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;

namespace FgScanner.App.Views;

public partial class IndexView : UserControl
{
    private readonly ZoomController _zoom = new();
    private readonly FitPolicy _fit;

    public IndexView()
    {
        InitializeComponent();
        _fit = new FitPolicy(_zoom);
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
