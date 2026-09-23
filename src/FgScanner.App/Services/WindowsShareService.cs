using System.Diagnostics;
using System.IO;
using FgScanner.Core.Sharing;
using Serilog;

namespace FgScanner.App.Services;

/// <summary>
/// Puts files in front of the operator's mail path and reports which route opened. It never
/// sends (AC-5) — see <see cref="IShareService"/> for why that is a rule rather than an omission.
///
/// The order: a registered MAPI client's own draft when the probe finds one, then the Share
/// sheet, then Explorer. §08 first put the Share sheet ahead of MAPI, but the sheet opens on every
/// Windows 10 and 11 machine, so MAPI was unreachable — and classic Outlook is not a share target,
/// so a classic-Outlook station got a sheet without Outlook in it. Franz moved MAPI first, gated
/// on the probe, so a station without a MAPI client (new Outlook, most others) is unaffected.
///
/// Each route is injectable so the chain can be tested without a window, a mail client or the
/// shell, the way <c>IScanService</c> keeps hardware out of the suite. The defaults are the real
/// ones, and every route is wrapped: a route that throws is a route that did not work, and the
/// next one is tried. The operator never sees an HRESULT (AC-6).
/// </summary>
public sealed class WindowsShareService(
    Func<ShareRequest, bool>? shareSheet = null,
    Func<ShareRequest, MapiDraft>? mapi = null,
    Func<bool>? mapiAvailable = null,
    Func<string, bool>? revealInExplorer = null,
    Func<string, bool>? openInBrowser = null,
    Func<IReadOnlyList<string>, bool>? copyToClipboard = null,
    Action<string, string>? announcePaste = null) : IShareService
{
    private readonly Func<ShareRequest, bool> _shareSheet = shareSheet ?? ShareSheetRoute.TryOpen;
    private readonly Func<ShareRequest, MapiDraft> _mapi = mapi ?? SimpleMapiRoute.TryOpen;
    private readonly Func<bool> _mapiAvailable =
        mapiAvailable ?? (() => new MapiProbe(new WindowsRegistryReader()).IsAvailable());

    private readonly Func<string, bool> _revealInExplorer = revealInExplorer ?? ExplorerSelect.Reveal;
    private readonly Func<string, bool> _openInBrowser = openInBrowser ?? OpenInBrowser;
    private readonly Func<IReadOnlyList<string>, bool> _copyToClipboard = copyToClipboard ?? CopyToClipboard;
    private readonly Action<string, string> _announcePaste = announcePaste ?? Views.Dialogs.PasteNotice.Show;

    public ShareOutcome Open(ShareRequest request)
    {
        // Logged before any route is invoked, so a send that ends up attaching nothing still left
        // a record of what was asked for (§14). Never the subject: it is the operator's free text,
        // and naming who a message is for is an ordinary thing to type into it.
        Log.Information("Sharing {Count} file(s)", request.FilePaths.Count);

        // A station whose mail is in a browser never tries the mail-app routes: the Share sheet
        // opens on every Windows machine, so it would always "work", and Gmail is never in it.
        if (request.Via != MailPath.MailApp)
        {
            return OpenWebmail(request);
        }

        // The probe only reads the registry; MAPI itself is never called on a station without a
        // registered client (AC-7).
        if (Try(_mapiAvailable, "the MAPI probe"))
        {
            // MAPI_DIALOG is modal, so by the time this returns the operator has already sent the
            // draft or thrown it away. Neither is "open", and a thrown-away draft attached nothing.
            var draft = MapiDraft.Failed;
            Try(() => (draft = _mapi(request)) != MapiDraft.Failed, "Simple MAPI");
            if (draft == MapiDraft.Sent)
            {
                Log.Information("Shared {Count} file(s) via Simple MAPI; the draft was sent", request.FilePaths.Count);
                return new ShareOutcome(ShareRoute.Mapi, "Your mail app reports the message as sent.");
            }

            if (draft == MapiDraft.Cancelled)
            {
                Log.Information("The Simple MAPI draft was closed without sending");
                return new ShareOutcome(
                    ShareRoute.Mapi,
                    "You closed the message without sending it — nothing left the app.",
                    Declined: true);
            }
        }

        if (Try(() => _shareSheet(request), "the Windows Share sheet"))
        {
            Log.Information("Shared {Count} file(s) via the Share sheet", request.FilePaths.Count);

            // The sheet opening is not a message opening: the operator still picks where it goes,
            // and may close it without choosing anything.
            // Where the file is, too: the sheet lists installed apps only, so an operator whose
            // mail is in a browser closes it and would otherwise have no idea where to look.
            var (sheetFolder, sheetCount, _) = Location(request);
            return new ShareOutcome(
                ShareRoute.ShareSheet,
                "The Windows Share sheet is open — choose your mail app there. "
                    + $"{sheetCount} in {sheetFolder} as well, if your mail is in a browser. {StaysUntilClose}");
        }

        // Neither mail route worked. The pages exist and the operator is told where, in a sentence
        // — a raw code here would send them looking for a fault in FG Scanner instead of attaching
        // the file (§14, AC-6).
        var (folder, count, pronoun) = Location(request);
        Log.Warning("No mail route was available; falling back to Explorer for {Folder}", folder);

        if (request.FilePaths.Count > 0 && Try(() => _revealInExplorer(request.FilePaths[0]), "Explorer"))
        {
            return new ShareOutcome(
                ShareRoute.Explorer,
                $"No mail app was found. {count} in {folder} — attach {pronoun} to your message yourself. "
                    + StaysUntilClose);
        }

        return new ShareOutcome(
            ShareRoute.None,
            $"No mail app was found, and the folder could not be opened. {count} in {folder} — "
                + $"attach {pronoun} to your message yourself. " + StaysUntilClose);
    }

    /// <summary>
    /// Gmail or Yahoo Mail: a new message in the browser with the subject filled in, and the files
    /// on the clipboard for the operator to paste in with Ctrl+V. No desktop app can attach a file
    /// to a web page, but a browser takes a pasted one (Franz, 2026-09-23). When the clipboard cannot
    /// be set, Explorer opens beside the message with the file selected, to be dragged in. Any part
    /// failing still leaves the operator told where the file is.
    /// </summary>
    private ShareOutcome OpenWebmail(ShareRequest request)
    {
        var name = WebmailCompose.Name(request.Via);
        var (folder, count, pronoun) = Location(request);
        var copied = request.FilePaths.Count > 0 && Try(() => _copyToClipboard(request.FilePaths), "the clipboard");

        // Explorer first, the message second: whichever opens last takes the foreground, and the
        // message is the window the operator works in. The other way round, Explorer covered the
        // compose window and the send looked as though nothing had happened.
        var shown = !copied
            && request.FilePaths.Count > 0 && Try(() => _revealInExplorer(request.FilePaths[0]), "Explorer");
        var browser = Try(
            () => _openInBrowser(WebmailCompose.Url(request.Via, request.Subject, request.Account)), name);

        // No message to paste into, so the files have to be found: Explorer after all.
        if (!browser && copied)
        {
            shown = Try(() => _revealInExplorer(request.FilePaths[0]), "Explorer");
        }

        Log.Information(
            "Webmail: {Service} compose opened {Browser}, clipboard {Clipboard}, Explorer opened {Explorer}, {Count} file(s)",
            name, browser, copied, shown, request.FilePaths.Count);

        if (browser && copied)
        {
            var paste = request.FilePaths.Count == 1
                ? "the file is ready to paste — click in the message and press Ctrl+V to attach it."
                : $"the {request.FilePaths.Count} files are ready to paste — click in the message and press "
                    + "Ctrl+V to attach them all.";

            // After the browser, so the notice is on top of the message it is about. A notice that
            // cannot be shown costs only the reminder; the status line says the same.
            Try(
                () =>
                {
                    _announcePaste(
                        Copied(request.FilePaths),
                        $"Click in the {name} message and press Ctrl+V to attach "
                            + (request.FilePaths.Count == 1 ? "it." : "them."));
                    return true;
                },
                "the paste notice");
            return new ShareOutcome(
                ShareRoute.Webmail, $"A new {name} message is open in your browser, and {paste} {StaysUntilClose}");
        }

        if (browser && shown)
        {
            // Explorer can select one file only, so several are named as being in the window.
            var drag = request.FilePaths.Count == 1
                ? "the file is selected in the Explorer window beside it — drag it into the message."
                : $"the {request.FilePaths.Count} files are in the Explorer window beside it — select them all "
                    + "and drag them into the message.";
            return new ShareOutcome(
                ShareRoute.Webmail, $"A new {name} message is open in your browser, and {drag} {StaysUntilClose}");
        }

        if (browser)
        {
            return new ShareOutcome(
                ShareRoute.Webmail,
                $"A new {name} message is open in your browser. {count} in {folder} — attach {pronoun} to "
                    + $"the message. {StaysUntilClose}");
        }

        if (shown)
        {
            return new ShareOutcome(
                ShareRoute.Explorer,
                $"{name} could not be opened in your browser. {count} in {folder}, in the Explorer window "
                    + $"that opened — start a new {name} message and drag {pronoun} in. {StaysUntilClose}");
        }

        return new ShareOutcome(
            ShareRoute.None,
            $"{name} could not be opened, and neither could the folder. {count} in {folder} — attach "
                + $"{pronoun} to a new message yourself. {StaysUntilClose}");
    }

    /// <summary>
    /// Files, never pages: this layer is handed attachments, and one PDF can hold sixty pages.
    /// Counting them as pages is how "20 pages attached … The 1 page is in" reached the screen.
    /// </summary>
    private static (string Folder, string Count, string Pronoun) Location(ShareRequest request)
    {
        var folder = Path.GetDirectoryName(request.FilePaths.Count > 0 ? request.FilePaths[0] : "") ?? "";
        return request.FilePaths.Count == 1
            ? (folder, "The attachment is", "it")
            : (folder, $"The {request.FilePaths.Count} attachments are", "them");
    }

    private static bool OpenInBrowser(string url)
    {
        // The default browser, through the shell — the same way the separator-sheet PDF opens.
        using var started = Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        return true;
    }

    /// <summary>What went on the clipboard, in the operator's words: one PDF, or the page images.</summary>
    private static string Copied(IReadOnlyList<string> files) =>
        files.Count == 1
            ? (string.Equals(Path.GetExtension(files[0]), ".pdf", StringComparison.OrdinalIgnoreCase)
                ? "PDF copied"
                : "Image copied")
            : $"{files.Count} images copied";

    /// <summary>
    /// The file list Explorer's Ctrl+C puts on the clipboard, which is what a browser reads on
    /// Ctrl+V. Runs on the UI thread, as the whole send does: the clipboard needs an STA thread.
    /// </summary>
    private static bool CopyToClipboard(IReadOnlyList<string> files)
    {
        var list = new System.Collections.Specialized.StringCollection();
        foreach (var file in files)
        {
            list.Add(file);
        }

        System.Windows.Clipboard.SetFileDropList(list);
        return true;
    }

    /// <summary>
    /// The attachment folder is removed when the app closes (AttachmentBuilder.CleanUp) and, for
    /// a session that was killed and never reached that, at the next startup. The operator sent to
    /// the folder is told both halves: promising only "until FG Scanner closes" claimed a deletion
    /// a killed session cannot perform.
    /// </summary>
    private const string StaysUntilClose =
        "It stays there until FG Scanner closes, and is removed the next time it starts.";

    /// <summary>
    /// A route that throws is a route that did not work. The reason goes to the log, where it can
    /// be read later, and never into the sentence the operator sees.
    /// </summary>
    private static bool Try(Func<bool> route, string what)
    {
        try
        {
            return route();
        }
        catch (Exception ex)
        {
            // The type, never the exception: a browser that fails to start throws a Win32Exception
            // quoting the whole command line — which for the webmail route is the compose URL,
            // carrying both the subject and the operator's own address (§14).
            Log.Warning("{Route} was not available ({Error})", what, ex.GetType().Name);
            return false;
        }
    }
}
