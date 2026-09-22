using System.Globalization;
using System.IO;
using FgScanner.Core.Naming;
using FgScanner.Core.Sharing;
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

    /// <summary>
    /// How this station sends mail: a mail program on the PC, or Gmail or Yahoo Mail in the
    /// browser. Chosen in Settings, never detected — nothing on Windows says where someone reads
    /// their mail. Read fresh per send, and an unknown value is the mail-program path, which is
    /// what every station did before the choice existed.
    /// </summary>
    public const string SendWithKey = "Email.SendWith";

    public static async Task<MailPath> ReadSendWithAsync(
        FgScanner.Data.AppSettingsService settings, CancellationToken cancellationToken = default)
    {
        var stored = await settings.GetAsync(SendWithKey, nameof(MailPath.MailApp), cancellationToken)
            .ConfigureAwait(false);
        return Enum.TryParse<MailPath>(stored, ignoreCase: true, out var value) && Enum.IsDefined(value)
            ? value
            : MailPath.MailApp;
    }

    public static Task WriteSendWithAsync(
        FgScanner.Data.AppSettingsService settings, MailPath value, CancellationToken cancellationToken = default) =>
        settings.SetAsync(SendWithKey, value.ToString(), cancellationToken);

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
/// Turns pages into something to attach. A PDF is built by the very exporter the Export PDF button
/// uses — so an emailed PDF is byte-for-byte the file an exported PDF would have been, and nobody
/// has to wonder whether the thing that left the building was made differently.
///
/// Images are the page files themselves, copied byte for byte, not run through the image
/// exporter. Its default re-encoded the scanner's JPEGs as PNG, two and a half to four times the
/// size of the PDF of the same pages; and an untouched copy is the better thing to send from an
/// evidence record, because its checksum is the one index.json holds and a recipient can check
/// it against the record. This departs from the spec's first wording (§07), by Franz's decision.
///
/// **Nothing is written into the group folder.** The folder's checksums and its `originals\`
/// archive are what make it evidence (ADR-0003, CLAUDE.md); a send produces a copy somewhere else
/// and leaves the record untouched. Attachments are temporary files, not records: they are not in
/// the database and they do not survive the session (§07).
/// </summary>
public sealed class AttachmentBuilder(
    PdfExportService pdf,
    string? tempRoot = null)
{
    /// <summary>§15: most mail servers reject above about 25 MB, and a 300 DPI colour page is roughly 2 MB.</summary>
    private const long WarnAboveBytes = 20L * 1024 * 1024;

    private const int MaxBaseNameLength = 100;

    private readonly string _tempRoot = tempRoot
        ?? Path.Combine(Path.GetTempPath(), "FGScanner", "email");

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

        // The sanitiser does not shorten, and the operator can paste a paragraph into the subject.
        // Capped well inside the 255-character name limit, leaving room for the exporter's
        // ".pdf.tmp" and page-number suffixes.
        if (baseName.Length > MaxBaseNameLength)
        {
            baseName = baseName[..MaxBaseNameLength].TrimEnd(' ', '.', '-');
        }

        if (string.IsNullOrWhiteSpace(baseName))
        {
            baseName = "scan";
        }

        var folder = Path.Combine(_tempRoot, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);

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
            built = await CopyPagesAsync(pagePaths, folder, baseName, cancellationToken).ConfigureAwait(false);
        }

        var bytes = built.Sum(p => new FileInfo(p).Length);
        Log.Information(
            "Built {Count} attachment(s) ({Bytes} bytes) as {Format} in {Folder}",
            built.Count, bytes, format, folder);

        return new BuiltAttachments(true, built, "", SizeWarning(bytes), bytes);
    }

    /// <summary>
    /// Named the way the image exporter names them — the subject, then _001, _002… when there is
    /// more than one — each keeping its own file's extension, since a copy is whatever the page is.
    /// </summary>
    private static async Task<IReadOnlyList<string>> CopyPagesAsync(
        IReadOnlyList<string> pagePaths, string folder, string baseName, CancellationToken cancellationToken)
    {
        var copied = new List<string>(pagePaths.Count);
        for (var i = 0; i < pagePaths.Count; i++)
        {
            var suffix = pagePaths.Count == 1 ? "" : "_" + (i + 1).ToString("000", CultureInfo.InvariantCulture);
            var target = Path.Combine(
                folder, baseName + suffix + Path.GetExtension(pagePaths[i]).ToLowerInvariant());

            // Asynchronous, because a send runs on the UI thread and a page is a couple of MB.
            await using var source = new FileStream(
                pagePaths[i], FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
            await using var destination = new FileStream(
                target, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true);
            await source.CopyToAsync(destination, cancellationToken).ConfigureAwait(false);
            copied.Add(target);
        }

        return copied;
    }

    /// <summary>
    /// §15: warn above 20 MB and let the operator decide. Refusing would be worse — the limit is
    /// the recipient's mail server, which this app cannot know, and a send that is merely large
    /// is not a send that is wrong.
    /// </summary>
    public static string SizeWarning(long bytes)
    {
        // The server's limit applies to the message, and attachments travel base64-encoded: four
        // bytes for every three. Measured on the files alone, a 19 MB PDF — a 25 MB message — was
        // never warned about.
        var encoded = bytes * 4 / 3;
        if (encoded <= WarnAboveBytes)
        {
            return "";
        }

        var mb = (encoded / (1024.0 * 1024.0)).ToString("0", CultureInfo.InvariantCulture);
        // Never "attach images instead": the PDF carries the scanner's JPEGs through unchanged,
        // so the images are no smaller and that advice sent the same message back to bounce.
        return $"Attached to a message these are about {mb} MB. Mail servers often refuse anything over "
            + "20 MB, so this may bounce — send fewer pages at a time.";
    }

    /// <summary>
    /// Removes every attachment folder under the root — this session's and any a crashed session
    /// left. Called at startup and at exit. The copies are temporary by design, and leaving case
    /// material in the temp folder after the app has gone is exactly the quiet spread §13 is
    /// about.
    ///
    /// The whole root, not just the folders this instance made: a crash, End Task or power cut
    /// never reaches exit, and a folder whose delete failed (a PDF still open in the mail client)
    /// used to be forgotten. That is safe only because FG Scanner is single-instance — the mutex
    /// is taken before anything is built, so no other session can be using a folder here.
    /// </summary>
    public void CleanUp()
    {
        if (!Directory.Exists(_tempRoot))
        {
            return;
        }

        foreach (var folder in Directory.EnumerateDirectories(_tempRoot))
        {
            try
            {
                Directory.Delete(folder, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Still open in the mail client, usually. The next startup tries again.
                Log.Warning(ex, "Could not remove the attachment folder {Folder}; retrying next start", folder);
            }
        }
    }
}
