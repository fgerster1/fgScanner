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

    /// <summary>Pages produced per scan run (feeder simulation).</summary>
    public int PageCount { get; init; } = 3;

    /// <summary>Delay between pages, to exercise streaming UI.</summary>
    public TimeSpan PageDelay { get; init; } = TimeSpan.Zero;

    /// <summary>When set, thrown after <see cref="ErrorAfterPages"/> pages — simulates a jam/driver failure.</summary>
    public Exception? Error { get; init; }

    public int ErrorAfterPages { get; init; }

    public Task<IReadOnlyList<ScanDeviceInfo>> ListDevicesAsync(
        ScanDriver driver, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<ScanDeviceInfo>>([.. Devices.Where(d => d.Driver == driver)]);

    public async IAsyncEnumerable<ScannedPage> ScanAsync(
        ScanProfileOptions options,
        IPageStorage storage,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var pagesThisRun = options.Source == ScanSource.Flatbed ? 1 : PageCount;
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
            WritePageImage(path, i, options);
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

    private static void WritePageImage(string path, int pageNumber, ScanProfileOptions options)
    {
        // Letter aspect at 1/4 scale keeps fixtures small but visually page-like.
        using var bitmap = new Bitmap(212, 275);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.Clear(options.BitDepth == ScanBitDepth.Color ? Color.Ivory : Color.White);
        using var font = new Font(FontFamily.GenericSansSerif, 24);
        graphics.DrawString($"Page {pageNumber}", font, Brushes.Black, 40, 110);
        graphics.DrawRectangle(Pens.Gray, 5, 5, 201, 264);
        bitmap.Save(path, ImageFormat.Png);
    }
}
