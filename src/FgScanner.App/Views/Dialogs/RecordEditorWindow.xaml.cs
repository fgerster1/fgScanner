using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using FgScanner.App.Services;

namespace FgScanner.App.Views.Dialogs;

/// <summary>
/// One page's index values as a form, beside the page itself, above the rest of the group
/// (SPEC-2026-002). The grid alone means scrolling sideways past columns to reach a field while the
/// page it belongs to is 300px wide in the corner.
///
/// Modal, like the page viewer: the editor works on the Groups grid's own rows, and two surfaces
/// editing them at once would each show the other's values stale.
/// </summary>
public partial class RecordEditorWindow : Window
{
    private readonly RecordEditorViewModel _editor;
    private readonly ZoomController _zoom = new();
    private readonly FitPolicy _fit;

    /// <summary>The memo boxes now on screen, by field name, so their sizes can be restored and saved.</summary>
    private readonly Dictionary<string, TextBox> _memoBoxes = new(StringComparer.OrdinalIgnoreCase);

    private RecordEditorLayout _layout = new(0, 0, new Dictionary<string, MemoSize>());

    public RecordEditorWindow(RecordEditorViewModel editor)
    {
        InitializeComponent();
        _fit = new FitPolicy(_zoom);
        _editor = editor;
        DataContext = editor;
        editor.FieldsRebuilt += RebuildColumns;
        Closed += (_, _) => editor.FieldsRebuilt -= RebuildColumns;
        AddHandler(TextLengthGuard.RefusedEvent, new EventHandler<LengthRefusedEventArgs>(OnLengthRefused));
    }

    /// <summary>Shows the editor over the main window and returns when it closes.</summary>
    public static void ShowModal(RecordEditorViewModel editor)
    {
        var window = new RecordEditorWindow(editor) { Owner = Application.Current?.MainWindow };
        window.ShowDialog();
    }

    /// <summary>A refused paste says why on the status line, which is the group's own.</summary>
    private void OnLengthRefused(object? sender, LengthRefusedEventArgs e) => _editor.StatusText = e.Message;

