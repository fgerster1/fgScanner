namespace FgScanner.Core;

/// <summary>
/// Renders a PDF's pages to image files (PLAN §5.7 retro-processing). Implemented over the same
/// Pdfium path the scan import uses; defined here so FgScanner.Data stays free of NAPS2 types.
/// </summary>
public interface IPdfRenderer
{
    /// <summary>Renders every page to a PNG in <paramref name="outputDirectory"/>; returns the files in page order.</summary>
    Task<IReadOnlyList<string>> RenderPagesAsync(
        string pdfPath, string outputDirectory, CancellationToken cancellationToken = default);
}
