using FgScanner.Data;
using FgScanner.Scanning.Editing;
using FgScanner.Scanning.Export;
using FgScanner.Scanning.Import;

namespace FgScanner.App.Services;

/// <summary>The phase-4 editing/export services, bundled so view models take one dependency.</summary>
public sealed record PageEditingToolset(
    ImageEditor Editor,
    PdfExportService PdfExport,
    ImageExportService ImageExport,
    FileImportService FileImport,
    ReorderService Reorder);
