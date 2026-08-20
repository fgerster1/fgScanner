using System.Drawing;
using System.Runtime.Versioning;
using ZXing;
using ZXing.Windows.Compatibility;

namespace FgScanner.Scanning.Capture;

/// <summary>
/// Detects Patch-T separator sheets: a Code 39 barcode reading "PATCHT" — the encoding NAPS2 and
/// Paperless-ngx print on their separator sheets, so their sheets work here too (research-5 C1).
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class PatchTDetector(int maxDecodeWidth = 1600)
{
    public const string PatchTValue = "PATCHT";

    // A reader per decode: BarcodeReader carries mutable decode state and is not thread-safe.
    private static BarcodeReader CreateReader() => new()
    {
        AutoRotate = true,
        Options = new ZXing.Common.DecodingOptions
        {
            TryHarder = true,
            PossibleFormats = [BarcodeFormat.CODE_39],
        },
    };

    public bool IsSeparator(string imagePath)
    {
        // Barcode modules survive downscaling to ~1600px across; decoding a 600-DPI page at
        // full resolution is 10× slower for no extra hits.
        using var original = new Bitmap(imagePath);
        if (original.Width <= maxDecodeWidth)
        {
            return Decode(original);
        }

        var scale = (double)maxDecodeWidth / original.Width;
        using var scaled = new Bitmap(original, maxDecodeWidth, (int)(original.Height * scale));
        return Decode(scaled);
    }

    private static bool Decode(Bitmap bitmap)
    {
        var result = CreateReader().Decode(bitmap);
        return result is not null
            && string.Equals(result.Text?.Trim(), PatchTValue, StringComparison.OrdinalIgnoreCase);
    }
}
