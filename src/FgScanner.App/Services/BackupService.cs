using System.IO;
using System.Runtime.InteropServices;
using FgScanner.Core.Station;
using FgScanner.Data;
using Serilog;

namespace FgScanner.App.Services;

/// <summary>What a backup ended with: the folder and code, or the reason in Jim's words.</summary>
public sealed record BackupOutcome(string? Folder, string? Code, int Files, long Bytes, string? Problem)
{
    public static BackupOutcome Refused(string problem) => new(null, null, 0, 0, problem);
}

/// <summary>
/// The Back up button's work (SPEC-SCANNER-2026-011): a whole, checked copy of this station on a
/// USB stick, made while the app is open. The CLI verbs refuse a running app; this one IS the
/// running app, so it checks for itself that nothing is in hand, holds the background readers
/// out (<see cref="WorkGate"/>), and copies the database through SQLite's backup API.
/// </summary>
public sealed class BackupService(StationPaths station, WorkGate gate)
{
    /// <param name="whyNotNow">The Scan page's reason a backup cannot start (paper in hand,
    /// unsaved pages), or null.</param>
    public async Task<BackupOutcome> BackupAsync(string folder, string? whyNotNow, CancellationToken cancellationToken)
    {
        if (whyNotNow is not null)
        {
            return BackupOutcome.Refused(whyNotNow);
        }

        Log.Information("Backup to {Folder} starting", folder);
        using var hold = await gate.HoldAsync(cancellationToken);
        var outcome = await Task.Run(() => Run(folder), cancellationToken);
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

    private BackupOutcome Run(string folder)
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
            var result = new StationBackup(packer, DriveFormatOf).Run(request);
            if (result.Pack.Problem is { } problem)
            {
                return BackupOutcome.Refused(problem);
            }

            foreach (var removed in result.Removed)
            {
                Log.Information("Removed the older backup {Folder}", removed);
            }

            var bytes = new FileInfo(Path.Combine(result.Pack.PackFolder!, StationManifest.DatabaseFileName)).Length + result.Pack.ShippedBytes;
            return new BackupOutcome(result.Pack.PackFolder, result.Pack.Code, result.Pack.ShippedFiles + 1, bytes, null);
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
