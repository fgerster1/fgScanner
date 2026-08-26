namespace FgScanner.Core.Imaging;

/// <summary>
/// Turns a page image on disk. Rotating pixels needs System.Drawing and a Windows target, which
/// neither FgScanner.Core nor FgScanner.Ocr takes, so the implementation lives in
/// FgScanner.Scanning and arrives at the OCR pipeline as a dependency. Declaring it here rather
/// than in either of those keeps Scanning and Ocr independent of one another, the same split used
/// for perceptual hashing (ImageHashComparer in Core, ImageHasher in Scanning).
/// </summary>
public interface IPageRotator
{
    /// <summary>Rotates the file in place by the given clockwise degrees (90, 180 or 270).</summary>
    Task RotateAsync(string imagePath, int clockwiseDegrees, CancellationToken cancellationToken = default);
}
