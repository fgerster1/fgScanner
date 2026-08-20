using NAPS2.Images;

namespace FgScanner.Scanning.Editing;

/// <summary>
/// Projection-profile skew estimation (independent implementation — NAPS2's deskewer lives in its
/// GPL app layer). The angle that maximizes the variance of the horizontal ink profile is the one
/// that makes text lines horizontal; we search coarse then fine over ±10°.
/// </summary>
public static class SkewEstimator
{
    private const int TargetWidth = 800;
    private const double MaxAngle = 10.0;

    /// <summary>Returns the detected skew in degrees; rotating by the negation straightens the image.</summary>
    public static double EstimateSkewDegrees(IMemoryImage image)
    {
        var (pixels, width, height) = ToBinaryGrid(image);
        var coarse = BestAngle(pixels, width, height, -MaxAngle, MaxAngle, 0.5);
        // Negated so the result matches RotationTransform: the angle the page was rotated by.
        return -BestAngle(pixels, width, height, coarse - 0.5, coarse + 0.5, 0.1);
    }

    private static (bool[] Pixels, int Width, int Height) ToBinaryGrid(IMemoryImage image)
    {
        var scale = Math.Min(1.0, (double)TargetWidth / image.Width);
        var width = Math.Max(1, (int)(image.Width * scale));
        var height = Math.Max(1, (int)(image.Height * scale));

        using var lockState = image.Lock(LockMode.ReadOnly, out var data);
        var luminance = new byte[width * height];
        long sum = 0;
        for (var y = 0; y < height; y++)
        {
            var sourceY = (int)(y / scale);
            for (var x = 0; x < width; x++)
            {
                var sourceX = (int)(x / scale);
                var value = SampleLuminance(data, sourceX, sourceY);
                luminance[y * width + x] = value;
                sum += value;
            }
        }

        // Ink = darker than the mean; robust enough for scans, which are mostly background.
        var threshold = (byte)(sum / luminance.Length);
        var pixels = new bool[luminance.Length];
        for (var i = 0; i < luminance.Length; i++)
        {
            pixels[i] = luminance[i] < threshold;
        }

        return (pixels, width, height);
    }

    private static unsafe byte SampleLuminance(BitwiseImageData data, int x, int y)
    {
        var row = (byte*)data.ptr + (long)y * data.stride;
        if (data.bitsPerPixel == 1)
        {
            var bit = (row[x / 8] >> (7 - (x % 8))) & 1;
            return (byte)(bit == 1 ? 255 : 0);
        }

        if (data.bitsPerPixel == 8)
        {
            return row[x];
        }

        var pixel = row + ((long)x * (data.bitsPerPixel / 8));
        // BGR(A) byte order; integer luma approximation.
        return (byte)(((pixel[2] * 299) + (pixel[1] * 587) + (pixel[0] * 114)) / 1000);
    }

    private static double BestAngle(bool[] pixels, int width, int height, double from, double to, double step)
    {
        var bestAngle = 0.0;
        var bestScore = double.MinValue;
        for (var angle = from; angle <= to + (step / 2); angle += step)
        {
            var score = ProfileVariance(pixels, width, height, angle);
            if (score > bestScore)
            {
                bestScore = score;
                bestAngle = angle;
            }
        }

        return bestAngle;
    }

    private static double ProfileVariance(bool[] pixels, int width, int height, double angleDegrees)
    {
        var shear = Math.Tan(angleDegrees * Math.PI / 180.0);
        var counts = new int[height];
        for (var y = 0; y < height; y++)
        {
            var rowBase = y * width;
            for (var x = 0; x < width; x++)
            {
                if (!pixels[rowBase + x])
                {
                    continue;
                }

                var sheared = (int)(y + (shear * x));
                if (sheared >= 0 && sheared < height)
                {
                    counts[sheared]++;
                }
            }
        }

        double mean = counts.Average();
        return counts.Sum(c => (c - mean) * (c - mean)) / height;
    }
}
