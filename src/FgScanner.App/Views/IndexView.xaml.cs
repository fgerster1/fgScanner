using System.Windows.Controls;

namespace FgScanner.App.Views;

public partial class IndexView : UserControl
{
    public IndexView()
    {
        InitializeComponent();
    }

    private async void OnOpenPackage(object sender, System.Windows.RoutedEventArgs e)
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
