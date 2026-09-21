using System.IO;
using System.Reflection;
using FgScanner.App.Services;
using FgScanner.App.Views;
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

    /// <summary>
    /// A real PNG, because the exporters decode what they are given — and a distinct one, because
    /// adoption drops a page whose checksum is already in the group and identical fixtures would
    /// quietly leave a six-page group holding one row.
    /// </summary>
    private static string MakePageIn(string directory, string name)
    {
        var path = Path.Combine(directory, name);
        using var bitmap = new System.Drawing.Bitmap(120, 160);
        using (var graphics = System.Drawing.Graphics.FromImage(bitmap))
        {
            graphics.Clear(System.Drawing.Color.White);
            using var font = new System.Drawing.Font(System.Drawing.FontFamily.GenericSansSerif, 10);
            graphics.DrawString(name, font, System.Drawing.Brushes.Black, 6, 40);
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

    // ---- which pages a send actually takes ----

    private async Task<GroupDetailViewModel> GroupOfAsync(
        int pages, bool evidenceProfile = false, bool committed = false)
    {
        var ct = TestContext.Current.CancellationToken;
        var dbPath = Path.Combine(_root, $"group-{evidenceProfile}-{committed}.db");
        using (var db = new FgScanner.Data.FgScannerDbContext(FgScanner.Data.DbBootstrapper.BuildOptions(dbPath)))
        {
            Microsoft.EntityFrameworkCore.RelationalDatabaseFacadeExtensions.Migrate(db.Database);
        }

        var factory = new TestFactory(dbPath);
        var groups = new FgScanner.Data.GroupService(factory);
        var profiles = new FgScanner.Data.ProfileService(factory);
        var trash = new FgScanner.Data.TrashService(factory, Path.Combine(_root, "trash"));
        var indexing = new FgScanner.Data.IndexingService(
            factory, profiles, new FgScanner.Core.Index.IndexExporter());

        var incoming = Path.Combine(_root, $"incoming-{evidenceProfile}-{committed}");
        Directory.CreateDirectory(incoming);

        (Guid, int)? profileRef = null;
        if (evidenceProfile)
        {
            var evidence = await profiles.EnsureEvidenceProfileAsync(ct);
            profileRef = (evidence.Id, (await profiles.GetLatestSchemaAsync(evidence.Id, ct)).Version);
        }
        else
        {
            var ordinary = await profiles.CreateAsync("Invoices", ct);
            profileRef = (ordinary.Id, (await profiles.GetLatestSchemaAsync(ordinary.Id, ct)).Version);
        }

        var group = await groups.CreateGroupAsync(
            Path.Combine(_root, $"groups-{evidenceProfile}-{committed}"), "Farm Folder", profileRef, ct);
        var files = Enumerable.Range(1, pages)
            .Select(i => MakePageIn(incoming, $"in_{i:00}.png"))
            .ToList();
        await groups.AdoptPagesAsync(group.Id, files, null, false, ct);
        if (committed)
        {
            // Committing is IndexingService's job and needs an index; the state is what the
            // warning turns on, so it is set directly here — in the row and in the instance the
            // view model is handed.
            await using (var db = new FgScanner.Data.FgScannerDbContext(
                FgScanner.Data.DbBootstrapper.BuildOptions(dbPath)))
            {
                var row = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions
                    .FirstAsync(db.Groups, g => g.Id == group.Id, ct);
                row.State = FgScanner.Data.GroupState.Committed;
                await db.SaveChangesAsync(ct);
            }

            group.State = FgScanner.Data.GroupState.Committed;
        }

        var toolset = new PageEditingToolset(
            new FgScanner.Scanning.Editing.ImageEditor(),
            new FgScanner.Scanning.Export.PdfExportService(),
            new FgScanner.Scanning.Export.ImageExportService(),
            new FgScanner.Scanning.Import.FileImportService(),
            new FgScanner.Data.ReorderService(factory),
            new FgScanner.Data.OcrQueueService(factory),
            new FgScanner.Data.AiQueueService(factory),
            new FgScanner.Data.RetroProcessService(factory, groups, trash),
            new FgScanner.Ai.CredentialStore(Path.Combine(_root, "cred"), useCredentialManager: false),
            new FgScanner.Data.AppSettingsService(factory),
            new FgScanner.Data.CaptureTriageService(factory, new FgScanner.Data.AppSettingsService(factory)),
            new FgScanner.Data.DuplicateFinder(factory));

        var vm = new GroupDetailViewModel(
            group, groups, profiles, indexing, trash, new ActiveGroupStore(), toolset);
        await vm.LoadAsync();
        return vm;
    }

    /// <summary>
    /// §05 N2a. Export treats a selection of one as "the whole group"
    /// (`ExportImagePaths`, deliberately left alone). Email must not: picking one page and
    /// sending sixty is the kind of mistake that is only noticed by the recipient, and this is
    /// case material. Email gets its own rule — any selection means exactly that selection.
    /// </summary>
    [Fact]
    public async Task Three_selected_rows_send_exactly_those_three_in_sequence_order()
    {
        var vm = await GroupOfAsync(6);
        foreach (var row in new[] { vm.Rows[4], vm.Rows[1], vm.Rows[3] })
        {
            vm.SelectedRows.Add(row);
        }

        Assert.Equal(
            [vm.Rows[1].ImagePath, vm.Rows[3].ImagePath, vm.Rows[4].ImagePath],
            vm.EmailImagePaths);
    }

    [Fact]
    public async Task One_selected_row_sends_that_row_and_not_the_group()
    {
        var vm = await GroupOfAsync(6);
        vm.SelectedRows.Add(vm.Rows[2]);

        Assert.Equal([vm.Rows[2].ImagePath], vm.EmailImagePaths);

        // The export rule is the one that widens to the group, and it stays that way.
        Assert.Equal(6, vm.Rows.Count);
    }

    [Fact]
    public async Task No_selection_sends_the_whole_group()
    {
        var vm = await GroupOfAsync(4);

        Assert.Empty(vm.SelectedRows);
        Assert.Equal(vm.Rows.Select(r => r.ImagePath), vm.EmailImagePaths);
    }

    /// <summary>
    /// The Scan page sends what is on screen, in the order it is on screen.
    ///
    /// The prompt for this phase says "ordered by sequence", which was true when it was written.
    /// Phase 25 changed it: a two-pass duplex stack is deliberately no longer in capture order,
    /// the sequence numbers record what came off the scanner first, and the list is the order —
    /// which is what SaveToGroupAsync reads. Ordering a send by sequence number would email a
    /// paired stack as fronts-then-backs while the screen showed sheet order.
    /// </summary>
    [Fact]
    public void The_scan_page_sends_its_pages_in_the_order_on_screen()
    {
        var scan = ScanViewModelFor([
            new FgScanner.Scanning.ScannedPage(MakePage("a.png"), 1),
            new FgScanner.Scanning.ScannedPage(MakePage("b.png"), 4),
            new FgScanner.Scanning.ScannedPage(MakePage("c.png"), 2),
        ]);

        Assert.Equal(scan.Pages.Select(p => p.FilePath), scan.EmailImagePaths);
    }

    private ScanViewModel ScanViewModelFor(IReadOnlyList<FgScanner.Scanning.ScannedPage> pages)
    {
        var dbPath = Path.Combine(_root, "scan.db");
        using (var db = new FgScanner.Data.FgScannerDbContext(FgScanner.Data.DbBootstrapper.BuildOptions(dbPath)))
        {
            Microsoft.EntityFrameworkCore.RelationalDatabaseFacadeExtensions.Migrate(db.Database);
        }

        var factory = new TestFactory(dbPath);
        var groups = new FgScanner.Data.GroupService(factory);
        var profiles = new FgScanner.Data.ProfileService(factory);
        var trash = new FgScanner.Data.TrashService(factory, Path.Combine(_root, "scantrash"));
        var scan = new ScanViewModel(
            new FgScanner.Scanning.FakeScanService(),
            new ScanSessionService(Path.Combine(_root, "recovery")),
            groups,
            new FgScanner.Data.IndexingService(factory, profiles, new FgScanner.Core.Index.IndexExporter()),
            new ActiveGroupStore(),
            new ProfileOcrTrigger(profiles, new FgScanner.Data.OcrQueueService(factory)),
            new PageEditingToolset(
                new FgScanner.Scanning.Editing.ImageEditor(),
                new FgScanner.Scanning.Export.PdfExportService(),
                new FgScanner.Scanning.Export.ImageExportService(),
                new FgScanner.Scanning.Import.FileImportService(),
                new FgScanner.Data.ReorderService(factory),
                new FgScanner.Data.OcrQueueService(factory),
                new FgScanner.Data.AiQueueService(factory),
                new FgScanner.Data.RetroProcessService(factory, groups, trash),
                new FgScanner.Ai.CredentialStore(Path.Combine(_root, "cred2"), useCredentialManager: false),
                new FgScanner.Data.AppSettingsService(factory),
                new FgScanner.Data.CaptureTriageService(factory, new FgScanner.Data.AppSettingsService(factory)),
                new FgScanner.Data.DuplicateFinder(factory)),
            trash);
        foreach (var page in pages)
        {
            scan.Pages.Add(page);
        }

        return scan;
    }

    // ---- the one-time evidence warning (§05 Q2b) ----

    /// <summary>
    /// Records what the dialog was asked to show, and answers as if the operator pressed
    /// Continue. Replaces the real dialog so a send can be walked without a window.
    /// </summary>
    private sealed class RecordingAsk
    {
        public List<bool> WarningShown { get; } = [];

        public bool Dismiss { get; set; }

        public (EmailAttachment Format, string Subject, bool DontWarnAgain)? Ask(
            int pageCount, string source, string subject, EmailAttachment format, bool warn)
        {
            WarningShown.Add(warn);
            return (format, subject, Dismiss);
        }
    }

    private async Task<(GroupDetailViewModel Vm, RecordingAsk Ask)> EvidenceGroupAsync(
        bool evidenceProfile = true, bool committed = true)
    {
        var vm = await GroupOfAsync(2, evidenceProfile, committed);
        var ask = new RecordingAsk();
        vm.Email.Ask = ask.Ask;
        return (vm, ask);
    }

    /// <summary>
    /// §05 Q2b. A send copies pages out of the folder whose checksums and `originals\` archive
    /// are its evidentiary integrity (ADR-0003). It is allowed — but the operator is told, once,
    /// what leaving that folder means. It never blocks the send.
    /// </summary>
    [Fact]
    public async Task A_committed_evidence_group_warns_on_the_first_send()
    {
        var (vm, ask) = await EvidenceGroupAsync();

        await vm.EmailCommand.ExecuteAsync(null);

        Assert.Equal([true], ask.WarningShown);
    }

    [Fact]
    public async Task The_warning_does_not_come_back_once_it_is_dismissed()
    {
        var (vm, ask) = await EvidenceGroupAsync();
        ask.Dismiss = true;

        await vm.EmailCommand.ExecuteAsync(null);
        await vm.EmailCommand.ExecuteAsync(null);

        Assert.Equal([true, false], ask.WarningShown);
    }

    /// <summary>Not every group is evidence; a warning shown everywhere is a warning nobody reads.</summary>
    [Fact]
    public async Task A_group_on_an_ordinary_profile_is_never_warned_about()
    {
        var (vm, ask) = await EvidenceGroupAsync(evidenceProfile: false);

        await vm.EmailCommand.ExecuteAsync(null);

        Assert.Equal([false], ask.WarningShown);
    }

    /// <summary>
    /// Before commit there is no index and no `originals\` archive to speak of — the folder is
    /// still being built, and the warning is about leaving a finished record.
    /// </summary>
    [Fact]
    public async Task An_uncommitted_group_is_never_warned_about()
    {
        var (vm, ask) = await EvidenceGroupAsync(committed: false);

        await vm.EmailCommand.ExecuteAsync(null);

        Assert.Equal([false], ask.WarningShown);
    }

    /// <summary>
    /// §14. Every send is logged: which surface, how many pages, which format, which route.
    ///
    /// And never a recipient. The app does not know one — the operator addresses the message in
    /// their own client — and it must not start keeping a record of who case material was sent
    /// to as a side effect of logging that it was sent. That would be a decision of its own, and
    /// nobody has made it. Asserted rather than eyeballed, because a log line grows by accident.
    /// </summary>
    [Fact]
    public async Task Every_send_is_logged_without_a_recipient()
    {
        var written = new List<string>();
        var previous = Serilog.Log.Logger;
        Serilog.Log.Logger = new Serilog.LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.Sink(new CapturingSink(written))
            .CreateLogger();
        try
        {
            var share = new FakeShareService(ShareRoute.Explorer);
            var sender = new EmailSender(Builder(), share, Settings())
            {
                Ask = (_, _, subject, format, _) => (format, subject, false),
            };

            await sender.SendAsync(
                [MakePage("log-1.png"), MakePage("log-2.png")],
                "Farm Folder",
                "the 2 selected pages",
                evidenceRecord: false,
                TestContext.Current.CancellationToken);
        }
        finally
        {
            (Serilog.Log.Logger as IDisposable)?.Dispose();
            Serilog.Log.Logger = previous;
        }

        var line = Assert.Single(written, l => l.Contains("Email:", StringComparison.Ordinal));
        Assert.Contains("2 page(s)", line, StringComparison.Ordinal);
        Assert.Contains("the 2 selected pages", line, StringComparison.Ordinal);
        Assert.Contains("Pdf", line, StringComparison.Ordinal);
        Assert.Contains("Explorer", line, StringComparison.Ordinal);

        // Nothing that could be an address, anywhere in the whole run's output.
        Assert.All(written, l => Assert.DoesNotContain("@", l, StringComparison.Ordinal));
        foreach (var word in new[] { "recipient", "mailto", "To:", "Cc" })
        {
            Assert.All(written, l => Assert.DoesNotContain(word, l, StringComparison.OrdinalIgnoreCase));
        }
    }

    private sealed class CapturingSink(List<string> lines) : Serilog.Core.ILogEventSink
    {
        public void Emit(Serilog.Events.LogEvent logEvent)
        {
            using var writer = new StringWriter();
            logEvent.RenderMessage(writer, System.Globalization.CultureInfo.InvariantCulture);
            lines.Add($"[{logEvent.Level}] {writer}");
        }
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
