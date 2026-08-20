using FgScanner.Ai;
using FgScanner.Data;
using FgScanner.Scanning.Editing;
using FgScanner.Scanning.Export;
using FgScanner.Scanning.Import;

namespace FgScanner.App.Services;

/// <summary>The editing/export/OCR/AI services, bundled so view models take one dependency.</summary>
public sealed record PageEditingToolset(
    ImageEditor Editor,
    PdfExportService PdfExport,
    ImageExportService ImageExport,
    FileImportService FileImport,
    ReorderService Reorder,
    OcrQueueService OcrQueue,
    AiQueueService AiQueue,
    RetroProcessService Retro,
    CredentialStore Credentials,
    AppSettingsService Settings);
