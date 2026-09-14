using System.Windows.Controls;
using System.Windows.Data;
using FgScanner.Core.Index;
using FgScanner.Data;

namespace FgScanner.App.Views;

/// <summary>
/// The entry grid's columns: fixed ones, then one editable column per schema field (dynamic, because
/// the schema differs per profile). Shared so the Groups page and the record editor build the same grid.
/// </summary>
public static class EntryGridColumns
{
    public static void Build(DataGrid grid, IReadOnlyList<FieldDefinition> fields)
    {
        grid.Columns.Clear();
        grid.Columns.Add(new DataGridTextColumn
        {
            Header = "#",
            Binding = new Binding(nameof(DocumentRow.Sequence)),
            IsReadOnly = true,
            Width = 36,
        });
        grid.Columns.Add(new DataGridTextColumn
        {
            Header = "Image",
            Binding = new Binding(nameof(DocumentRow.ImageName)),
            IsReadOnly = true,
            Width = 130,
        });
        grid.Columns.Add(new DataGridTextColumn
        {
            Header = "OCRed",
            Binding = new Binding(nameof(DocumentRow.OcrStatus)),
            IsReadOnly = true,
            Width = 60,
        });
        grid.Columns.Add(new DataGridTextColumn
        {
            Header = "AI",
            Binding = new Binding(nameof(DocumentRow.AiStatus)),
            IsReadOnly = true,
            Width = 60,
        });

        foreach (var field in fields)
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
                grid.Columns.Add(column);
            }
            else
            {
                grid.Columns.Add(new DataGridTextColumn
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
