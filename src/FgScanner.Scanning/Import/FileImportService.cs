using System.Runtime.CompilerServices;
using NAPS2.Images;
using NAPS2.Images.Gdi;
using NAPS2.ImportExport;
using NAPS2.Scan;

namespace FgScanner.Scanning.Import;

/// <summary>
/// Imports PDFs (password-protected included) and image files through the same page-adoption path
/// as scanning: each page is rendered to a file in <see cref="IPageStorage"/> (PLAN prompt 4).
/// </summary>
public sealed class FileImportService : IDisposable
{
    private readonly ScanningContext _scanningContext = new(new GdiImageContext());

    public static readonly string[] SupportedExtensions =
        [".pdf", ".png", ".jpg", ".jpeg", ".tif", ".tiff", ".bmp"];

    public async IAsyncEnumerable<ScannedPage> ImportAsync(
        string sourcePath,
        IPageStorage storage,
        string? pdfPassword = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var importer = new FileImporter(_scanningContext);
        var importParams = new ImportParams { Password = pdfPassword };
        await foreach (var processed in importer.Import(sourcePath, importParams)
            .WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            using (processed)
            {
                var path = storage.ReserveNextPagePath("png");
                using (var rendered = processed.Render())
                {
                    rendered.Save(path, ImageFileFormat.Png);
                }

                var page = new ScannedPage(path, ExtractSequence(path));
                storage.CommitPage(page);
                yield return page;
            }
        }
    }

    public void Dispose() => _scanningContext.Dispose();

    private static int ExtractSequence(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        var dash = name.LastIndexOf('-');
        return dash >= 0 && int.TryParse(name[(dash + 1)..], out var n) ? n : 0;
    }
}
