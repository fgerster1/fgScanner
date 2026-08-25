using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Runtime.Versioning;

namespace FgScanner.Scanning.Capture;

/// <summary>
/// Perceptual image hashing (dHash) for "is this the same page scanned twice?".
///
/// Hand-rolled deliberately: the obvious libraries for this are ImageSharp and Emgu.CV, both of
/// which are on the CLAUDE.md forbidden list. dHash suits the job anyway — it compares the sign of
/// horizontal brightness gradients, so it shrugs off rescaling, JPEG noise and the small exposure
/// differences between two passes of the same sheet.
///
/// 256 bits (17x16), not the textbook 64 (9x8). Scanned documents are mostly white with horizontal
/// bands of text, so at 9x8 two unrelated pages can agree on 85%+ of their bits purely because both
/// are "dark lines on white". A measured example: two synthetic pages with different text but bands
/// at the same heights scored 86% at 64 bits. More bits dilute that shared structure and let an 80%
/// threshold mean something.
///
/// It is NOT rotation invariant: a page scanned upside down hashes to something unrelated. That is
/// correct here — the two are genuinely different images until one is rotated.
/// </summary>
[SupportedOSPlatform("windows")]
public static class ImageHasher
{
    private const int Width = 17;
    private const int Height = 16;

    public const int Bits = (Width - 1) * Height;

    /// <summary>
    /// Default "probably the same page" threshold, measured rather than guessed.
    ///
    /// Over 12 synthetic text pages, comparing each page against a 0.75-rescaled, heavily
    /// recompressed copy of itself versus against an unrelated page:
    ///     same page      min 95.3%   avg 97.4%
    ///     different page max 91.8%   avg 89.3%
    ///
    /// Unrelated document scans already agree on ~89% of bits because every one of them is "dark
    /// text on white", so a threshold of 80% would report every page as a duplicate of every other.
    /// 93% sits inside the measured gap. The gap is narrow, which is why image similarity is only
    /// ever a hint here and OCR text similarity is the stronger signal when text exists.
    /// </summary>
    public const double DefaultThreshold = 0.93;

    private const int ByteCount = Bits / 8;

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
    public static double? Compare(string? left, string? right)
    {
        if (!TryParse(left, out var a) || !TryParse(right, out var b))
        {
            return null;
        }

        var differing = 0;
        for (var i = 0; i < ByteCount; i++)
        {
            differing += System.Numerics.BitOperations.PopCount((uint)(a[i] ^ b[i]));
        }

        return (Bits - differing) / (double)Bits;
    }

    private static bool TryParse(string? hash, out byte[] value)
    {
        value = [];
        if (hash is null || hash.Length != ByteCount * 2)
        {
            return false;
        }

        try
        {
            value = Convert.FromHexString(hash);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    // Rec. 601 luma: green dominates perceived brightness, so a plain average blurs real contrast.
    private static double Luminance(Color color) =>
        (0.299 * color.R) + (0.587 * color.G) + (0.114 * color.B);
}
