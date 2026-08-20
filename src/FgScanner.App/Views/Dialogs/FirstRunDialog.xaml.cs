using System.Windows;

namespace FgScanner.App.Views.Dialogs;

public partial class FirstRunDialog : Window
{
    public FirstRunDialog(bool aiAvailable)
    {
        InitializeComponent();
        if (!aiAvailable)
        {
            OpenAiSetup.Visibility = Visibility.Collapsed;
        }
    }

    /// <summary>"system" | "light" | "dark" — persisted as the Ui.Theme setting.</summary>
    public string Theme => ThemeBox.SelectedIndex switch { 1 => "light", 2 => "dark", _ => "system" };

    public string? NewProfileName =>
        string.IsNullOrWhiteSpace(ProfileNameBox.Text) ? null : ProfileNameBox.Text.Trim();

    public bool WantsAiSetup => OpenAiSetup.IsChecked == true;

    private void OnStart(object sender, RoutedEventArgs e) => DialogResult = true;
}
