using System.Windows;

namespace FgScanner.App.Views.Dialogs;

/// <summary>
/// The "Propose documents" preview, or its refusal. A MessageBox showed fifteen runs of a group's
/// hundreds and could not scroll.
/// </summary>
public partial class ProposalDialog : Window
{
    private ProposalDialog(string body, bool canApply)
    {
        InitializeComponent();
        BodyText.Text = body;
        if (!canApply)
        {
            ApplyButton.Visibility = Visibility.Collapsed;
            CancelButton.Content = "Close";
        }

        Loaded += (_, _) => CancelButton.Focus();
    }

    private void OnApply(object sender, RoutedEventArgs e) => DialogResult = true;

    /// <summary>True only when the operator pressed Apply.</summary>
    public static bool Confirm(string body) =>
        new ProposalDialog(body, canApply: true) { Owner = Application.Current?.MainWindow }.ShowDialog() == true;

    public static void Inform(string body) =>
        new ProposalDialog(body, canApply: false) { Owner = Application.Current?.MainWindow }.ShowDialog();
}
