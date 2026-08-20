using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.Versioning;

namespace FgScanner.Scanning.Capture;

/// <summary>
/// NAPS2-style blank detection (research-5 C2): a pixel is "ink" when its luminance falls below
/// the white threshold; the page is blank when ink coverage stays under the coverage threshold.
/// A 3% border margin is ignored — feeder shadows and punch holes live there.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class BlankPageDetector(int whiteThreshold = 70, double coveragePercent = 1.0)
{
    private const double MarginFraction = 0.03;

    // Sampling every nth pixel keeps a 300-DPI page under a few milliseconds; blank detection
    // needs coverage statistics, not exact counts.
    private const int SampleStride = 4;

    public bool IsBlank(string imagePath)
    {
        using var bitmap = new Bitmap(imagePath);
        return IsBlank(bitmap);
    }

    public unsafe bool IsBlank(Bitmap bitmap)
    {
        // Pixels darker than whiteThreshold% of full white count as ink; 70 ≈ NAPS2's default
        // sensitivity — printed text falls well below, scanner background stays above.
        var inkCutoff = 255 * whiteThreshold / 100;

        var marginX = (int)(bitmap.Width * MarginFraction);
        var marginY = (int)(bitmap.Height * MarginFraction);
        var rect = new Rectangle(
            marginX, marginY, bitmap.Width - (2 * marginX), bitmap.Height - (2 * marginY));
        if (rect.Width <= 0 || rect.Height <= 0)
        {
            return true;
        }

        var data = bitmap.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
        try
        {
            long sampled = 0, ink = 0;
            for (var y = 0; y < rect.Height; y += SampleStride)
            {
                var row = (byte*)data.Scan0 + ((long)y * data.Stride);
                for (var x = 0; x < rect.Width; x += SampleStride)
                {
                    var pixel = row + (x * 3);
                    // Integer Rec. 601 luma: (299R + 587G + 114B) / 1000; BGR byte order.
                    var luminance = ((299 * pixel[2]) + (587 * pixel[1]) + (114 * pixel[0])) / 1000;
                    sampled++;
                    if (luminance < inkCutoff)
                    {
                        ink++;
                    }
                }
            }

            return sampled == 0 || ink * 100.0 / sampled < coveragePercent;
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }
}
