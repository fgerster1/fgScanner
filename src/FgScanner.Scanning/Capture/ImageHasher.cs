using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.Versioning;
using FgScanner.Core.Duplicates;

namespace FgScanner.Scanning.Capture;

/// <summary>
/// Computes perceptual image fingerprints (dHash) for "is this the same page scanned twice?".
/// Comparison lives in <see cref="ImageHashComparer"/>, which needs no platform support and so is
/// reachable from the data layer.
///
/// Hand-rolled deliberately: the obvious libraries for this are ImageSharp and Emgu.CV, both on the
/// CLAUDE.md forbidden list. dHash suits the job anyway — it compares the sign of horizontal
/// brightness gradients, so it shrugs off rescaling, JPEG noise and the small exposure differences
/// between two passes of the same sheet.
///
/// 256 bits (17x16), not the textbook 64 (9x8): scanned documents are mostly white with horizontal
/// bands of text, and at 9x8 two unrelated pages agree on ~86% of their bits purely because both
/// are "dark lines on white". More bits dilute that shared structure.
///
/// NOT rotation invariant: a page scanned upside down hashes to something unrelated. That is
/// correct here — the two are genuinely different images until one is rotated.
/// </summary>
[SupportedOSPlatform("windows")]
public static class ImageHasher
{
    private const int Width = 17;
    private const int Height = 16;

    private const int ByteCount = ImageHashComparer.Bits / 8;

    /// <summary>The measured "probably the same page" threshold; see ImageHashComparer.</summary>
    public const double DefaultThreshold = ImageHashComparer.DefaultThreshold;

    public static string Compute(string imagePath)
    {
        using var bitmap = new Bitmap(imagePath);
        return Compute(bitmap);
    }

    public static string Compute(Bitmap source)
    {
        using var small = new Bitmap(Width, Height);
        using (var graphics = Graphics.FromImage(small))
        {
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.DrawImage(source, 0, 0, Width, Height);
        }

        var bytes = new byte[ByteCount];
        var index = 0;
        for (var y = 0; y < Height; y++)
        {
            for (var x = 0; x < Width - 1; x++)
            {
                if (Luminance(small.GetPixel(x, y)) > Luminance(small.GetPixel(x + 1, y)))
                {
                    bytes[index / 8] |= (byte)(1 << (index % 8));
                }

                index++;
            }
        }

        return Convert.ToHexStringLower(bytes);
    }

    /// <summary>Fraction of matching bits, 0 to 1. Null when either hash is missing or malformed.</summary>
    public static double? Compare(string? left, string? right) => ImageHashComparer.Compare(left, right);

    // Rec. 601 luma: green dominates perceived brightness, so a plain average blurs real contrast.
    private static double Luminance(Color color) =>
        (0.299 * color.R) + (0.587 * color.G) + (0.114 * color.B);
}
