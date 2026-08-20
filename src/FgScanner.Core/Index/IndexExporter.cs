using System.Text.Json;

namespace FgScanner.Core.Index;

/// <summary>
/// One pipeline, four format writers. Every file is written atomically with lock-retry;
/// a locked file is reported, never thrown — the database commit is unaffected (PLAN §5.2).
/// Also writes manifest.json beside the index files.
/// </summary>
public sealed class IndexExporter(AtomicFileWriter? fileWriter = null)
{
    private static readonly JsonSerializerOptions ManifestOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly AtomicFileWriter _fileWriter = fileWriter ?? new AtomicFileWriter();

    public async Task<ExportResult> ExportAsync(
        IndexExportData data, CancellationToken cancellationToken = default)
    {
        var results = new List<FormatResult>();
        foreach (var format in data.Formats)
        {
            IFormatWriter writer = format switch
            {
                IndexFormat.Csv => new CsvFormatWriter(),
                IndexFormat.Xlsx => new XlsxFormatWriter(),
                IndexFormat.Xml => new XmlFormatWriter(),
                IndexFormat.Json => new JsonFormatWriter(),
                _ => throw new ArgumentOutOfRangeException(nameof(data)),
            };
            var path = Path.Combine(data.GroupDirectory, writer.FileName(data));
            var (outcome, message) = await _fileWriter.WriteAsync(
                path, stream => writer.WriteAsync(stream, data), cancellationToken).ConfigureAwait(false);
            results.Add(new FormatResult(format, path, outcome, message));
        }

        var manifestPath = Path.Combine(data.GroupDirectory, "manifest.json");
        var (manifestOutcome, manifestMessage) = await _fileWriter.WriteAsync(
            manifestPath,
            stream => JsonSerializer.SerializeAsync(stream, ManifestBuilder.Build(data), ManifestOptions, cancellationToken),
            cancellationToken).ConfigureAwait(false);
        results.Add(new FormatResult(IndexFormat.Json, manifestPath, manifestOutcome, manifestMessage));

        return new ExportResult(results);
    }
}
