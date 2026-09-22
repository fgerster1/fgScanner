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

        // No ConfigureAwait(false) in this class: the mail route has to run on the UI thread,
        // where the Share sheet finds the app's window and MAPI's modal draft finds its parent.
        // The services it awaits may leave the UI thread inside themselves; these awaits bring the
        // send back. The export finishing on the pool used to carry the rest of the send there
        // with it, and the sheet was skipped with nothing on screen or in the log.
        //
        // Nothing above this catches: both callers are async commands, and an exception out of
        // one closes the app (§12, "named message; no crash"). A page that passes the existence
        // check and then will not decode, a full temp folder, a page locked by another program —
        // each is a send that did not happen, and the operator needs to be told that in a sentence.
        try
        {
            return await SendCoreAsync(pages, subject, source, evidenceRecord, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Log.Error(ex, "Email from {Source} failed while building or opening the message", source);
            return $"The attachment could not be built, so nothing left the app. ({ex.Message})";
        }
    }

    private async Task<string> SendCoreAsync(
        IReadOnlyList<string> pages,
        string subject,
        string source,
        bool evidenceRecord,
        CancellationToken cancellationToken)
    {
        // Read per send, never captured once: a format chosen in Settings reaches the next send
        // without a restart (ADR-0010).
        var remembered = await EmailSettings.ReadAsync(settings, cancellationToken);

        // §05 Q2b: allowed, but said once. The warning rides inside the dialog the operator was
        // going to see anyway — it is not a second confirmation and it cannot stop a send.
        var warn = evidenceRecord
            && !await EmailSettings.WarningSeenAsync(settings, cancellationToken);

        if (Ask(pages.Count, source, subject, remembered, warn) is not { } chosen)
        {
            return "Email cancelled — nothing left the app.";
        }

        if (warn && chosen.DontWarnAgain)
        {
            await EmailSettings.MarkWarningSeenAsync(settings, cancellationToken);
        }

        if (chosen.Format != remembered)
        {
            await EmailSettings.WriteAsync(settings, chosen.Format, cancellationToken);
        }

        var built = await attachments
            .BuildAsync(pages, chosen.Subject, chosen.Format, cancellationToken);
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

        // Only a route that took the files may say they were attached. The fallbacks attached
        // nothing, so they say what was made instead — pages here, files in the share layer's
        // sentence, since one PDF holds every page.
        var pagesWord = pages.Count == 1 ? "1 page" : $"{pages.Count} pages";
        var status = outcome.Route is ShareRoute.ShareSheet or ShareRoute.Mapi
            ? $"{pagesWord} attached. {outcome.Message}"
            : $"{pagesWord} {(pages.Count == 1 ? "was" : "were")} made into {Made(chosen.Format, built.FilePaths.Count)}. "
                + outcome.Message;
        return built.TooLarge ? $"{status} {built.Warning}" : status;
    }

    private static string Made(EmailAttachment format, int files) => format switch
    {
        EmailAttachment.Pdf => "one PDF",
        _ => files == 1 ? "1 image" : $"{files} images",
    };

    /// <summary>
    /// A sender that declines before building anything or showing a window. The toolset's default
    /// for construction sites that never meant to send — tests, chiefly — which must not reach the
    /// operator's shell, registry or temp folder by accident.
    /// </summary>
    public static EmailSender Unwired(
        Scanning.Export.PdfExportService pdf,
        AppSettingsService settings) =>
        new(new AttachmentBuilder(pdf), new NoMailPath(), settings)
        {
            Ask = (_, _, _, _, _) => null,
        };

    private sealed class NoMailPath : IShareService
    {
        public ShareOutcome Open(ShareRequest request) =>
            new(ShareRoute.None, "Email is not set up here — nothing left the app.");
    }
}
