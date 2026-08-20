namespace FgScanner.Scanning;

public interface IScanService
{
    /// <summary>Drivers usable on this machine (TWAIN is unavailable on ARM64 — no ARM64 TWAIN drivers exist).</summary>
    IReadOnlyList<ScanDriver> AvailableDrivers { get; }

    Task<IReadOnlyList<ScanDeviceInfo>> ListDevicesAsync(ScanDriver driver, CancellationToken cancellationToken = default);

    /// <summary>
    /// Scans with the given options, writing each page into <paramref name="storage"/> as it arrives.
    /// Pages stream out one at a time so the UI can show them during a feeder run.
    /// </summary>
    IAsyncEnumerable<ScannedPage> ScanAsync(
        ScanProfileOptions options,
        IPageStorage storage,
        CancellationToken cancellationToken = default);
}

/// <summary>Where scanned pages get written. Implemented by the recovery session (crash-safe) and by tests.</summary>
public interface IPageStorage
{
    /// <summary>Reserves the path for the next page image and returns it (file not yet created).</summary>
    string ReserveNextPagePath(string extension);

    /// <summary>Called after the page file has been fully written.</summary>
    void CommitPage(ScannedPage page);
}
