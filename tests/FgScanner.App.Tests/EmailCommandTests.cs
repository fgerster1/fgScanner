using System.IO;
using System.Reflection;
using FgScanner.App.Services;
using FgScanner.Core.Sharing;
using Xunit;

namespace FgScanner.App.Tests;

/// <summary>
/// The share service hands files to whatever mail path the station has and returns. It is the
/// first thing in this app that moves case material out of the folder whose checksums are its
/// evidentiary integrity, so two properties matter more than any feature: it never transmits,
/// and when there is no mail path at all it says so in a sentence rather than a code.
/// </summary>
public sealed class EmailCommandTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "fgscanner-tests", Guid.NewGuid().ToString("N"));

    public EmailCommandTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    /// <summary>
    /// Stands in for the shell the way FakeScanService stands in for hardware: it records what it
    /// was asked to share and which route it was told to report, and it cannot send either.
    /// </summary>
    private sealed class FakeShareService(ShareRoute route) : IShareService
    {
        public List<ShareRequest> Opened { get; } = [];

        public ShareOutcome Open(ShareRequest request)
        {
            Opened.Add(request);
            return new ShareOutcome(route, $"opened via {route}");
        }
    }

    private string MakePage(string name)
    {
        var path = Path.Combine(_root, name);
        File.WriteAllText(path, name);
        return path;
    }

    private ShareRequest Request() => new([MakePage("page-1.png"), MakePage("page-2.png")], "Two pages");

    /// <summary>
    /// AC-5. The operator presses Send in their own mail client, under their own identity, or
    /// nothing leaves the machine. Nothing in this app may claim otherwise, so the interface is
    /// not allowed to grow a method that sends — a later session must not add one casually.
    /// </summary>
    [Fact]
    public void The_share_service_is_never_asked_to_send()
    {
        var fake = new FakeShareService(ShareRoute.ShareSheet);

        var outcome = fake.Open(Request());

        Assert.Single(fake.Opened);
        Assert.Equal(ShareRoute.ShareSheet, outcome.Route);

        var sending = typeof(IShareService)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Select(m => m.Name)
            .Where(n => n.Contains("Send", StringComparison.OrdinalIgnoreCase)
                || n.Contains("Transmit", StringComparison.OrdinalIgnoreCase)
                || n.Contains("Deliver", StringComparison.OrdinalIgnoreCase))
            .ToList();
        Assert.Empty(sending);
    }

    /// <summary>
    /// AC-6. With no share target and no MAPI client, the operator is told where the pages are
    /// and asked to attach them — not shown an HRESULT. The route delegates all fail here, which
    /// is exactly the station this was written on.
    /// </summary>
    [Fact]
    public void The_fallback_explains_itself()
    {
        var revealed = new List<string>();
        var service = new WindowsShareService(
            shareSheet: _ => false,
            mapi: _ => false,
            mapiAvailable: () => false,
            revealInExplorer: path => { revealed.Add(path); return true; });

        var request = Request();
        var outcome = service.Open(request);

        Assert.Equal(ShareRoute.Explorer, outcome.Route);
        Assert.Equal([request.FilePaths[0]], revealed);
        Assert.Contains(_root, outcome.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("attach", outcome.Message, StringComparison.OrdinalIgnoreCase);

        // Never an error code and never an exception's words.
        Assert.DoesNotContain("0x", outcome.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Exception", outcome.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("HRESULT", outcome.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Even the last route failing leaves a sentence, not a stack trace.</summary>
    [Fact]
    public void Every_route_failing_still_reads_as_a_sentence()
    {
        var service = new WindowsShareService(
            shareSheet: _ => throw new InvalidOperationException("no share target registered"),
            mapi: _ => throw new InvalidOperationException("MAPI32.DLL not found"),
            mapiAvailable: () => true,
            revealInExplorer: _ => throw new InvalidOperationException("explorer.exe is missing"));

        var outcome = service.Open(Request());

        Assert.Equal(ShareRoute.None, outcome.Route);
        Assert.DoesNotContain("0x", outcome.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Exception", outcome.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("MAPI32.DLL", outcome.Message, StringComparison.Ordinal);
        Assert.Contains(_root, outcome.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The routes are tried in the order §08 fixed, and the first that works wins.</summary>
    [Fact]
    public void The_share_sheet_is_tried_before_mapi_and_mapi_before_explorer()
    {
        var order = new List<string>();

        var sheetWins = new WindowsShareService(
            shareSheet: _ => { order.Add("sheet"); return true; },
            mapi: _ => { order.Add("mapi"); return true; },
            mapiAvailable: () => true,
            revealInExplorer: _ => { order.Add("explorer"); return true; });
        Assert.Equal(ShareRoute.ShareSheet, sheetWins.Open(Request()).Route);
        Assert.Equal(["sheet"], order);

        order.Clear();
        var mapiWins = new WindowsShareService(
            shareSheet: _ => { order.Add("sheet"); return false; },
            mapi: _ => { order.Add("mapi"); return true; },
            mapiAvailable: () => true,
            revealInExplorer: _ => { order.Add("explorer"); return true; });
        Assert.Equal(ShareRoute.Mapi, mapiWins.Open(Request()).Route);
        Assert.Equal(["sheet", "mapi"], order);
    }

    /// <summary>
    /// AC-7's other half. A failing probe means MAPI is never reached at all — the point is that
    /// the station is asked about MAPI, never MAPI itself.
    /// </summary>
    [Fact]
    public void A_failing_probe_keeps_mapi_out_of_the_route_list()
    {
        var reached = false;
        var service = new WindowsShareService(
            shareSheet: _ => false,
            mapi: _ => { reached = true; return true; },
            mapiAvailable: () => false,
            revealInExplorer: _ => true);

        var outcome = service.Open(Request());

        Assert.False(reached);
        Assert.Equal(ShareRoute.Explorer, outcome.Route);
    }
}
