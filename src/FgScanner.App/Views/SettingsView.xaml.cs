using System.Windows.Controls;

namespace FgScanner.App.Views;

public partial class SettingsView : UserControl
{
    public SettingsView()
    {
        InitializeComponent();

        // A DataGridColumn is not in the visual tree, so it cannot bind to the view model and its
        // items are set here instead. They must be the SAME type the column's SelectedItemBinding
        // writes — a list of stored FieldTypes under a binding to FieldDisplayType matches nothing,
        // which empties every cell in the column and silently drops every edit.
        TypeColumn.ItemsSource = FieldDisplayTypes.All;
    }
}
