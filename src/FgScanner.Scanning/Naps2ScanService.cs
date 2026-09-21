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

    /// <inheritdoc />
    public async Task<ScanCapabilities> GetCapabilitiesAsync(
        ScanDeviceInfo device,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var caps = await _controller.GetCaps(
                new ScanDevice(ToNaps2Driver(device.Driver), device.Id, device.Name),
                cancellationToken).ConfigureAwait(false);
            if (caps?.PaperSourceCaps is not { } paper)
            {
                return ScanCapabilities.Everything;
            }

            return new ScanCapabilities(paper.SupportsFlatbed, paper.SupportsFeeder, paper.SupportsDuplex);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Not every driver answers this, and some answer wrongly. The question is asked to
            // spare the operator a raw NoDuplexSupportException, so failing to ask must cost them
            // nothing: offer everything and let the scan itself report what the hardware does.
            // Swallowed rather than logged because this project takes no logging dependency; the
            // caller reports it, which is where the operator can see it.
            _ = ex;
            return ScanCapabilities.Everything;
        }
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

        // Enumerated by hand rather than with await foreach: the driver's exceptions have to be
        // translated into this app's vocabulary before they leave the scanning layer, and a catch
        // cannot wrap a yield.
        var pages = _controller.Scan(naps2Options, cancellationToken).GetAsyncEnumerator(cancellationToken);
        try
        {
            while (true)
            {
                ProcessedImage image;
                try
                {
                    if (!await pages.MoveNextAsync().ConfigureAwait(false))
                    {
                        break;
                    }

                    image = pages.Current;
                }
                catch (Exception ex) when (Translate(ex, options.Source) is { } named)
                {
                    throw named;
                }

                using (image)
                {
                    var path = storage.ReserveNextPagePath("jpg");
                    SaveWithCorrectedResolution(image, path, options.Dpi);
                    var page = new ScannedPage(path, ExtractSequence(path));
                    storage.CommitPage(page);
                    yield return page;
                }
            }
        }
        finally
        {
            await pages.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// The driver's word for a failure, in this app's words — or null to leave it alone, which
    /// sends it to the Scan page's last-resort handler unchanged. Only the failures an operator can
    /// actually do something about are named; inventing friendly text for the rest would hide the
    /// detail that makes an unknown fault diagnosable.
    /// </summary>
    private static ScanException? Translate(Exception ex, ScanSource source) => ex switch
    {
        NAPS2.Scan.Exceptions.NoDuplexSupportException =>
            new ScanSourceUnavailableException(ScanSource.Duplex, ex),
        NAPS2.Scan.Exceptions.NoFeederSupportException =>
            new ScanSourceUnavailableException(ScanSource.Feeder, ex),
        NAPS2.Scan.Exceptions.DeviceFeederEmptyException => new FeederEmptyException(ex),
        _ => null,
    };

    /// <summary>
    /// Writes the page, repairing a resolution the driver never set (see
    /// <see cref="ScanResolutionPolicy"/>). Each axis is judged on its own so a scanner reporting a
    /// genuine asymmetric resolution keeps it. Rendering here costs nothing extra — saving a
    /// <see cref="ProcessedImage"/> renders it anyway.
    /// </summary>
    private static void SaveWithCorrectedResolution(ProcessedImage image, string path, int requestedDpi)
    {
        using var rendered = image.Render();
        var x = ScanResolutionPolicy.ResolveDpiToStamp(rendered.HorizontalResolution, requestedDpi);
        var y = ScanResolutionPolicy.ResolveDpiToStamp(rendered.VerticalResolution, requestedDpi);
        if (x is not null || y is not null)
        {
            rendered.SetResolution(x ?? rendered.HorizontalResolution, y ?? rendered.VerticalResolution);
        }

        rendered.Save(path);
    }

    public void Dispose() => _scanningContext.Dispose();

    private static int ExtractSequence(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        var dash = name.LastIndexOf('-');
        return dash >= 0 && int.TryParse(name[(dash + 1)..], out var n) ? n : 0;
    }

    /// <summary>
    /// The operator's profile as the driver sees it. Internal rather than private so a test can
    /// assert what a capture actually sends: this is the legal-evidence capture path, and "the
    /// defaults are unchanged" is a claim worth pinning rather than reviewing by eye.
    /// </summary>
    internal static ScanOptions BuildOptions(ScanProfileOptions options) => new()
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

        // Only duplex has backs to turn over. Sending it for a single-sided source would be a
        // setting the operator cannot see the effect of, waiting to surprise them later.
        FlipDuplexedPages = options.Source == ScanSource.Duplex && options.FlipDuplexedPages,
    };

    private static Driver ToNaps2Driver(ScanDriver driver) => driver switch
    {
        ScanDriver.Twain => Driver.Twain,
        ScanDriver.Escl => Driver.Escl,
        _ => Driver.Wia,
    };
}
