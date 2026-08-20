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
}

/// <summary>A scanned page persisted to disk (inside the active recovery session folder).</summary>
public sealed record ScannedPage(string FilePath, int SequenceNumber);
