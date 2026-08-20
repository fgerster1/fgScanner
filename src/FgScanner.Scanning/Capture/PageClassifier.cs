using System.Runtime.Versioning;
using FgScanner.Core.Capture;

namespace FgScanner.Scanning.Capture;

/// <summary>
/// The capture-time triage decision for one page. Blank is checked before Patch-T: decoding a
/// barcode is the expensive step, and a blank page can't carry one (research-5 C2's DCP trap —
/// blank removal must not race separator detection — is avoided by this fixed order).
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class PageClassifier(
    BlankPageDetector? blankDetector = null, PatchTDetector? patchTDetector = null) : IPageClassifier
{
    private readonly BlankPageDetector _blank = blankDetector ?? new BlankPageDetector();
    private readonly PatchTDetector _patchT = patchTDetector ?? new PatchTDetector();

    public PageKind Classify(string imagePath, CapturePolicy policy)
    {
        if (policy.BlankPolicy != BlankPagePolicy.Keep && _blank.IsBlank(imagePath))
        {
            return policy.BlankPolicy == BlankPagePolicy.Separator ? PageKind.Separator : PageKind.Blank;
        }

        if (policy.DetectSeparators && _patchT.IsSeparator(imagePath))
        {
            return PageKind.Separator;
        }

        return PageKind.Content;
    }
}
