namespace FgScanner.Scanning;

/// <summary>
/// Some TWAIN data sources report no resolution on the first image of a run: the bitmap reaches us
/// carrying GDI's 96 DPI default while the pixels are at the requested DPI. Observed 3/3 on a Pantum
/// M6550NW, 2026-08-24 (docs/manual-tests.md, BUG-1); eSCL was unaffected.
///
/// That matters because <c>OcrWorker</c> derives Tesseract's <c>--dpi</c> from the saved image and
/// the PDF exporter sizes pages from it — the misalignment class PLAN §5.5 set out to guard against.
///
/// We only correct a resolution that looks *unset*. A driver that clamps to a nearby supported DPI
/// (ask for 1200, get 600) is reporting the truth, and overwriting that with the request would turn
/// a correct label into a lie.
/// </summary>
public static class ScanResolutionPolicy
{
    /// <summary>What GDI reports for a bitmap whose resolution was never assigned.</summary>
    public const float GdiDefaultDpi = 96f;

    /// <summary>
    /// Returns the DPI to stamp on a scanned image, or <c>null</c> to keep what the driver reported.
    /// </summary>
    public static float? ResolveDpiToStamp(float reportedDpi, int requestedDpi)
    {
        if (requestedDpi <= 0)
        {
            return null; // nothing trustworthy to stamp
        }

        if (reportedDpi <= 0)
        {
            return requestedDpi; // no resolution at all
        }

        // 96 is a legitimate scan resolution, so it only reads as "unset" when we asked for
        // something else and the pixel count already reflects the request.
        var looksLikeGdiDefault =
            Math.Abs(reportedDpi - GdiDefaultDpi) < 0.01f && requestedDpi != (int)GdiDefaultDpi;

        return looksLikeGdiDefault ? requestedDpi : null;
    }
}
