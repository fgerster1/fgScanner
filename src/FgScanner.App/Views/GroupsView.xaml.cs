using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using FgScanner.Core.Index;
using FgScanner.Data;

namespace FgScanner.App.Views;

public partial class GroupsView : UserControl
{
    private GroupDetailViewModel? _detail;

    private const string PreviewWidthKey = "Session.PreviewPanelWidth";
    private const string PreviewHeightKey = "Session.PreviewPanelHeight";

    public GroupsView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (DataContext is GroupsViewModel vm)
            {
                vm.PropertyChanged += OnViewModelPropertyChanged;
                HookDetail(vm.Detail);
                _ = RestorePanelSizesAsync(vm);
            }
        };
        Unloaded += (_, _) => SavePanelSizes();
    }

    /// <summary>
    /// A panel the user dragged wider that snaps back on the next launch has not really been made
    /// resizable. Stored as plain numbers rather than window state so a bad value cannot wedge the
    /// layout — anything unparseable or out of range falls back to the design size.
    /// </summary>
    private async Task RestorePanelSizesAsync(GroupsViewModel vm)
    {
        try
        {
            PreviewColumn.Width = await ReadLengthAsync(vm, PreviewWidthKey, 300, 200, 1600);
            PreviewRow.Height = await ReadLengthAsync(vm, PreviewHeightKey, 190, 90, 2000);
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Restoring preview panel sizes");
        }
    }

    private static async Task<GridLength> ReadLengthAsync(
        GroupsViewModel vm, string key, double fallback, double minimum, double maximum)
    {
        var stored = await vm.Settings.GetAsync(key, "");
        return double.TryParse(stored, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var value)
            && value >= minimum && value <= maximum
            ? new GridLength(value)
            : new GridLength(fallback);
    }

    /// <summary>
    /// Saved on release rather than only on unload: closing the app while still on this screen
    /// never unloads the view, and a size that survives only if you happen to navigate away first
    /// is not remembered in any sense the user would recognise.
    /// </summary>
    private void OnSplitterDragCompleted(
        object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e) => SavePanelSizes();

    private void SavePanelSizes()
    {
        if (DataContext is not GroupsViewModel vm)
        {
            return;
        }

        try
        {
            var width = PreviewColumn.ActualWidth;
            var height = PreviewRow.ActualHeight;
            if (width > 0 && height > 0)
            {
                _ = vm.Settings.SetAsync(
                    PreviewWidthKey, width.ToString("0", System.Globalization.CultureInfo.InvariantCulture));
                _ = vm.Settings.SetAsync(
                    PreviewHeightKey, height.ToString("0", System.Globalization.CultureInfo.InvariantCulture));
            }
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Saving preview panel sizes");
        }
    }

    /// <summary>
    /// Selects the group under the cursor before its context menu opens. WPF does not select on
    /// right-click, and every item in that menu acts on the selected group — so without this,
    /// right-clicking one group while another is selected acts on the other one.
    /// </summary>
    private void OnGroupRightClicked(object sender, MouseButtonEventArgs e)
    {
        if (sender is ListBoxItem item)
        {
            item.IsSelected = true;
        }
    }

    /// <summary>
    /// Suppresses the menu on empty space below the list. There is no group under the cursor
    /// there, so offering "Delete group…" would silently target whatever happened to be selected.
    /// </summary>
    private void OnGroupsContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source && FindListBoxItem(source) is null)
        {
            e.Handled = true;
        }
    }

    private static ListBoxItem? FindListBoxItem(DependencyObject? source)
    {
        while (source is not null and not ListBoxItem)
        {
            source = source is System.Windows.Media.Visual or System.Windows.Media.Media3D.Visual3D
                ? System.Windows.Media.VisualTreeHelper.GetParent(source)
                : LogicalTreeHelper.GetParent(source);
        }

        return source as ListBoxItem;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(GroupsViewModel.Detail) && sender is GroupsViewModel vm)
        {
            HookDetail(vm.Detail);
        }
    }

    /// <summary>Mirrors the grid's multi-selection into the VM for apply-to-selected edits.</summary>
    private void OnGridSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_detail is null)
        {
            return;
        }

        _detail.SelectedRows.Clear();
        foreach (var item in EntryGrid.SelectedItems)
        {
            if (item is DocumentRow row)
            {
                _detail.SelectedRows.Add(row);
            }
        }
    }

    /// <summary>Drag-out: dragging the preview hands the page file to Explorer or another app.</summary>
    private readonly ZoomController _previewZoom = new();
    private Point? _dragOrigin;

    private void OnThumbnailMouseMove(object sender, MouseEventArgs e)
    {
        // Only start a drag once the pointer has actually travelled. DoDragDrop on any movement
        // swallows the second click of a double-click, which is how the preview is opened.
        if (e.LeftButton != MouseButtonState.Pressed || _dragOrigin is not { } origin)
        {
            return;
        }

        var moved = e.GetPosition(this) - origin;
        if (Math.Abs(moved.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(moved.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        if (_detail?.SelectedRow is { } row && System.IO.File.Exists(row.ImagePath))
        {
            _dragOrigin = null;
            var data = new DataObject(DataFormats.FileDrop, new[] { row.ImagePath });
            DragDrop.DoDragDrop((DependencyObject)sender, data, DragDropEffects.Copy);
        }
    }

    private void OnPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            _dragOrigin = null;
            _detail?.OpenPageViewerCommand.Execute(null);
            e.Handled = true;
            return;
        }

        _dragOrigin = e.GetPosition(this);
    }

    private void OnPreviewMouseUp(object sender, MouseButtonEventArgs e) => _dragOrigin = null;

    private void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (Keyboard.Modifiers != ModifierKeys.Control)
        {
            return;
        }

        if (e.Delta > 0)
        {
            _previewZoom.In();
        }
        else
        {
            _previewZoom.Out();
        }

        ApplyPreviewZoom();
        e.Handled = true;
    }

    private void OnPreviewZoomIn(object sender, RoutedEventArgs e)
    {
        _previewZoom.In();
        ApplyPreviewZoom();
    }

    private void OnPreviewZoomOut(object sender, RoutedEventArgs e)
    {
        _previewZoom.Out();
        ApplyPreviewZoom();
    }

    /// <summary>
    /// Each newly selected page opens showing all of itself. Without this the preview would start
    /// at 1:1 — a 1200px decode in a 300px panel, scrolled to the middle of the paper.
    /// </summary>
    private void OnPreviewImageChanged(object sender, DataTransferEventArgs e) => FitPreview();

    private void OnPreviewFit(object sender, RoutedEventArgs e) => FitPreview();

    private void FitPreview()
    {
        if (PreviewImage.Source is System.Windows.Media.Imaging.BitmapSource image)
        {
            _previewZoom.Fit(
                image.PixelWidth, image.PixelHeight,
                PreviewScroller.ViewportWidth, PreviewScroller.ViewportHeight);
        }

        ApplyPreviewZoom();
    }

    private void ApplyPreviewZoom()
    {
        PreviewScale.ScaleX = _previewZoom.Scale;
        PreviewScale.ScaleY = _previewZoom.Scale;
    }

    private void HookDetail(GroupDetailViewModel? detail)
    {
        if (_detail is not null)
        {
            _detail.SchemaLoaded -= RebuildColumns;
        }

        _detail = detail;
        if (_detail is not null)
        {
            _detail.SchemaLoaded += RebuildColumns;
            RebuildColumns();
        }
    }

    /// <summary>Fixed columns + one editable column per schema field (dynamic — schema differs per profile).</summary>
    private void RebuildColumns()
    {
        EntryGrid.Columns.Clear();
        EntryGrid.Columns.Add(new DataGridTextColumn
        {
            Header = "#",
            Binding = new Binding(nameof(DocumentRow.Sequence)),
            IsReadOnly = true,
            Width = 36,
        });
        EntryGrid.Columns.Add(new DataGridTextColumn
        {
            Header = "Image",
            Binding = new Binding(nameof(DocumentRow.ImageName)),
            IsReadOnly = true,
            Width = 130,
        });
        EntryGrid.Columns.Add(new DataGridTextColumn
        {
            Header = "OCRed",
            Binding = new Binding(nameof(DocumentRow.OcrStatus)),
            IsReadOnly = true,
            Width = 60,
        });
        EntryGrid.Columns.Add(new DataGridTextColumn
        {
            Header = "AI",
            Binding = new Binding(nameof(DocumentRow.AiStatus)),
            IsReadOnly = true,
            Width = 60,
        });

        foreach (var field in _detail?.Fields ?? [])
        {
            var binding = new Binding($"Values[{field.Name}]")
            {
                Mode = BindingMode.TwoWay,
                UpdateSourceTrigger = UpdateSourceTrigger.LostFocus,
                ValidatesOnNotifyDataErrors = true,
            };
            var isBatch = field.Scope == FieldScope.Batch;
            if (field.Type == FieldType.List)
            {
                var column = new DataGridComboBoxColumn
                {
                    Header = field.Name + (field.Required ? " *" : ""),
                    SelectedItemBinding = binding,
                    ItemsSource = IndexingService.ParseChoices(field.ListChoicesJson),
                    Width = new DataGridLength(1, DataGridLengthUnitType.Star),
                    IsReadOnly = isBatch,
                };
                EntryGrid.Columns.Add(column);
            }
            else
            {
                EntryGrid.Columns.Add(new DataGridTextColumn
                {
                    Header = field.Name + (field.Required ? " *" : ""),
                    Binding = binding,
                    Width = new DataGridLength(1, DataGridLengthUnitType.Star),
                    IsReadOnly = isBatch,
                });
            }
        }
    }
}

/// <summary>Visible when a bound count/length is greater than zero.</summary>
public sealed class CountToVisibilityConverter : IValueConverter
{
    public static CountToVisibilityConverter Instance { get; } = new();

    public object Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) =>
        value is int count && count > 0 ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Shows an element only while its bound text is non-empty.</summary>
public sealed class StringToVisibilityConverter : IValueConverter
{
    public static StringToVisibilityConverter Instance { get; } = new();

    public object Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) =>
        string.IsNullOrWhiteSpace(value as string)
            ? System.Windows.Visibility.Collapsed
            : System.Windows.Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) =>
        throw new NotSupportedException();
}
