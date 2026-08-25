using System.Numerics;

namespace FgScanner.Core.Duplicates;

/// <summary>
/// Compares two dHash fingerprints. Computing one needs System.Drawing and a Windows target, so it
/// lives in FgScanner.Scanning; comparing two hex strings is arithmetic and belongs here, where the
/// data layer can reach it without taking a platform dependency.
/// </summary>
public static class ImageHashComparer
{
    /// <summary>Hash width in bits. 17x16 dHash — see ImageHasher for why not the textbook 64.</summary>
    public const int Bits = 256;

    private const int ByteCount = Bits / 8;

    /// <summary>
    /// Default "probably the same page" threshold, measured rather than guessed.
    ///
    /// Over 12 synthetic text pages, comparing each against a 0.75-rescaled, heavily recompressed
    /// copy of itself versus against an unrelated page:
    ///     same page      min 95.3%   avg 97.4%
    ///     different page max 91.8%   avg 89.3%
    ///
    /// Unrelated document scans already agree on ~89% of bits because every one of them is "dark
    /// text on white", so a threshold of 80% would report every page as a duplicate of every other.
    /// 93% sits inside the measured gap. That gap is narrow, which is why image similarity is only
    /// a hint and OCR text overlap is preferred wherever both pages have text.
    /// </summary>
    public const double DefaultThreshold = 0.93;

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
            differing += BitOperations.PopCount((uint)(a[i] ^ b[i]));
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
}
