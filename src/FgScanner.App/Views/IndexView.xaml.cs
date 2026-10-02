using System.Windows.Controls;

namespace FgScanner.App.Views;

public partial class IndexView : UserControl
{
    public IndexView()
    {
        InitializeComponent();
    }

    // The downloaded zip is the normal path (ADR-0015): picking the FILE
    // means Windows never offers the inside of the zip as if it were a folder.
    private async void OnOpenPackage(object sender, System.Windows.RoutedEventArgs e)
    {
        if (DataContext is not IndexViewModel viewModel)
        {
            return;
        }

        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Open the batch you downloaded",
            Filter = "Index batch (*.zip)|*.zip",
            InitialDirectory = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"),
        };
        if (dialog.ShowDialog() == true)
        {
            await viewModel.OpenPackageAsync(dialog.FileName);
        }
    }

    private async void OnOpenPackageFolder(object sender, System.Windows.RoutedEventArgs e)
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
