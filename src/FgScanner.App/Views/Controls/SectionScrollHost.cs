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
        // Fluent's ScrollViewer style is implicit, and implicit styles match the exact type only. Without
        // this the host renders the classic template: bars that take layout space, and a corner square
        // that stays light grey in the dark theme.
        SetResourceReference(StyleProperty, typeof(ScrollViewer));
        HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        Focusable = false;
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

        // Otherwise only the presenter below is re-measured, and it hands the new section unlimited
        // room before MeasureOverride has sized it.
        InvalidateMeasure();
    }

    private static void OnMinimumChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var host = (SectionScrollHost)d;
        host.VerticalScrollBarVisibility = double.IsNaN(host.MinContentHeight)
            ? ScrollBarVisibility.Visible
            : ScrollBarVisibility.Auto;
        host.InvalidateMeasure();
    }

    /// <summary>
    /// Sizes the section before the ScrollViewer measures it. Sizing it after layout instead (on
    /// SizeChanged) left the first measure unsized: the section shown before the window had a size —
    /// Scan, at startup — was measured with infinite height, and its thumbnail list built and decoded
    /// every page before the window appeared.
    /// </summary>
    protected override Size MeasureOverride(Size constraint)
    {
        if (Content is FrameworkElement content
            && double.IsFinite(constraint.Width) && double.IsFinite(constraint.Height))
        {
            var size = ScrollFill.Resolve(
                constraint.Width, constraint.Height, MinContentWidth, MinContentHeight,
                SystemParameters.VerticalScrollBarWidth, SystemParameters.HorizontalScrollBarHeight);
            content.Width = size.Width;
            content.Height = size.Height;
        }

        return base.MeasureOverride(constraint);
    }
}
