using System.Windows;
using FgScanner.Data;

namespace FgScanner.App.Views.Dialogs;

/// <summary>Picks the group a set of scans should move into.</summary>
public partial class GroupPicker : Window
{
    public GroupPicker(string prompt, IReadOnlyList<Group> groups)
    {
        InitializeComponent();
        PromptText.Text = prompt;
        GroupBox.ItemsSource = groups;
        GroupBox.SelectedIndex = groups.Count > 0 ? 0 : -1;
    }

    public Group? Selected => GroupBox.SelectedItem as Group;

    private void OnOk(object sender, RoutedEventArgs e) => DialogResult = true;

    public static Group? Show(Window? owner, string prompt, IReadOnlyList<Group> groups)
    {
        var dialog = new GroupPicker(prompt, groups) { Owner = owner };
        return dialog.ShowDialog() == true ? dialog.Selected : null;
    }
}
