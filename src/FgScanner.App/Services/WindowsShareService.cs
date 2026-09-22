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
    Func<string, bool>? revealInExplorer = null) : IShareService
{
    private readonly Func<ShareRequest, bool> _shareSheet = shareSheet ?? ShareSheetRoute.TryOpen;
    private readonly Func<ShareRequest, MapiDraft> _mapi = mapi ?? SimpleMapiRoute.TryOpen;
    private readonly Func<bool> _mapiAvailable =
        mapiAvailable ?? (() => new MapiProbe(new WindowsRegistryReader()).IsAvailable());

    private readonly Func<string, bool> _revealInExplorer = revealInExplorer ?? ExplorerSelect.Reveal;

    public ShareOutcome Open(ShareRequest request)
    {
        // Logged before any route is invoked, so a send that ends up attaching nothing still left
        // a record of what was asked for (§14). Never the subject: it is the operator's free text,
        // and naming who a message is for is an ordinary thing to type into it.
        Log.Information("Sharing {Count} file(s)", request.FilePaths.Count);

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
            return new ShareOutcome(
                ShareRoute.ShareSheet, "The Windows Share sheet is open — choose your mail app there.");
        }

        // Neither mail route worked. The pages exist and the operator is told where, in a sentence
        // — a raw code here would send them looking for a fault in FG Scanner instead of attaching
        // the file (§14, AC-6).
        var folder = Path.GetDirectoryName(request.FilePaths.Count > 0 ? request.FilePaths[0] : "") ?? "";
        // Files, never pages: this layer is handed attachments, and one PDF can hold sixty pages.
        // Counting them as pages is how "20 pages attached … The 1 page is in" reached the screen.
        var (count, pronoun) = request.FilePaths.Count == 1
            ? ("The attachment is", "it")
            : ($"The {request.FilePaths.Count} attachments are", "them");
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
    /// The attachment folder is removed when the app closes (AttachmentBuilder.CleanUp), so the
    /// operator sent to it is told — or they close FG Scanner and find the file gone.
    /// </summary>
    private const string StaysUntilClose = "It stays there until FG Scanner closes.";

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
            Log.Warning(ex, "{Route} was not available", what);
            return false;
        }
    }
}
