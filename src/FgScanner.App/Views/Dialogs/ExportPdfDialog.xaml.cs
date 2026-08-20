using System.Windows;
using FgScanner.Scanning.Export;

namespace FgScanner.App.Views.Dialogs;

public partial class ExportPdfDialog : Window
{
    public ExportPdfDialog(string defaultTitle)
    {
        InitializeComponent();
        TitleBox.Text = defaultTitle;
    }

    public PdfExportOptions Options => new()
    {
        Title = TitleBox.Text,
        Author = AuthorBox.Text,
        Subject = SubjectBox.Text,
        Keywords = KeywordsBox.Text,
        Compat = (PdfCompatLevel)CompatBox.SelectedIndex,
        Security = EncryptCheck.IsChecked != true
            ? null
            : new PdfSecurity
            {
                OwnerPassword = OwnerBox.Text,
                UserPassword = UserBox.Text,
                AllowPrinting = AllowPrinting.IsChecked == true,
                AllowFullQualityPrinting = AllowFullPrint.IsChecked == true,
                AllowDocumentModification = AllowModify.IsChecked == true,
                AllowDocumentAssembly = AllowAssembly.IsChecked == true,
                AllowContentCopying = AllowCopy.IsChecked == true,
                AllowContentCopyingForAccessibility = AllowCopyAccess.IsChecked == true,
                AllowAnnotations = AllowAnnotate.IsChecked == true,
                AllowFormFilling = AllowForms.IsChecked == true,
            },
    };

    private void OnExport(object sender, RoutedEventArgs e) => DialogResult = true;
}
