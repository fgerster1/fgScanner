using System.Diagnostics;
using System.IO;
using FgScanner.Core.Sharing;
using Serilog;

namespace FgScanner.App.Services;

/// <summary>
/// Puts files in front of the operator's mail path, trying the three routes SPEC-2026-007 §08
/// fixed, in that order, and reporting which one opened. It never sends (AC-5) — see
/// <see cref="IShareService"/> for why that is a rule rather than an omission.
///
/// Each route is injectable so the chain can be tested without a window, a mail client or the
/// shell, the way <c>IScanService</c> keeps hardware out of the suite. The defaults are the real
/// ones, and every route is wrapped: a route that throws is a route that did not work, and the
/// next one is tried. The operator never sees an HRESULT (AC-6).
/// </summary>
public sealed class WindowsShareService(
    Func<ShareRequest, bool>? shareSheet = null,
    Func<ShareRequest, bool>? mapi = null,
    Func<bool>? mapiAvailable = null,
    Func<string, bool>? revealInExplorer = null) : IShareService
{
    private readonly Func<ShareRequest, bool> _shareSheet = shareSheet ?? ShareSheetRoute.TryOpen;
    private readonly Func<ShareRequest, bool> _mapi = mapi ?? SimpleMapiRoute.TryOpen;
    private readonly Func<bool> _mapiAvailable =
        mapiAvailable ?? (() => new MapiProbe(new WindowsRegistryReader()).IsAvailable());

    private readonly Func<string, bool> _revealInExplorer = revealInExplorer ?? RevealInExplorer;

    public ShareOutcome Open(ShareRequest request)
    {
        // Logged before any route is invoked, so a send that ends up attaching nothing still left
        // a record of what was asked for (§14). Never the subject: it is the operator's free text,
        // and naming who a message is for is an ordinary thing to type into it.
        Log.Information("Sharing {Count} file(s)", request.FilePaths.Count);

        if (Try(() => _shareSheet(request), "the Windows Share sheet"))
        {
            Log.Information("Shared {Count} page(s) via the Share sheet", request.FilePaths.Count);
            return new ShareOutcome(ShareRoute.ShareSheet, "Opened in your mail app.");
        }

        if (Try(_mapiAvailable, "the MAPI probe") && Try(() => _mapi(request), "Simple MAPI"))
        {
            Log.Information("Shared {Count} page(s) via Simple MAPI", request.FilePaths.Count);
            return new ShareOutcome(ShareRoute.Mapi, "Opened in your mail app.");
        }

        // Neither mail route worked. The pages exist and the operator is told where, in a sentence
        // — a raw code here would send them looking for a fault in FG Scanner instead of attaching
        // the file (§14, AC-6).
        var folder = Path.GetDirectoryName(request.FilePaths.Count > 0 ? request.FilePaths[0] : "") ?? "";
        var count = request.FilePaths.Count == 1 ? "1 page is" : $"{request.FilePaths.Count} pages are";
        Log.Warning("No mail route was available; falling back to Explorer for {Folder}", folder);

        if (request.FilePaths.Count > 0 && Try(() => _revealInExplorer(request.FilePaths[0]), "Explorer"))
        {
            return new ShareOutcome(
                ShareRoute.Explorer,
                $"No mail app was found. The {count} in {folder} — attach them to your message yourself.");
        }

        return new ShareOutcome(
            ShareRoute.None,
            $"No mail app was found, and the folder could not be opened. The {count} in {folder} — "
                + "attach them to your message yourself.");
    }

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

    private static bool RevealInExplorer(string path)
    {
        // /select, highlights the file itself rather than just opening the folder — the same call
        // the Groups page already makes (GroupDetailViewModel).
        using var started = Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            ArgumentList = { "/select,", path },
            UseShellExecute = false,
        });
        return true;
    }
}
