using System.Windows;
using FgScanner.Scanning.Export;

namespace FgScanner.App.Views.Dialogs;

public partial class ExportImagesDialog : Window
{
    public ExportImagesDialog() => InitializeComponent();

    public string Pattern => PatternBox.Text;

    public ImageExportOptions Options => new()
    {
        Format = (ImageExportFormat)FormatBox.SelectedIndex,
        JpegQuality = (int)QualitySlider.Value,
        TiffCompression = (TiffCompression)TiffCompressionBox.SelectedIndex,
        TiffMultiPage = MultiPageCheck.IsChecked == true,
    };

    private void OnFormatChanged(object sender, RoutedEventArgs e)
    {
        if (JpegPanel is null)
        {
            return;
        }

        JpegPanel.Visibility = FormatBox.SelectedIndex == 0 ? Visibility.Visible : Visibility.Collapsed;
        TiffPanel.Visibility = FormatBox.SelectedIndex == 2 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnExport(object sender, RoutedEventArgs e) => DialogResult = true;
}
