using System.Windows;
using FgScanner.App.Services;

namespace FgScanner.App.Views.Dialogs;

/// <summary>
/// Asks what to attach, and says how many pages are going, before anything is built. The format
/// starts at whatever was chosen last (§05 Q3a, stored as <c>Email.Attachment</c>); the caller
/// reads it back and writes it.
///
/// "Continue" rather than "Send", because this app never sends — it opens a message and the
/// operator presses Send there (AC-5).
/// </summary>
public partial class EmailDialog : Window
{
    public EmailDialog()
    {
        InitializeComponent();
    }

    public EmailAttachment Format
    {
        get => FormatBox.SelectedIndex == 1 ? EmailAttachment.Images : EmailAttachment.Pdf;
        set => FormatBox.SelectedIndex = value == EmailAttachment.Images ? 1 : 0;
    }

    public string Subject
    {
        get => SubjectBox.Text;
        set => SubjectBox.Text = value;
    }

    public void Describe(int pageCount, string source) =>
        SummaryText.Text = pageCount == 1
            ? $"1 page from {source} will be attached."
            : $"{pageCount} pages from {source} will be attached.";

    /// <summary>Shows the dialog over the active window, or null if it was cancelled.</summary>
    public static (EmailAttachment Format, string Subject)? Ask(
        int pageCount, string source, string subject, EmailAttachment format)
    {
        var dialog = new EmailDialog { Owner = Application.Current?.MainWindow, Subject = subject };
        dialog.Format = format;
        dialog.Describe(pageCount, source);
        return dialog.ShowDialog() == true ? (dialog.Format, dialog.Subject) : null;
    }

    private void OnContinue(object sender, RoutedEventArgs e) => DialogResult = true;
}
