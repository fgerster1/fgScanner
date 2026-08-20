namespace FgScanner.Core.Capture;

/// <summary>Per-profile handling of blank pages (PLAN prompt 10 / research-5 C2).</summary>
public enum BlankPagePolicy
{
    /// <summary>Blanks are ordinary pages (default; detection off).</summary>
    Keep,

    /// <summary>Blank pages are not adopted; the drop is journaled in the group.</summary>
    Drop,

    /// <summary>Blank pages are adopted but excluded from OCR, AI, and the index files.</summary>
    Flag,

    /// <summary>Blank pages act like Patch-T separator sheets (kept/dropped by that policy).</summary>
    Separator,
}

public enum PageKind
{
    Content,
    Separator,
    Blank,
}

/// <summary>What capture-time triage should do for one group, resolved from profile + feature flags.</summary>
public sealed record CapturePolicy(
    bool DetectSeparators,
    bool KeepSeparatorPages,
    BlankPagePolicy BlankPolicy)
{
    public static CapturePolicy Off { get; } = new(false, false, BlankPagePolicy.Keep);

    public bool IsActive => DetectSeparators || BlankPolicy != BlankPagePolicy.Keep;
}

/// <summary>
/// Classifies a captured page image. Implemented in FgScanner.Scanning (ZXing + pixel coverage);
/// Core and Data stay free of imaging dependencies through this seam.
/// </summary>
public interface IPageClassifier
{
    PageKind Classify(string imagePath, CapturePolicy policy);
}
