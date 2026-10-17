using System.ComponentModel;
using System.Windows;
using Microsoft.Win32;

namespace FgScanner.App.Views.Dialogs;

/// <summary>
/// The Back up window (SPEC-SCANNER-2026-011). Modal, and it cannot be closed while a backup runs:
/// the app must not scan into a station that is being copied, and a window closed mid-copy would
/// leave Jim not knowing whether he has a backup.
/// </summary>
public partial class BackupWindow : Window
{
    private readonly BackupViewModel _viewModel;

    public BackupWindow(BackupViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        Loaded += async (_, _) =>
        {
            await viewModel.LoadAsync(CancellationToken.None);
            TargetBox.Focus();
        };
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        e.Cancel = _viewModel.IsRunning;
        base.OnClosing(e);
    }

    private void OnBrowse(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Choose the folder to back up to" };
        if (dialog.ShowDialog(this) == true)
        {
            _viewModel.UseFolder(dialog.FolderName);
        }
    }

    /// <summary>Shows the window; true when it ended with a backup that checked out.</summary>
    public static bool Show(Window? owner, BackupViewModel viewModel)
    {
        new BackupWindow(viewModel) { Owner = owner }.ShowDialog();
        return viewModel.Succeeded;
    }
}
