using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.CompilerServices;

namespace FgScanner.Scanning;

/// <summary>
/// Hardware-free IScanService (NAPS2 MockScanBridge pattern): produces generated page images
/// so business logic and UI are fully testable/demoable without a scanner.
/// Select in the app with --fake-scanner.
/// </summary>
public sealed class FakeScanService : IScanService
{
    public IReadOnlyList<ScanDriver> AvailableDrivers { get; init; } = [ScanDriver.Wia, ScanDriver.Twain, ScanDriver.Escl];

    public IReadOnlyList<ScanDeviceInfo> Devices { get; init; } =
    [
        new(ScanDriver.Wia, "fake-wia-1", "Fake WIA Scanner"),
        new(ScanDriver.Twain, "fake-twain-1", "Fake TWAIN Scanner"),
        new(ScanDriver.Escl, "fake-escl-1", "Fake Network Scanner"),
    ];

    /// <summary>
    /// Pages produced per scan run (feeder simulation). Settable between runs, so a test can make
    /// the second pass of a stack come up short — a double feed or a sheet left in the tray, which
    /// is the ordinary failure a two-pass run has to survive.
    /// </summary>
    public int PageCount { get; set; } = 3;

    /// <summary>
    /// A count for each successive run, the last of them repeating; null leaves
    /// <see cref="PageCount"/> governing every run. It exists so the failures of a two-pass stack
    /// can be rehearsed without paper: [10, 9] is ten sheets with one pulled out before the backs,
    /// and [5, 4] is an odd stack whose last sheet is single-sided. Both are refusals, and a
    /// refusal that has never been seen on screen is a refusal nobody has read.
    /// </summary>
    public IReadOnlyList<int>? PagesPerRun { get; init; }

    /// <summary>Delay between pages, to exercise streaming UI.</summary>
    public TimeSpan PageDelay { get; init; } = TimeSpan.Zero;

    /// <summary>When set, thrown after <see cref="ErrorAfterPages"/> pages — simulates a jam/driver failure.</summary>
    public Exception? Error { get; init; }

    public int ErrorAfterPages { get; init; }

    /// <summary>
    /// What <see cref="GetCapabilitiesAsync"/> reports. Settable rather than init-only so a test
    /// can swap scanners the way an operator does, and everything is supported by default so no
    /// existing test changes behaviour by gaining a capability probe.
    /// </summary>
    public ScanCapabilities Capabilities { get; set; } = ScanCapabilities.Everything;

    /// <summary>When set, thrown from the capability probe — the driver that cannot answer.</summary>
    public Exception? CapabilitiesError { get; init; }

    /// <summary>
    /// Produces byte-identical pages: a stack of blank backs, which is what a real duplex run
    /// hands back and what no other fake scan can reproduce. Off by default, because the run
    /// number stamped into every page is deliberate — it stops consecutive scans colliding on
    /// checksum, which would make adoption drop them. Turning it on is how a test asks for
    /// exactly that collision.
    /// </summary>
    public bool BlankIdenticalPages { get; init; }

    /// <summary>How many times the device has been asked; a probe on the scan path shows up here.</summary>
    public int CapabilityProbes => _capabilityProbes;

    private int _capabilityProbes;

    private int _run;

    public Task<ScanCapabilities> GetCapabilitiesAsync(
        ScanDeviceInfo device, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _capabilityProbes);
        return CapabilitiesError is not null
            ? Task.FromException<ScanCapabilities>(CapabilitiesError)
            : Task.FromResult(Capabilities);
    }

    public Task<IReadOnlyList<ScanDeviceInfo>> ListDevicesAsync(
        ScanDriver driver, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<ScanDeviceInfo>>([.. Devices.Where(d => d.Driver == driver)]);

    public async IAsyncEnumerable<ScannedPage> ScanAsync(
        ScanProfileOptions options,
        IPageStorage storage,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // Each run's pages must differ from every earlier run's. A session that saves to a group
        // resets its page numbering, so drawing only the per-run index made consecutive scans
        // byte-identical — and adoption silently skips a page whose checksum is already in the
        // group. No real scanner hands back a second sheet identical to the first.
        var run = Interlocked.Increment(ref _run);
        var forThisRun = PagesPerRun is { Count: > 0 } counts
            ? counts[Math.Min(run - 1, counts.Count - 1)]
            : PageCount;
        var pagesThisRun = options.Source == ScanSource.Flatbed ? 1 : forThisRun;
        for (var i = 1; i <= pagesThisRun; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Error is not null && i > ErrorAfterPages)
            {
                throw Error;
            }

            if (PageDelay > TimeSpan.Zero)
            {
                await Task.Delay(PageDelay, cancellationToken).ConfigureAwait(false);
            }

            var path = storage.ReserveNextPagePath("png");
            WritePageImage(path, BlankIdenticalPages ? null : (run, i), options);
            var page = new ScannedPage(path, ExtractSequence(path));
            storage.CommitPage(page);
            yield return page;
        }
    }

    private static int ExtractSequence(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        var dash = name.LastIndexOf('-');
        return dash >= 0 && int.TryParse(name[(dash + 1)..], out var n) ? n : 0;
    }

    /// <summary>
    /// Writes a page. A null <paramref name="marks"/> leaves it blank and therefore identical to
    /// every other blank one — the stack of blank backs a duplex run produces.
    /// </summary>
    private static void WritePageImage(string path, (int Run, int Page)? marks, ScanProfileOptions options)
    {
        // Letter aspect at 1/4 scale keeps fixtures small but visually page-like.
        using var bitmap = new Bitmap(212, 275);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.Clear(options.BitDepth == ScanBitDepth.Color ? Color.Ivory : Color.White);
        if (marks is { } mark)
        {
            using var font = new Font(FontFamily.GenericSansSerif, 24);
            graphics.DrawString($"Page {mark.Page}", font, Brushes.Black, 40, 110);
            graphics.DrawString($"Sheet {mark.Run}", font, Brushes.DimGray, 40, 160);
        }

        graphics.DrawRectangle(Pens.Gray, 5, 5, 201, 264);
        bitmap.Save(path, ImageFormat.Png);
    }
}
