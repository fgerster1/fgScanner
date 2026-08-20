using System.Windows;

namespace FgScanner.App.Views.Dialogs;

public enum ReprocessScope
{
    AllMissing,
    OcrOnly,
    AiOnly,
    RedoEverything,
}

public partial class ReprocessDialog : Window
{
    public ReprocessDialog() => InitializeComponent();

    public ReprocessScope Scope =>
        ScopeOcr.IsChecked == true ? ReprocessScope.OcrOnly
        : ScopeAi.IsChecked == true ? ReprocessScope.AiOnly
        : ScopeRedo.IsChecked == true ? ReprocessScope.RedoEverything
        : ReprocessScope.AllMissing;

    private void OnStart(object sender, RoutedEventArgs e) => DialogResult = true;
}
