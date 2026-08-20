using System.Globalization;

using FgScanner.Core;



namespace FgScanner.Scanning.Import;

/// <summary>IPdfRenderer over the same Pdfium import path scanning uses (PLAN §5.7).</summary>
public sealed class Naps2PdfRenderer(FileImportService fileImport) : IPdfRenderer
{
    public async Task<IReadOnlyList<string>> RenderPagesAsync(
        string pdfPath, string outputDirectory, CancellationToken cancellationToken = default)
    {
        var storage = new DirectoryPageStorage(outputDirectory);
        var files = new List<string>();
        await foreach (var page in fileImport.ImportAsync(pdfPath, storage, cancellationToken: cancellationToken))
        {
            files.Add(page.FilePath);
        }

        return files;
    }

    private sealed class DirectoryPageStorage(string directory) : IPageStorage
    {
        private int _next;

        public string ReserveNextPagePath(string extension)
        {
            Directory.CreateDirectory(directory);
            _next++;
            return Path.Combine(
                directory, $"page-{_next.ToString("00000", CultureInfo.InvariantCulture)}.{extension}");
        }

        public void CommitPage(ScannedPage page)
        {
        }
    }
}
