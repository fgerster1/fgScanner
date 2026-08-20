namespace FgScanner.Scanning.Editing;

/// <summary>
/// One editing operation on a page image. Values follow the NAPS2 convention of -1000..1000
/// (0 = unchanged) so profiles and the UI share one scale.
/// </summary>
public abstract record PageEdit
{
    /// <summary>Clockwise degrees; covers rotate left (-90), right (90), flip (180), and custom angles.</summary>
    public sealed record Rotate(double Degrees) : PageEdit;

    /// <summary>Auto-straighten via projection-profile skew estimation.</summary>
    public sealed record Deskew : PageEdit;

    /// <summary>Pixels trimmed from each side.</summary>
    public sealed record Crop(int Left, int Top, int Right, int Bottom) : PageEdit;

    public sealed record Brightness(int Value) : PageEdit;

    public sealed record Contrast(int Value) : PageEdit;

    public sealed record Hue(int Value) : PageEdit;

    public sealed record Saturation(int Value) : PageEdit;

    public sealed record Sharpen(int Value) : PageEdit;

    /// <summary>Binarize with the given threshold (-1000..1000, 0 = default midpoint).</summary>
    public sealed record BlackWhite(int Threshold) : PageEdit;
}
