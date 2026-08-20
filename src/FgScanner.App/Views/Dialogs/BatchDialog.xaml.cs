using System.Globalization;
using System.Windows;

namespace FgScanner.App.Views.Dialogs;

public enum BatchMode
{
    SinglePass,
    MultipleWithPrompt,
    MultipleWithDelay,
}

public partial class BatchDialog : Window
{
    public BatchDialog() => InitializeComponent();

    public BatchMode Mode =>
        ModePrompt.IsChecked == true ? BatchMode.MultipleWithPrompt
        : ModeDelay.IsChecked == true ? BatchMode.MultipleWithDelay
        : BatchMode.SinglePass;

    public int Count => Mode == BatchMode.SinglePass ? 1 : Math.Max(1, ParseInt(CountBox.Text, 2));

    public int DelaySeconds => Math.Max(1, ParseInt(DelayBox.Text, 5));

    private static int ParseInt(string text, int fallback) =>
        int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : fallback;

    private void OnStart(object sender, RoutedEventArgs e) => DialogResult = true;
}
