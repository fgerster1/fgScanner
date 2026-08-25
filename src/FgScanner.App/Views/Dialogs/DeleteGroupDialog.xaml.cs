using System.Windows;
using FgScanner.Data;
using Microsoft.Win32;

namespace FgScanner.App.Views.Dialogs;

/// <summary>
/// "Delete group" means different things depending on what the scans are worth, so the choice is
/// the user's: forget the group, move its scans into another group, relocate the folder, or discard
/// it. Discarding still goes to the trash folder — a wrong click should not end a batch of scans.
/// </summary>
public partial class DeleteGroupDialog : Window
{
    public DeleteGroupDialog(Group group, int pageCount, IReadOnlyList<Group> otherGroups)
    {
        InitializeComponent();
        PromptText.Text = $"Delete the group \"{group.Name}\"?";
        CountText.Text = pageCount == 0
            ? $"It has no pages. Folder: {group.DirectoryPath}"
            : $"It holds {pageCount} page(s). Folder: {group.DirectoryPath}";

        TargetGroupBox.ItemsSource = otherGroups;
        TargetGroupBox.SelectedIndex = otherGroups.Count > 0 ? 0 : -1;
        if (otherGroups.Count == 0)
        {
            MoveGroupOption.IsEnabled = false;
        }
    }

    public GroupFilePolicy Policy => MoveFilesOption.IsChecked == true
        ? GroupFilePolicy.MoveFiles
        : DeleteOption.IsChecked == true
            ? GroupFilePolicy.DeleteFiles
            : GroupFilePolicy.KeepFiles;

    /// <summary>Non-null when the scans should be moved into another group before deleting.</summary>
    public Group? TargetGroup =>
        MoveGroupOption.IsChecked == true ? TargetGroupBox.SelectedItem as Group : null;

    public string MoveTo => MoveToBox.Text;

    private void OnBrowse(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Choose where to move the group's folder" };
        if (dialog.ShowDialog() == true)
        {
            MoveToBox.Text = dialog.FolderName;
        }
    }

    private void OnOk(object sender, RoutedEventArgs e)
    {
        if (MoveGroupOption.IsChecked == true && TargetGroup is null)
        {
            MessageBox.Show(this, "Pick the group to move the scans into.", "Delete group",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (MoveFilesOption.IsChecked == true && string.IsNullOrWhiteSpace(MoveToBox.Text))
        {
            MessageBox.Show(this, "Choose where to move the folder.", "Delete group",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        DialogResult = true;
    }
}
