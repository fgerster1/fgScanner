using System.IO;
using System.Runtime.InteropServices;
using FgScanner.Core.Station;
using FgScanner.Data;
using Serilog;

namespace FgScanner.App.Services;

/// <summary>What a backup ended with: the folder and code, or the reason in Jim's words.</summary>
/// <param name="Damaged">Earlier backups in the folder whose files are missing; kept for Franz.</param>
public sealed record BackupOutcome(
    string? Folder, string? Code, int Files, long Bytes, string? Problem, IReadOnlyList<string>? Damaged = null)
{
    public static BackupOutcome Refused(string problem) => new(null, null, 0, 0, problem);
}

/// <summary>
/// The Back up button's work (SPEC-SCANNER-2026-011): a whole, checked copy of this station on a
/// USB stick, made while the app is open. The CLI verbs refuse a running app; this one IS the
/// running app, so it checks for itself that nothing is in hand, holds the background readers
/// out (<see cref="WorkGate"/>), and copies the database through SQLite's backup API.
/// </summary>
/// <param name="holdTimeout">How long to wait for the readers to pause; a parameter only so a test
/// need not wait the real two minutes.</param>
public sealed class BackupService(StationPaths station, WorkGate gate, TimeSpan? holdTimeout = null)
{
    /// <param name="whyNotNow">The Scan page's reason a backup cannot start (paper in hand,
    /// unsaved pages), or null.</param>
    /// <param name="keep">How many of this station's backups stay in <paramref name="folder"/>.</param>
    /// <param name="recordAsLast">False for a test copy sent to Franz, which is not Jim's backup.</param>
    public async Task<BackupOutcome> BackupAsync(
        string folder, string? whyNotNow, CancellationToken cancellationToken,
        int keep = StationBackup.DefaultKeep, bool recordAsLast = true)
    {
        if (whyNotNow is not null)
        {
            return BackupOutcome.Refused(whyNotNow);
        }

        if (NotOnThisComputer(folder))
        {
            return BackupOutcome.Refused("the backup must go to a USB stick or a folder on this computer, not a network or Google Drive folder");
        }

        Log.Information("Backup to {Folder} starting", folder);
        BackupOutcome outcome;
        IDisposable hold;
        try
        {
            // A reader that never lets go must end as NOT OK, never as a window that cannot close
            // (Revision C, CR-1).
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(holdTimeout ?? TimeSpan.FromMinutes(2));
            hold = await gate.HoldAsync(timeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            outcome = BackupOutcome.Refused("page reading did not pause in time — wait a minute, then back up again");
            Log.Warning("Backup to {Folder} refused: {Problem}", folder, outcome.Problem);
            return outcome;
        }
        catch (InvalidOperationException)
        {
            return BackupOutcome.Refused("a backup is already running — wait for it to say OK");
        }

        using (hold)
        {
            try
            {
                outcome = await Task.Run(() => Run(folder, keep, recordAsLast), cancellationToken);
            }
            catch (Exception ex)
            {
                // Whatever went wrong, Jim reads a reason, never a crash (Revision C, CR-2).
                Log.Error(ex, "Backup to {Folder}", folder);
                outcome = BackupOutcome.Refused($"the backup stopped ({ex.Message}) — check the stick is plugged in, then try again");
            }
        }

        if (outcome.Problem is null)
        {
            Log.Information("Backup {Code} written to {Folder}: {Files} files, {Bytes} bytes", outcome.Code, outcome.Folder, outcome.Files, outcome.Bytes);
        }
        else
        {
            Log.Warning("Backup to {Folder} refused: {Problem}", folder, outcome.Problem);
        }

        return outcome;
    }

    /// <summary>The folders a backup must not share a disk with: the station and its evidence.</summary>
    public IReadOnlyList<string> StationFolders()
    {
        try
        {
            return StationFacts.Read(station.Database).EvidenceRoot(null) is { } evidence ? [station.Root, evidence] : [station.Root];
        }
        catch (Exception ex) when (ex is Microsoft.Data.Sqlite.SqliteException or IOException)
        {
            return [station.Root];
        }
    }

    /// <summary>A share, a mapped network drive, or the Google Drive mount: a backup there would
    /// send Jim's evidence off the computer.</summary>
    public static bool NotOnThisComputer(string folder)
    {
        try
        {
            var full = Path.GetFullPath(folder);
            if (full.StartsWith(@"\\", StringComparison.Ordinal))
            {
                return true;
            }

            var drive = new DriveInfo(Path.GetPathRoot(full)!);
            return drive.DriveType == DriveType.Network
                || (drive.IsReady && drive.VolumeLabel.Contains("Google Drive", StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return true;
        }
    }

    private BackupOutcome Run(string folder, int keep, bool recordAsLast)
    {
        StationFacts facts;
        try
        {
            facts = StationFacts.Read(station.Database);
        }
        catch (Microsoft.Data.Sqlite.SqliteException ex)
        {
            return BackupOutcome.Refused($"the database could not be read ({ex.Message}) — call Franz");
        }

        if (facts.EvidenceRoot(null) is not { } evidence)
        {
            return BackupOutcome.Refused("cannot tell which folder holds the scans — call Franz");
        }

        if (facts.GroupOutside(evidence) is { } outside)
        {
            return BackupOutcome.Refused($"a group lives outside the evidence folder {evidence}: {outside} — call Franz");
        }

        // The open app is the station's owner, so the closed-station check cannot hold (its
        // database is in WAL mode); the backup API copies a consistent snapshot regardless. What
        // is left to refuse is a scan never saved, from an earlier session the app was told to keep.
        var packer = new StationPacker(
            () => false,
            StationFacts.CopyDatabase,
            target => new DriveInfo(Path.GetPathRoot(Path.GetFullPath(target))!).AvailableFreeSpace,
            restProblem: () => HoldsUnsavedPages(station.Recovery)
                ? "FG Scanner holds unsaved scans from an earlier session — save or discard them on the Scan page, then back up again"
                : null);
        var request = new StationPackRequest(
            new StationSource(
                station.Name ?? Environment.MachineName.ToLowerInvariant(),
                station.Database,
                evidence,
                station.Trash,
                typeof(BackupService).Assembly.GetName().Version?.ToString(3) ?? "0.0.0",
                facts.LastMigration,
                null,
                DateTimeOffset.Now),
            station.Root,
            folder,
            null,
            StationPacker.BackupKind,
            // Reading jobs are held by the gate, not running: a restored station picks them up again.
            0);

        _ = SetThreadExecutionState(EsContinuous | EsSystemRequired);
        try
        {
            // Work the gate does not hold (an import or a commit already under way) can save a
            // page while the station is copied; the copy then names a file it lacks (Revision C, CR-5).
            var result = new StationBackup(packer, DriveFormatOf, check: pack =>
                    StationFacts.PageMissingFromPack(pack, evidence) is { } missing
                        ? $"the station changed while it was copied ({missing}) — back up again"
                        : null)
                .Run(request, keep, recordAsLast);
            if (result.Pack.Problem is { } problem)
            {
                return BackupOutcome.Refused(problem);
            }

            foreach (var removed in result.Removed)
            {
                Log.Information("Removed the older backup {Folder}", removed);
            }

            foreach (var damaged in result.Damaged)
            {
                Log.Warning("A damaged backup is kept for Franz: {Folder}", damaged);
            }

            var bytes = new FileInfo(Path.Combine(result.Pack.PackFolder!, StationManifest.DatabaseFileName)).Length + result.Pack.ShippedBytes;
            return new BackupOutcome(result.Pack.PackFolder, result.Pack.Code, result.Pack.ShippedFiles + 1, bytes, null, result.Damaged);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A stick pulled mid-copy: the packer has already removed what it could.
            return BackupOutcome.Refused($"the copy stopped ({ex.Message}) — check the stick is plugged in, then try again");
        }
        finally
        {
            _ = SetThreadExecutionState(EsContinuous);
        }
    }

    /// <summary>Page files in the recovery folder. The caller has already refused while the Scan
    /// page holds pages, so any page left here is an earlier session's, never saved; the live
    /// session's <c>.lock</c> and <c>index.json</c> are not scans.</summary>
    private static bool HoldsUnsavedPages(string recovery) =>
        Directory.Exists(recovery)
        && Directory.EnumerateFiles(recovery, "page-*", SearchOption.AllDirectories).Any();

    private static string? DriveFormatOf(string folder)
    {
        try
        {
            return new DriveInfo(Path.GetPathRoot(Path.GetFullPath(folder))!).DriveFormat;
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private const uint EsContinuous = 0x80000000;
    private const uint EsSystemRequired = 0x00000001;

    // Jim's laptop sleeps on its own; a backup cut short by sleep is a backup to redo.
    [DllImport("kernel32")]
    private static extern uint SetThreadExecutionState(uint flags);
}
