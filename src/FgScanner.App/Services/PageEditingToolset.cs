using FgScanner.Data;
using FgScanner.Scanning.Editing;
using FgScanner.Scanning.Export;
using FgScanner.Scanning.Import;

namespace FgScanner.App.Services;

/// <summary>The editing/export/OCR services, bundled so view models take one dependency.</summary>
public sealed record PageEditingToolset(
    ImageEditor Editor,
    PdfExportService PdfExport,
    ImageExportService ImageExport,
    FileImportService FileImport,
    ReorderService Reorder,
    OcrQueueService OcrQueue);
