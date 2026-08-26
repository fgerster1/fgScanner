using System.Windows.Controls;

namespace FgScanner.App.Views;

public partial class TrashView : UserControl
{
    public TrashView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Mirrors the grid's selection into the view model. DataGrid.SelectedItems is not bindable,
    /// so the view has to push it — the same arrangement the entry grid uses.
    /// </summary>
    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DataContext is not TrashViewModel viewModel || sender is not DataGrid grid)
        {
            return;
        }

        viewModel.SelectedItems.Clear();
        foreach (var item in grid.SelectedItems.OfType<FgScanner.Data.TrashItem>())
        {
            viewModel.SelectedItems.Add(item);
        }
    }
}
