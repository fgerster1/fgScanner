using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using FgScanner.Data;

namespace FgScanner.App.Views;

public partial class GroupsView : UserControl
{
    private GroupDetailViewModel? _detail;

    public GroupsView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (DataContext is GroupsViewModel vm)
            {
                vm.PropertyChanged += OnViewModelPropertyChanged;
                HookDetail(vm.Detail);
            }
        };
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
    private void OnThumbnailMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed
            && _detail?.SelectedRow is { } row
            && System.IO.File.Exists(row.ImagePath))
        {
            var data = new DataObject(DataFormats.FileDrop, new[] { row.ImagePath });
            DragDrop.DoDragDrop((DependencyObject)sender, data, DragDropEffects.Copy);
        }
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
            if (field.Type == FieldType.List)
            {
                var column = new DataGridComboBoxColumn
                {
                    Header = field.Name + (field.Required ? " *" : ""),
                    SelectedItemBinding = binding,
                    ItemsSource = IndexingService.ParseChoices(field.ListChoicesJson),
                    Width = new DataGridLength(1, DataGridLengthUnitType.Star),
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
