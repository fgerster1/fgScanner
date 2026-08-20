using FgScanner.Core.Index;
using NAPS2.Images;
using NAPS2.Images.Gdi;
using NAPS2.ImportExport;
using NAPS2.Pdf;
using NAPS2.Scan;

namespace FgScanner.Scanning.Export;

public enum PdfCompatLevel
{
    Default,
    PdfA1B,
    PdfA2B,
    PdfA3B,
    PdfA3U,
}

/// <summary>Owner/user passwords plus the eight PDF permission flags (NAPS2 parity).</summary>
public sealed record PdfSecurity
{
    public string OwnerPassword { get; init; } = "";
    public string UserPassword { get; init; } = "";
    public bool AllowPrinting { get; init; } = true;
    public bool AllowFullQualityPrinting { get; init; } = true;
    public bool AllowDocumentModification { get; init; } = true;
    public bool AllowDocumentAssembly { get; init; } = true;
    public bool AllowContentCopying { get; init; } = true;
    public bool AllowContentCopyingForAccessibility { get; init; } = true;
    public bool AllowAnnotations { get; init; } = true;
    public bool AllowFormFilling { get; init; } = true;
}

/// <summary>
/// Tesseract wiring for the searchable text layer (paths supplied by FgScanner.Ocr so this
/// project stays decoupled from it).
/// </summary>
public sealed record PdfOcrSettings(
    string TesseractExePath, string TessdataDir, string Languages = "eng", double TimeoutSeconds = 120);

public sealed record PdfExportOptions
{
    public string Title { get; init; } = "";
    public string Author { get; init; } = "";
    public string Subject { get; init; } = "";
    public string Keywords { get; init; } = "";
    public PdfCompatLevel Compat { get; init; } = PdfCompatLevel.Default;
    public PdfSecurity? Security { get; init; }

    /// <summary>When set, the export runs OCR and embeds an invisible, selectable text layer.</summary>
    public PdfOcrSettings? Ocr { get; init; }
}

/// <summary>
/// PDF export over NAPS2.Sdk's PDFsharp path (PLAN §5.8): PDF/A levels, metadata, and encryption.
/// The OCR text layer joins in phase 5 via OcrParams.
/// </summary>
public sealed class PdfExportService : IDisposable
{
    private readonly ScanningContext _scanningContext = new(new GdiImageContext());
    private readonly AtomicFileWriter _writer = new();

    public async Task ExportAsync(
        IReadOnlyList<string> imagePaths, string outputPath, PdfExportOptions options,
        CancellationToken cancellationToken = default)
    {
        var importer = new ImageImporter(_scanningContext);
        var images = new List<ProcessedImage>();
        try
        {
            foreach (var path in imagePaths)
            {
                await foreach (var image in importer.Import(path).WithCancellation(cancellationToken).ConfigureAwait(false))
                {
                    images.Add(image);
                }
            }

            var exportParams = new PdfExportParams(
                new PdfMetadata
                {
                    Title = options.Title,
                    Author = options.Author,
                    Subject = options.Subject,
                    Keywords = options.Keywords,
                    Creator = "FG Scanner",
                },
                BuildEncryption(options.Security),
                options.Compat switch
                {
                    PdfCompatLevel.PdfA1B => PdfCompat.PdfA1B,
                    PdfCompatLevel.PdfA2B => PdfCompat.PdfA2B,
                    PdfCompatLevel.PdfA3B => PdfCompat.PdfA3B,
                    PdfCompatLevel.PdfA3U => PdfCompat.PdfA3U,
                    _ => PdfCompat.Default,
                });

            NAPS2.Ocr.OcrParams? ocrParams = null;
            if (options.Ocr is { } ocr)
            {
                // NAPS2's exporter runs Tesseract per page and draws the invisible text layer at
                // the image's true DPI — the alignment bug class of NAPS2 issue #843 is covered
                // by a regression test on the page box.
                _scanningContext.OcrEngine = NAPS2.Ocr.TesseractOcrEngine.Custom(
                    ocr.TesseractExePath, ocr.TessdataDir);
                ocrParams = new NAPS2.Ocr.OcrParams(ocr.Languages, TimeoutInSeconds: ocr.TimeoutSeconds);
            }

            var exporter = new PdfExporter(_scanningContext);
            var (outcome, message) = await _writer.WriteAsync(
                outputPath,
                stream => exporter.Export(stream, images, exportParams, ocrParams, progress: cancellationToken),
                cancellationToken).ConfigureAwait(false);
            if (outcome != ExportOutcome.Success)
            {
                throw new IOException(message ?? $"Could not write {outputPath}.");
            }
        }
        finally
        {
            foreach (var image in images)
            {
                image.Dispose();
            }
        }
    }

    private static PdfEncryption BuildEncryption(PdfSecurity? security) =>
        security is null
            ? new PdfEncryption()
            : new PdfEncryption
            {
                EncryptPdf = true,
                OwnerPassword = security.OwnerPassword,
                UserPassword = security.UserPassword,
                AllowPrinting = security.AllowPrinting,
                AllowFullQualityPrinting = security.AllowFullQualityPrinting,
                AllowDocumentModification = security.AllowDocumentModification,
                AllowDocumentAssembly = security.AllowDocumentAssembly,
                AllowContentCopying = security.AllowContentCopying,
                AllowContentCopyingForAccessibility = security.AllowContentCopyingForAccessibility,
                AllowAnnotations = security.AllowAnnotations,
                AllowFormFilling = security.AllowFormFilling,
            };

    public void Dispose() => _scanningContext.Dispose();
}
