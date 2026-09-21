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

    private RecordEditorLayout _layout = new(0, 0);

    /// <summary>Set once the operator has moved something, so a restore arriving late cannot undo it.</summary>
    private bool _layoutTouched;

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
    /// Delete removes the page — except where the key belongs to the text being typed. A TextBox
    /// consumes it itself, but a ComboBox does not: tabbing to a list field and pressing Delete to
    /// clear the choice would otherwise send the page to the Trash with no confirmation.
    /// </summary>
    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Delete || EditingText() || FocusIsInTheForm())
        {
            return;
        }

        if (_editor.DeletePageCommand.CanExecute(null))
        {
            _editor.DeletePageCommand.Execute(null);
        }

        e.Handled = true;
    }

    private static bool EditingText() =>
        Keyboard.FocusedElement is System.Windows.Controls.Primitives.TextBoxBase;

    private bool FocusIsInTheForm() =>
        Keyboard.FocusedElement is System.Windows.Media.Visual focused && FormScroller.IsAncestorOf(focused);

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
            // The panes' own room, not the window's: the window also carries the toolbar, the status
            // line and the margins, so clamping against it would restore a top pane taller than its
            // row can be and squeeze the page list below the minimum the store believes it kept.
            _layout = await _editor.Layout.LoadAsync(_editor.Group.Id, TopGrid.ActualWidth, PaneGrid.ActualHeight);

            // A pane not yet measured reports zero room, and half of nothing would open the form at
            // its minimum. A divider dragged while this read was in flight wins: restoring over it
            // would undo a drag the operator has already watched take effect.
            if (_layout.FormWidth > 0 && _layout.TopHeight > 0 && !_layoutTouched)
            {
                FormColumn.Width = new GridLength(_layout.FormWidth);
                TopRow.Height = new GridLength(_layout.TopHeight);
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
    private void OnSplitterDragCompleted(object sender, DragCompletedEventArgs e)
    {
        _layoutTouched = true;
        _ = SaveLayoutAsync();
    }

    private async Task SaveLayoutAsync()
    {
        try
        {
            _layout = new RecordEditorLayout(FormColumn.ActualWidth, TopRow.ActualHeight);
            await _editor.Layout.SaveAsync(_editor.Group.Id, _layout);
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Saving the record editor layout for group {GroupId}", _editor.Group.Id);
        }
    }

    /// <summary>
    /// No line breaks are stored (§05 Q3), so Enter has nothing to do in any text field. It moves to the
    /// next field instead, which is what a typist pressing it expects.
    /// </summary>
    private void OnFieldKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && sender is TextBox box)
        {
            box.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));

            // Enter walks the form and must not walk out of it. Past the last field the next stop
            // is a toolbar button, where Delete no longer clears a value — it sends the page to
            // the Trash (OnPreviewKeyDown), and an operator who thinks they are still typing has
            // no reason to expect that.
            if (!FocusIsInTheForm())
            {
                box.Focus();
            }

            e.Handled = true;
        }
    }

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
