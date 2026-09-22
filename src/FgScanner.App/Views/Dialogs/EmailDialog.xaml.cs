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

    /// <summary>Ticked only when the evidence warning was actually shown.</summary>
    public bool DontWarnAgain => EvidencePanel.Visibility == Visibility.Visible && DontWarnCheck.IsChecked == true;

    public void Describe(int pageCount, string source) =>
        SummaryText.Text = pageCount == 1
            ? $"1 page from {source} will be attached."
            : $"{pageCount} pages from {source} will be attached.";

    public void ShowEvidenceWarning() => EvidencePanel.Visibility = Visibility.Visible;

    /// <summary>Shows the dialog over the main window and returns what the operator chose.</summary>
    public static EmailChoice Ask(
        int pageCount, string source, string subject, EmailAttachment format, bool warnEvidence)
    {
        var dialog = new EmailDialog { Owner = Application.Current?.MainWindow, Subject = subject };
        dialog.Format = format;
        dialog.Describe(pageCount, source);
        if (warnEvidence)
        {
            dialog.ShowEvidenceWarning();
        }

        return dialog.ShowDialog() == true
            ? EmailChoice.Go(dialog.Format, dialog.Subject, dialog.DontWarnAgain)
            : EmailChoice.Cancel(dialog.DontWarnAgain);
    }

    private void OnContinue(object sender, RoutedEventArgs e) => DialogResult = true;
}
