using FgScanner.Core.Sharing;
using FgScanner.Data;
using Serilog;

namespace FgScanner.App.Services;

/// <summary>
/// One send, end to end: ask what to attach, build it, hand it to the operator's mail path, and
/// come back with the sentence to show. Both call sites — the Scan page and a group — use this,
/// so they cannot drift apart on what a send means.
///
/// It never sends. The share service opens a message and returns; the operator presses Send in
/// their own client, under their own identity, having seen it (AC-5).
/// </summary>
public sealed class EmailSender(
    AttachmentBuilder attachments,
    IShareService share,
    AppSettingsService settings)
{
    /// <summary>
    /// Asks the operator what to attach. Replaceable so a send can be walked in a test without a
    /// window, the way <c>ConfirmDelete</c> and <c>ShowPageViewer</c> already are.
    /// </summary>
    public Func<int, string, string, EmailAttachment, bool,
        (EmailAttachment Format, string Subject, bool DontWarnAgain)?> Ask
    { get; set; }
        = Views.Dialogs.EmailDialog.Ask;

    public async Task<string> SendAsync(
        IReadOnlyList<string> pages,
        string subject,
        string source,
        bool evidenceRecord = false,
        CancellationToken cancellationToken = default)
    {
        if (pages.Count == 0)
        {
            return "There are no pages to email.";
        }

        // Read per send, never captured once: a format chosen in Settings reaches the next send
        // without a restart (ADR-0010).
        var remembered = await EmailSettings.ReadAsync(settings, cancellationToken).ConfigureAwait(false);

        // §05 Q2b: allowed, but said once. The warning rides inside the dialog the operator was
        // going to see anyway — it is not a second confirmation and it cannot stop a send.
        var warn = evidenceRecord
            && !await EmailSettings.WarningSeenAsync(settings, cancellationToken).ConfigureAwait(false);

        if (Ask(pages.Count, source, subject, remembered, warn) is not { } chosen)
        {
            return "Email cancelled — nothing left the app.";
        }

        if (warn && chosen.DontWarnAgain)
        {
            await EmailSettings.MarkWarningSeenAsync(settings, cancellationToken).ConfigureAwait(false);
        }

        if (chosen.Format != remembered)
        {
            await EmailSettings.WriteAsync(settings, chosen.Format, cancellationToken).ConfigureAwait(false);
        }

        var built = await attachments
            .BuildAsync(pages, chosen.Subject, chosen.Format, cancellationToken).ConfigureAwait(false);
        if (!built.Ok)
        {
            return built.Message;
        }

        var outcome = share.Open(new ShareRequest(built.FilePaths, chosen.Subject));

        // §14: which surface, how many pages, which format, which route. Never the recipient —
        // the app does not know it, and should not start recording who case material went to
        // without that being a decision of its own.
        Log.Information(
            "Email: {Count} page(s) from {Source} as {Format} via {Route}",
            pages.Count, source, chosen.Format, outcome.Route);

        var counted = pages.Count == 1 ? "1 page attached" : $"{pages.Count} pages attached";
        return built.TooLarge
            ? $"{counted}. {outcome.Message} {built.Warning}"
            : $"{counted}. {outcome.Message}";
    }
}
