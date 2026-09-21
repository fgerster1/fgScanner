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
    AppSettingsService Settings,
    CaptureTriageService Triage,
    DuplicateFinder Duplicates)
{
    /// <summary>
    /// Sends pages to the operator's mail path. An init property rather than a thirteenth
    /// positional parameter, so the app can hand over the DI instance — whose temp folders
    /// <c>App.OnExit</c> cleans up — while every existing construction site keeps working with a
    /// self-contained default.
    /// </summary>
    public EmailSender Email { get; init; } = new(
        new AttachmentBuilder(PdfExport, ImageExport),
        new WindowsShareService(),
        Settings);
}
