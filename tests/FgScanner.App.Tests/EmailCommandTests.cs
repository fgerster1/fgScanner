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

    private string MakePage(string name) => MakePageIn(_root, name);

    /// <summary>A real PNG, because the exporters decode what they are given.</summary>
    private static string MakePageIn(string directory, string name)
    {
        var path = Path.Combine(directory, name);
        using var bitmap = new System.Drawing.Bitmap(120, 160);
        using (var graphics = System.Drawing.Graphics.FromImage(bitmap))
        {
            graphics.Clear(System.Drawing.Color.White);
            graphics.FillRectangle(System.Drawing.Brushes.Black, 20, 20, 80, 12);
        }

        bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png);
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

    private sealed class TestFactory(string dbPath)
        : Microsoft.EntityFrameworkCore.IDbContextFactory<FgScanner.Data.FgScannerDbContext>
    {
        public FgScanner.Data.FgScannerDbContext CreateDbContext() =>
            new(FgScanner.Data.DbBootstrapper.BuildOptions(dbPath));
    }

    private FgScanner.Data.AppSettingsService Settings()
    {
        var dbPath = Path.Combine(_root, "settings.db");
        using (var db = new FgScanner.Data.FgScannerDbContext(FgScanner.Data.DbBootstrapper.BuildOptions(dbPath)))
        {
            Microsoft.EntityFrameworkCore.RelationalDatabaseFacadeExtensions.Migrate(db.Database);
        }

        return new FgScanner.Data.AppSettingsService(new TestFactory(dbPath));
    }

    /// <summary>
    /// §07: read fresh per send, and a value that cannot be read falls back to PDF rather than
    /// throwing. A corrupt setting must not stop an operator sending a page, and it must not
    /// quietly change what gets attached either.
    /// </summary>
    [Fact]
    public async Task The_attachment_format_is_read_fresh_and_falls_back_to_pdf()
    {
        var ct = TestContext.Current.CancellationToken;
        var settings = Settings();

        Assert.Equal(EmailAttachment.Pdf, await EmailSettings.ReadAsync(settings, ct));

        await EmailSettings.WriteAsync(settings, EmailAttachment.Images, ct);
        Assert.Equal(EmailAttachment.Images, await EmailSettings.ReadAsync(settings, ct));

        await settings.SetAsync(EmailSettings.AttachmentKey, "Fax", ct);
        Assert.Equal(EmailAttachment.Pdf, await EmailSettings.ReadAsync(settings, ct));
    }

    private AttachmentBuilder Builder() => new(
        new FgScanner.Scanning.Export.PdfExportService(),
        new FgScanner.Scanning.Export.ImageExportService(),
        Path.Combine(_root, "temp"));

    /// <summary>
    /// AC-9. The group folder is the record: its checksums and its `originals\` archive are what
    /// make it evidence (ADR-0003). Building something to attach must not add a file to it, move
    /// one, or touch one — the copy that leaves is a copy, and the folder is as it was.
    /// </summary>
    [Fact]
    public async Task Building_attachments_writes_nothing_into_the_group_folder()
    {
        var group = Path.Combine(_root, "group");
        Directory.CreateDirectory(group);
        var pages = new[] { MakePageIn(group, "scan_00001.png"), MakePageIn(group, "scan_00002.png") };
        var before = Directory.GetFiles(group, "*", SearchOption.AllDirectories)
            .ToDictionary(f => f, f => new FileInfo(f).LastWriteTimeUtc, StringComparer.OrdinalIgnoreCase);

        var built = await Builder().BuildAsync(pages, "Farm Folder", EmailAttachment.Pdf, TestContext.Current.CancellationToken);

        Assert.True(built.Ok, built.Message);
        Assert.NotEmpty(built.FilePaths);
        Assert.All(built.FilePaths, p => Assert.DoesNotContain(group, p, StringComparison.OrdinalIgnoreCase));

        var after = Directory.GetFiles(group, "*", SearchOption.AllDirectories)
            .ToDictionary(f => f, f => new FileInfo(f).LastWriteTimeUtc, StringComparer.OrdinalIgnoreCase);
        Assert.Equal(before.Keys.Order(), after.Keys.Order());
        Assert.All(before, kv => Assert.Equal(kv.Value, after[kv.Key]));
    }

    /// <summary>
    /// A page whose file has gone — moved, deleted, a drive unplugged — must name the page and
    /// share nothing. Sending a PDF silently short of a page is the failure mode that matters
    /// here: the message looks complete to whoever receives it.
    /// </summary>
    [Fact]
    public async Task A_missing_page_names_the_page_and_shares_nothing()
    {
        var present = MakePage("here.png");
        var missing = Path.Combine(_root, "gone.png");

        var built = await Builder().BuildAsync([present, missing], "Farm Folder", EmailAttachment.Pdf, TestContext.Current.CancellationToken);

        Assert.False(built.Ok);
        Assert.Empty(built.FilePaths);
        Assert.Contains("gone.png", built.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Exception", built.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>§15: warn above 20 MB, never refuse — the operator decides.</summary>
    [Fact]
    public async Task A_large_attachment_warns_but_still_goes()
    {
        var built = await Builder().BuildAsync([MakePage("one.png")], "Small", EmailAttachment.Pdf, TestContext.Current.CancellationToken);
        Assert.True(built.Ok);
        Assert.False(built.TooLarge);
        Assert.Equal("", built.Warning);

        var over = AttachmentBuilder.SizeWarning(21L * 1024 * 1024);
        Assert.Contains("20 MB", over, StringComparison.Ordinal);
        Assert.Contains("21", over, StringComparison.Ordinal);
        Assert.Equal("", AttachmentBuilder.SizeWarning(19L * 1024 * 1024));
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