    /// <summary>
    /// Fits the design size to the screen this window opened on. 1280x900 is taller than a 1366x768
    /// laptop can show above its taskbar, and a record editor whose grid is under the taskbar is
    /// missing the half that says which page is being edited.
    /// </summary>
    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var bounds = WindowSizing.FitToWorkArea(Width, Height, MonitorWorkArea.For(this));
        Left = bounds.Left;
        Top = bounds.Top;
        Width = bounds.Width;
        Height = bounds.Height;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        RebuildColumns();
        _ = RestoreLayoutAsync();
    }

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e) => _ = SaveLayoutAsync();

    private void RebuildColumns() => EntryGridColumns.Build(EntryGrid, _editor.Fields);

    /// <summary>
    /// Restores the sizes this group was last left at, clamped to this window — a layout saved on the
    /// dev machine's monitor would otherwise push a pane off the station's screen.
    /// </summary>
    private async Task RestoreLayoutAsync()
    {
        try
        {
            _layout = await _editor.Layout.LoadAsync(_editor.Group.Id, TopGrid.ActualWidth, ActualHeight);

            // A pane not yet measured reports zero room, and half of nothing would open the form at
            // its minimum. The sizes on screen stay as they are until there is a real width to fit.
            if (_layout.FormWidth > 0 && _layout.TopHeight > 0)
            {
                FormColumn.Width = new GridLength(_layout.FormWidth);
                TopRow.Height = new GridLength(_layout.TopHeight);
            }

            foreach (var (name, box) in _memoBoxes)
            {
                ApplyMemoSize(box, name);
            }
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Restoring the record editor layout for group {GroupId}", _editor.Group.Id);
        }
    }

    /// <summary>
    /// Saved on release and on close rather than on close alone: a size that survives only if the
    /// window is closed the expected way is not remembered in any sense the user would recognise.
    /// </summary>
    private void OnSplitterDragCompleted(object sender, DragCompletedEventArgs e) => _ = SaveLayoutAsync();

    private async Task SaveLayoutAsync()
    {
        try
        {
            var memo = new Dictionary<string, MemoSize>(StringComparer.OrdinalIgnoreCase);
            foreach (var (name, box) in _memoBoxes)
            {
                memo[name] = new MemoSize(box.ActualWidth, Lines(box, box.ActualHeight));
            }

            // Sizes left over from a memo field that has since been renamed or removed are kept: the
            // field may come back, and a handful of stale keys costs nothing (§07).
            foreach (var (name, size) in _layout.Memo)
            {
                if (!memo.ContainsKey(name))
                {
                    memo[name] = size;
                }
            }

            _layout = new RecordEditorLayout(FormColumn.ActualWidth, TopRow.ActualHeight, memo);
            await _editor.Layout.SaveAsync(_editor.Group.Id, _layout);
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Saving the record editor layout for group {GroupId}", _editor.Group.Id);
        }
    }

    private void OnMemoBoxLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox box && box.DataContext is FormField field)
        {
            _memoBoxes[field.Name] = box;
            ApplyMemoSize(box, field.Name);
        }
    }

    private void OnMemoBoxUnloaded(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox { DataContext: FormField field })
        {
            _memoBoxes.Remove(field.Name);
        }
    }

    private void ApplyMemoSize(TextBox box, string name)
    {
        if (!_layout.Memo.TryGetValue(name, out var stored))
        {
            return;
        }

        var size = RecordEditorLayoutStore.ClampMemo(stored, PaneWidth());
        box.Width = size.Width;
        box.Height = Pixels(box, size.Height);
    }

    /// <summary>Resizes the memo box by dragging its grip, within what the pane and 2–20 lines allow.</summary>
    private void OnMemoResize(object sender, DragDeltaEventArgs e)
    {
        if (sender is not Thumb thumb || MemoBoxOf(thumb) is not { } box)
        {
            return;
        }

        var wanted = new MemoSize(
            box.ActualWidth + e.HorizontalChange,
            Lines(box, box.ActualHeight + e.VerticalChange));
        var size = RecordEditorLayoutStore.ClampMemo(wanted, PaneWidth());
        box.Width = size.Width;
        box.Height = Pixels(box, size.Height);
    }

    private void OnMemoResizeCompleted(object sender, DragCompletedEventArgs e) => _ = SaveLayoutAsync();

    /// <summary>
    /// No line breaks are stored (§05 Q3), so Enter has nothing to do in a memo box. It moves to the
    /// next field instead, which is what a typist pressing it expects.
    /// </summary>
    private void OnMemoKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && sender is TextBox box)
        {
            box.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
            e.Handled = true;
        }
    }

    private static TextBox? MemoBoxOf(Thumb thumb) =>
        (thumb.Parent as Grid)?.Children.OfType<TextBox>().FirstOrDefault();

    /// <summary>A memo box may be as wide as the form pane and no wider (§08).</summary>
    private double PaneWidth() => FormScroller.ViewportWidth > 0 ? FormScroller.ViewportWidth : FormColumn.ActualWidth;

    /// <summary>
    /// Heights are stored in lines rather than pixels, so a box keeps its meaning at another font
    /// size. The chrome is the padding and border the text sits inside.
    /// </summary>
    private static double LineHeight(TextBox box) => box.FontFamily.LineSpacing * box.FontSize;

    private static double Chrome(TextBox box) =>
        box.Padding.Top + box.Padding.Bottom + box.BorderThickness.Top + box.BorderThickness.Bottom;

    private static double Lines(TextBox box, double pixels) => (pixels - Chrome(box)) / LineHeight(box);

    private static double Pixels(TextBox box, double lines) => (lines * LineHeight(box)) + Chrome(box);

    /// <summary>Each newly selected page opens showing all of itself, as the Groups preview does.</summary>
    private void OnImageChanged(object sender, System.Windows.Data.DataTransferEventArgs e)
    {
        var path = _editor.SelectedRow?.ImagePath;
        var missing = PageImage.Source is null && path is not null;
        ImageMissingText.Text = missing ? $"Image file not found:\n{path}" : "";
        ImageMissingText.Visibility = missing ? Visibility.Visible : Visibility.Collapsed;
        FitImage();
    }

    private void FitImage()
    {
        if (PageImage.Source is BitmapSource image)
        {
            var layout = ImageLayout.Of(image);
            _fit.Fit(layout.Width, layout.Height, ImageScroller.ViewportWidth, ImageScroller.ViewportHeight, layout.MaxScale);
        }

        ApplyZoom();
    }

    /// <summary>
    /// Dragging a divider resizes the image pane; the page keeps fitting until the user zooms.
    /// ScrollChanged, not SizeChanged: the ScrollViewer publishes its new viewport only afterwards,
    /// so a re-fit there would use the previous size.
    /// </summary>
    private void OnImageScrollerScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if ((e.ViewportWidthChange != 0 || e.ViewportHeightChange != 0) && PageImage.Source is BitmapSource image)
        {
            var layout = ImageLayout.Of(image);
            _fit.ViewportResized(layout.Width, layout.Height, e.ViewportWidth, e.ViewportHeight, layout.MaxScale);
            ApplyZoom();
        }
    }

    private void OnImageMouseWheel(object sender, MouseWheelEventArgs e)
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

    private void OnFit(object sender, RoutedEventArgs e) => FitImage();

    private void OnActualSize(object sender, RoutedEventArgs e)
    {
        _fit.Reset();
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
}
