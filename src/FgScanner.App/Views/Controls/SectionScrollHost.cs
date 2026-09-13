using System.Windows;
using System.Windows.Controls;

namespace FgScanner.App.Views.Controls;

/// <summary>
/// Wraps a whole section so it scrolls only when the window is smaller than the section can use.
///
/// A plain ScrollViewer hands its content unlimited room, which breaks every screen here that fills
/// the window: star-sized rows collapse, and a DataGrid measured with infinite height stops
/// virtualizing and builds a row for every page in the group. This host instead sizes the content to
/// exactly the space it has — so above the minimum nothing behaves differently — and only below the
/// minimum lets the content grow past the edge, where the scroll bars take over. See
/// <see cref="ScrollFill"/> for why the size comes from the host rather than the viewport.
/// </summary>
public sealed class SectionScrollHost : ScrollViewer
{
    public static readonly DependencyProperty MinContentWidthProperty = DependencyProperty.Register(
        nameof(MinContentWidth), typeof(double), typeof(SectionScrollHost),
        new PropertyMetadata(0.0, OnMinimumChanged));

    /// <summary>NaN for a long page whose height is its content (Settings).</summary>
    public static readonly DependencyProperty MinContentHeightProperty = DependencyProperty.Register(
        nameof(MinContentHeight), typeof(double), typeof(SectionScrollHost),
        new PropertyMetadata(0.0, OnMinimumChanged));

    public SectionScrollHost()
    {
        HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        Focusable = false;
        SizeChanged += (_, _) => FitContent();
    }

    public double MinContentWidth
    {
        get => (double)GetValue(MinContentWidthProperty);
        set => SetValue(MinContentWidthProperty, value);
    }

    public double MinContentHeight
    {
        get => (double)GetValue(MinContentHeightProperty);
        set => SetValue(MinContentHeightProperty, value);
    }

    protected override void OnContentChanged(object oldContent, object newContent)
    {
        base.OnContentChanged(oldContent, newContent);
        FitContent();
    }

    private static void OnMinimumChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((SectionScrollHost)d).FitContent();

    private void FitContent()
    {
        VerticalScrollBarVisibility = double.IsNaN(MinContentHeight)
            ? ScrollBarVisibility.Visible
            : ScrollBarVisibility.Auto;

        if (Content is not FrameworkElement content || ActualWidth <= 0 || ActualHeight <= 0)
        {
            return;
        }

        var size = ScrollFill.Resolve(
            ActualWidth, ActualHeight, MinContentWidth, MinContentHeight,
            SystemParameters.VerticalScrollBarWidth, SystemParameters.HorizontalScrollBarHeight);
        content.Width = size.Width;
        content.Height = size.Height;
    }
}
