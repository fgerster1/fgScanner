using System.Windows;

namespace FgScanner.App.Views.Dialogs;

public partial class InputDialog : Window
{
    public InputDialog(string title, string prompt, string initialValue = "")
    {
        InitializeComponent();
        Title = title;
        PromptText.Text = prompt;
        ValueBox.Text = initialValue;
        Loaded += (_, _) =>
        {
            ValueBox.SelectAll();
            ValueBox.Focus();
        };
    }

    public string Value => ValueBox.Text;

    private void OnOk(object sender, RoutedEventArgs e) => DialogResult = true;

    public static string? Show(Window? owner, string title, string prompt, string initialValue = "")
    {
        var dialog = new InputDialog(title, prompt, initialValue) { Owner = owner };
        return dialog.ShowDialog() == true ? dialog.Value : null;
    }
}
