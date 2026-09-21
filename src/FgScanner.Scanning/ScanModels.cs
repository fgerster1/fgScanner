namespace FgScanner.Scanning;

public enum ScanDriver
{
    Wia,
    Twain,
    Escl,
}

public enum ScanSource
{
    Flatbed,
    Feeder,
    Duplex,
}

public enum ScanBitDepth
{
    Color,
    Grayscale,
    BlackWhite,
}

public enum ScanPageSize
{
    Letter,
    Legal,
    A4,
    A5,
    A3,
    B4,
    B5,
}

public sealed record ScanDeviceInfo(ScanDriver Driver, string Id, string Name);

public sealed record ScanProfileOptions
{
    public ScanDeviceInfo? Device { get; init; }
    public ScanSource Source { get; init; } = ScanSource.Flatbed;
    public int Dpi { get; init; } = 300;
    public ScanBitDepth BitDepth { get; init; } = ScanBitDepth.Color;
    public ScanPageSize PageSize { get; init; } = ScanPageSize.Letter;

    /// <summary>Range -1000..1000, NAPS2 convention.</summary>
    public int Brightness { get; init; }

    /// <summary>Range -1000..1000, NAPS2 convention.</summary>
    public int Contrast { get; init; }

    /// <summary>
    /// Turns the backs the right way up on a one-pass duplex scanner that hands them back upside
    /// down. **Defaults to false**: this record is on the legal-evidence capture path, so a new
    /// property that changed an existing capture would change every group scanned since — each one
    /// already committed, exported and handed to the portal. It is a correction for a particular
    /// scanner, not a default, and a scanner that already orients backs correctly would have every
    /// one of them rotated 180 degrees (SPEC-2026-006 §16 R2).
    /// </summary>
    public bool FlipDuplexedPages { get; init; }
}

/// <summary>A scanned page persisted to disk (inside the active recovery session folder).</summary>
public sealed record ScannedPage(string FilePath, int SequenceNumber);

/// <summary>
/// What a device says it can do, asked once when the device is chosen. Every value defaults to
/// supported: a driver that cannot answer, or answers wrongly — which some TWAIN drivers do — must
/// never be able to stop a scan the hardware can actually perform (SPEC-2026-006 §16 R5). The probe
/// removes a confusing error, it does not police the scanner.
/// </summary>
public sealed record ScanCapabilities(bool Flatbed = true, bool Feeder = true, bool Duplex = true)
{
    public static ScanCapabilities Everything { get; } = new();

    public bool Supports(ScanSource source) => source switch
    {
        ScanSource.Flatbed => Flatbed,
        ScanSource.Feeder => Feeder,
        ScanSource.Duplex => Duplex,
        _ => true,
    };
}
