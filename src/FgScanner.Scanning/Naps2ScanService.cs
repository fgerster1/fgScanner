using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using NAPS2.Images;
using NAPS2.Images.Gdi;
using NAPS2.Scan;

namespace FgScanner.Scanning;

/// <summary>
/// Production IScanService over NAPS2.Sdk: WIA (default), TWAIN (via the bundled 32-bit worker
/// process — most vendor TWAIN drivers are still 32-bit), and eSCL for network scanners.
/// </summary>
public sealed class Naps2ScanService : IScanService, IDisposable
{
    private readonly ScanningContext _scanningContext;
    private readonly ScanController _controller;

    public Naps2ScanService()
    {
        _scanningContext = new ScanningContext(new GdiImageContext());
        if (!IsArm64)
        {
            // Spins up the prebuilt x86 NAPS2.Worker.exe so 32-bit TWAIN data sources load.
            _scanningContext.SetUpWin32Worker();
        }

        _controller = new ScanController(_scanningContext);
    }

    private static bool IsArm64 => RuntimeInformation.OSArchitecture == Architecture.Arm64;

    /// <inheritdoc />
    public IReadOnlyList<ScanDriver> AvailableDrivers =>
        IsArm64
            ? [ScanDriver.Wia, ScanDriver.Escl] // no ARM64 TWAIN drivers exist; NAPS2 gates it off too
            : [ScanDriver.Wia, ScanDriver.Twain, ScanDriver.Escl];

    public async Task<IReadOnlyList<ScanDeviceInfo>> ListDevicesAsync(
        ScanDriver driver, CancellationToken cancellationToken = default)
    {
        var devices = await _controller.GetDeviceList(ToNaps2Driver(driver)).ConfigureAwait(false);
        return [.. devices.Select(d => new ScanDeviceInfo(driver, d.ID, d.Name))];
    }

    public async IAsyncEnumerable<ScannedPage> ScanAsync(
        ScanProfileOptions options,
        IPageStorage storage,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (options.Device is null)
        {
            throw new InvalidOperationException("No scanner device selected.");
        }

        var naps2Options = BuildOptions(options);
        await foreach (var image in _controller.Scan(naps2Options, cancellationToken).ConfigureAwait(false))
        {
            using (image)
            {
                var path = storage.ReserveNextPagePath("jpg");
                image.Save(path);
                var page = new ScannedPage(path, ExtractSequence(path));
                storage.CommitPage(page);
                yield return page;
            }
        }
    }

    public void Dispose() => _scanningContext.Dispose();

    private static int ExtractSequence(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        var dash = name.LastIndexOf('-');
        return dash >= 0 && int.TryParse(name[(dash + 1)..], out var n) ? n : 0;
    }

    private static ScanOptions BuildOptions(ScanProfileOptions options) => new()
    {
        Device = new ScanDevice(ToNaps2Driver(options.Device!.Driver), options.Device.Id, options.Device.Name),
        Driver = ToNaps2Driver(options.Device.Driver),
        PaperSource = options.Source switch
        {
            ScanSource.Feeder => PaperSource.Feeder,
            ScanSource.Duplex => PaperSource.Duplex,
            _ => PaperSource.Flatbed,
        },
        Dpi = options.Dpi,
        BitDepth = options.BitDepth switch
        {
            ScanBitDepth.Grayscale => BitDepth.Grayscale,
            ScanBitDepth.BlackWhite => BitDepth.BlackAndWhite,
            _ => BitDepth.Color,
        },
        PageSize = options.PageSize switch
        {
            ScanPageSize.Legal => PageSize.Legal,
            ScanPageSize.A4 => PageSize.A4,
            ScanPageSize.A5 => PageSize.A5,
            ScanPageSize.A3 => PageSize.A3,
            ScanPageSize.B4 => PageSize.B4,
            ScanPageSize.B5 => PageSize.B5,
            _ => PageSize.Letter,
        },
        Brightness = options.Brightness,
        Contrast = options.Contrast,
    };

    private static Driver ToNaps2Driver(ScanDriver driver) => driver switch
    {
        ScanDriver.Twain => Driver.Twain,
        ScanDriver.Escl => Driver.Escl,
        _ => Driver.Wia,
    };
}
