using System.Globalization;
using System.IO;
using FgScanner.Core.Naming;
using FgScanner.Scanning.Export;
using Serilog;

namespace FgScanner.App.Services;

/// <summary>Which artefact a send attaches. Stored as <c>Email.Attachment</c>, default Pdf.</summary>
public enum EmailAttachment
{
    Pdf,
    Images,
}

/// <summary>
/// The remembered attachment format (§07). One `Settings` row, no schema change, and **read fresh
/// on every send** rather than captured once — a format chosen in Settings has to reach the next
/// send without a restart, which is the whole of ADR-0010.
/// </summary>
public static class EmailSettings
{
    public const string AttachmentKey = "Email.Attachment";

    /// <summary>
    /// Whether the operator has already been told, once, what sending a committed evidence
    /// group's pages means (§05 Q2b, §07). Stored rather than shown every time, because a warning
    /// that appears on every send is a warning nobody reads — and this one is worth reading.
    /// </summary>
    public const string EvidenceWarningSeenKey = "Email.EvidenceWarningSeen";

    public static async Task<bool> WarningSeenAsync(
        FgScanner.Data.AppSettingsService settings, CancellationToken cancellationToken = default) =>
        string.Equals(
            await settings.GetAsync(EvidenceWarningSeenKey, "false", cancellationToken).ConfigureAwait(false),
            "true",
            StringComparison.OrdinalIgnoreCase);

    public static Task MarkWarningSeenAsync(
        FgScanner.Data.AppSettingsService settings, CancellationToken cancellationToken = default) =>
        settings.SetAsync(EvidenceWarningSeenKey, "true", cancellationToken);

    /// <summary>
    /// PDF by default: one file, the format a recipient can open anywhere, and the same artefact
    /// the export button produces. An unreadable or unknown stored value falls back to it rather
    /// than throwing — a corrupt setting must not stop an operator sending a page.
    /// </summary>
    public static async Task<EmailAttachment> ReadAsync(
        FgScanner.Data.AppSettingsService settings, CancellationToken cancellationToken = default)
    {
        var stored = await settings.GetAsync(AttachmentKey, nameof(EmailAttachment.Pdf), cancellationToken)
            .ConfigureAwait(false);
        return Enum.TryParse<EmailAttachment>(stored, ignoreCase: true, out var value) && Enum.IsDefined(value)
            ? value
            : EmailAttachment.Pdf;
    }

    public static Task WriteAsync(
        FgScanner.Data.AppSettingsService settings,
        EmailAttachment value,
        CancellationToken cancellationToken = default) =>
        settings.SetAsync(AttachmentKey, value.ToString(), cancellationToken);
}

/// <summary>
/// What was built, or why nothing was. <see cref="Warning"/> is shown alongside a successful
/// build; it never blocks one.
/// </summary>
public sealed record BuiltAttachments(
    bool Ok, IReadOnlyList<string> FilePaths, string Message, string Warning, long TotalBytes)
{
    public bool TooLarge => Warning.Length > 0;
}

/// <summary>
/// Turns pages into something to attach, using the very same exporters the export buttons use —
/// so an emailed PDF is byte-for-byte the file an exported PDF would have been, and nobody has to
/// wonder whether the thing that left the building was made differently.
///
/// **Nothing is written into the group folder.** The folder's checksums and its `originals\`
/// archive are what make it evidence (ADR-0003, CLAUDE.md); a send produces a copy somewhere else
/// and leaves the record untouched. Attachments are temporary files, not records: they are not in
/// the database and they do not survive the session (§07).
/// </summary>
public sealed class AttachmentBuilder(
    PdfExportService pdf,
    ImageExportService images,
    string? tempRoot = null)
{
    /// <summary>§15: most mail servers reject above about 25 MB, and a 300 DPI colour page is roughly 2 MB.</summary>
    private const long WarnAboveBytes = 20L * 1024 * 1024;

    private readonly string _tempRoot = tempRoot
        ?? Path.Combine(Path.GetTempPath(), "FGScanner", "email");

    /// <summary>Every folder this instance made, so app exit can take them all (§07).</summary>
    private readonly List<string> _folders = [];

    public async Task<BuiltAttachments> BuildAsync(
        IReadOnlyList<string> pagePaths,
        string subject,
        EmailAttachment format,
        CancellationToken cancellationToken = default)
    {
        if (pagePaths.Count == 0)
        {
            return new BuiltAttachments(false, [], "There are no pages to attach.", "", 0);
        }

        // Checked before anything is built. A PDF silently one page short looks complete to
        // whoever receives it, which is the failure worth refusing outright.
        var missing = pagePaths.Where(p => !File.Exists(p)).Select(Path.GetFileName).ToList();
        if (missing.Count > 0)
        {
            var named = string.Join(", ", missing);
            Log.Warning("Refused to build attachments: {Count} page file(s) missing: {Missing}", missing.Count, named);
            return new BuiltAttachments(
                false,
                [],
                missing.Count == 1
                    ? $"The page {named} is no longer on disk, so nothing was attached."
                    : $"These pages are no longer on disk, so nothing was attached: {named}.",
                "",
                0);
        }

        // The subject reaches a file name, so it goes through the same sanitisation the export
        // dialogs use rather than being trusted (§13).
        var baseName = NamingEngine.Sanitize(subject);
        if (string.IsNullOrWhiteSpace(baseName))
        {
            baseName = "scan";
        }

        var folder = Path.Combine(_tempRoot, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        _folders.Add(folder);

        IReadOnlyList<string> built;
        if (format == EmailAttachment.Pdf)
        {
            var target = Path.Combine(folder, baseName + ".pdf");
            await pdf.ExportAsync(pagePaths, target, new PdfExportOptions(), cancellationToken)
                .ConfigureAwait(false);
            built = [target];
        }
        else
        {
            built = await images
                .ExportAsync(pagePaths, folder, baseName, new ImageExportOptions(), cancellationToken)
                .ConfigureAwait(false);
        }

        var bytes = built.Sum(p => new FileInfo(p).Length);
        Log.Information(
            "Built {Count} attachment(s) ({Bytes} bytes) as {Format} in {Folder}",
            built.Count, bytes, format, folder);

        return new BuiltAttachments(true, built, "", SizeWarning(bytes), bytes);
    }

    /// <summary>
    /// §15: warn above 20 MB and let the operator decide. Refusing would be worse — the limit is
    /// the recipient's mail server, which this app cannot know, and a send that is merely large
    /// is not a send that is wrong.
    /// </summary>
    public static string SizeWarning(long bytes)
    {
        if (bytes <= WarnAboveBytes)
        {
            return "";
        }

        var mb = (bytes / (1024.0 * 1024.0)).ToString("0", CultureInfo.InvariantCulture);
        return $"These attachments are about {mb} MB. Mail servers often refuse anything over "
            + "20 MB, so this may bounce — send fewer pages, or attach images instead of a PDF.";
    }

    /// <summary>
    /// Removes this session's attachment folders. Called on app exit: the copies are temporary by
    /// design, and leaving case material in the temp folder after the app has gone is exactly the
    /// kind of quiet spread §13 is about.
    /// </summary>
    public void CleanUp()
    {
        foreach (var folder in _folders)
        {
            try
            {
                if (Directory.Exists(folder))
                {
                    Directory.Delete(folder, recursive: true);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // A file still open in the mail client is the ordinary case; the folder is under
                // the system temp path and Windows clears it eventually.
                Log.Warning(ex, "Could not remove the attachment folder {Folder}", folder);
            }
        }

        _folders.Clear();
    }
}
