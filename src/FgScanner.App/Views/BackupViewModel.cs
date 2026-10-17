using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FgScanner.App.Services;
using FgScanner.Data;

namespace FgScanner.App.Views;

/// <summary>A place a backup can go: a stick's backup folder, or a folder picked with Browse.</summary>
public sealed record BackupTarget(string Label, string Folder)
{
    public override string ToString() => Label;
}

/// <summary>
/// The Back up window (SPEC-SCANNER-2026-011 §03 item 4): pick the stick, back up, read OK and the
/// code — or NOT OK and why. Backups go into a <see cref="FolderName"/> folder on the stick so they
/// never mix with whatever else is on it, and the folder used is remembered for next time.
/// </summary>
/// <param name="testCopyFolder">Where a test copy for Franz goes (<c>to-franz\copies</c> in the shared
/// Drive folder), or null on a station whose Drive folder was never set up.</param>
public sealed partial class BackupViewModel(
    BackupService backup,
    AppSettingsService settings,
    Func<string?> whyNotNow,
    Func<IReadOnlyList<BackupTarget>> findTargets,
    string? testCopyFolder = null) : ObservableObject
{
    public const string FolderName = "FGScanner-Backup";

    public const string FolderSettingKey = "Backup.Folder";

    /// <summary>How many backups stay on the stick (spec §07); not shown in Settings.</summary>
    public const string KeepSettingKey = "Backup.Keep";

    /// <summary>Not cleared on reload: the drive list binds SelectedItem (CLAUDE.md, never clear a
    /// collection bound to a Selector).</summary>
    public ObservableCollection<BackupTarget> Targets { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(BackupCommand))]
    private BackupTarget? _selectedTarget;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(BackupCommand))]
    [NotifyCanExecuteChangedFor(nameof(SendTestCopyCommand))]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    private bool _isRunning;

    public bool IsIdle => !IsRunning;

    [ObservableProperty]
    private string _statusText = "";

    [ObservableProperty]
    private string _resultText = "";

    [ObservableProperty]
    private bool _succeeded;

    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        foreach (var target in findTargets().Where(t => !Targets.Contains(t)))
        {
            Targets.Add(target);
        }

        var remembered = await settings.GetAsync(FolderSettingKey, "", cancellationToken);
        if (remembered.Length > 0 && Targets.All(t => !SameFolder(t.Folder, remembered)) && Directory.Exists(remembered))
        {
            Targets.Add(new BackupTarget(remembered, remembered));
        }

        SelectedTarget = Targets.FirstOrDefault(t => SameFolder(t.Folder, remembered)) ?? Targets.FirstOrDefault();
        StatusText = Targets.Count == 0
            ? "Plug in your USB stick, or choose a folder with Browse."
            : "Leave the stick in until this says OK.";
    }

    /// <summary>A folder chosen with Browse; false (and why, in <see cref="StatusText"/>) for a
    /// share or the Google Drive mount, which the drive list leaves out for the same reason.</summary>
    public bool UseFolder(string folder)
    {
        if (BackupService.NotOnThisComputer(folder))
        {
            StatusText = "Choose a USB stick or a folder on this computer — a network or Google Drive folder would send your scans away.";
            return false;
        }

        var target = Targets.FirstOrDefault(t => SameFolder(t.Folder, folder)) ?? new BackupTarget(folder, folder);
        if (!Targets.Contains(target))
        {
            Targets.Add(target);
        }

        SelectedTarget = target;
        StatusText = "Leave the stick in until this says OK.";
        return true;
    }

    private bool CanBackup() => SelectedTarget is not null && !IsRunning;

    [RelayCommand(CanExecute = nameof(CanBackup))]
    private Task BackupAsync() => RunAsync(SelectedTarget!.Folder, keep: null, toFranz: false);

    private bool CanSendTestCopy() => testCopyFolder is not null && !IsRunning;

    /// <summary>The same checked copy, to Franz through Drive; only the newest stays there.</summary>
    [RelayCommand(CanExecute = nameof(CanSendTestCopy))]
    private Task SendTestCopyAsync() => RunAsync(testCopyFolder!, keep: 1, toFranz: true);

    /// <param name="keep">Null for a stick backup: <see cref="KeepSettingKey"/> decides.</param>
    private async Task RunAsync(string folder, int? keep, bool toFranz)
    {
        IsRunning = true;
        Succeeded = false;
        ResultText = "";
        StatusText = "Backing up — leave the stick in until this says OK.";
        try
        {
            keep ??= int.TryParse(
                await settings.GetAsync(KeepSettingKey, FgScanner.Core.Station.StationBackup.DefaultKeep.ToString(CultureInfo.InvariantCulture)),
                NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) && n > 0
                ? n
                : FgScanner.Core.Station.StationBackup.DefaultKeep;
            var refusal = whyNotNow();
            if (refusal is null && !TryCreate(folder, out var problem))
            {
                refusal = problem;
            }

            var outcome = refusal is null
                ? await backup.BackupAsync(folder, null, CancellationToken.None, keep.Value, recordAsLast: !toFranz)
                : BackupOutcome.Refused(refusal);
            Succeeded = outcome.Problem is null;
            if (Succeeded)
            {
                if (!toFranz)
                {
                    await settings.SetAsync(FolderSettingKey, folder);
                }

                ResultText = $"OK {outcome.Code} — {outcome.Files.ToString("N0", CultureInfo.InvariantCulture)} files, " +
                             $"{(outcome.Bytes / (double)(1L << 20)).ToString("N0", CultureInfo.InvariantCulture)} MB";
                StatusText = toFranz
                    ? "Read the code to Franz. Leave the laptop on until Google Drive says it is done."
                    : "Write the code on the stick's label.";
                if (outcome.Damaged is { Count: > 0 } damaged)
                {
                    StatusText += $" {damaged.Count.ToString(CultureInfo.InvariantCulture)} older backup(s) on the stick are damaged — tell Franz.";
                }
            }
            else
            {
                ResultText = $"NOT OK — {outcome.Problem}";
                StatusText = "Nothing on this computer was changed.";
            }
        }
        catch (Exception ex)
        {
            // A command that throws ends the app; Jim reads a reason instead (Revision D, RR-5).
            Serilog.Log.Error(ex, "Backup to {Folder}", folder);
            Succeeded = false;
            ResultText = $"NOT OK — {ex.Message}";
            StatusText = "Read this to Franz.";
        }
        finally
        {
            IsRunning = false;
        }
    }

    private static bool TryCreate(string folder, out string? problem)
    {
        try
        {
            Directory.CreateDirectory(folder);
            problem = null;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            problem = $"cannot write to {folder} — is the stick plugged in?";
            return false;
        }
    }

    private static bool SameFolder(string a, string b) =>
        string.Equals(Path.TrimEndingDirectorySeparator(a), Path.TrimEndingDirectorySeparator(b), StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Whether a drive is offered: a stick, or a non-system disk a USB drive may show up as — never
    /// the system disk or one holding the station or its scans (a backup there is lost with them),
    /// and never Google Drive's mount, which would upload Jim's evidence by accident.
    /// </summary>
    public static bool Offered(string root, DriveType type, string label, string? systemRoot, IReadOnlyList<string> stationFolders) =>
        type is DriveType.Removable or DriveType.Fixed
        && !string.Equals(root, systemRoot, StringComparison.OrdinalIgnoreCase)
        && !label.Contains("Google Drive", StringComparison.OrdinalIgnoreCase)
        && !stationFolders.Any(f => string.Equals(Path.GetPathRoot(Path.GetFullPath(f)), root, StringComparison.OrdinalIgnoreCase));

    public static IReadOnlyList<BackupTarget> FindDrives(IReadOnlyList<string> stationFolders)
    {
        var system = Path.GetPathRoot(Environment.SystemDirectory);
        var targets = new List<BackupTarget>();
        foreach (var drive in DriveInfo.GetDrives())
        {
            try
            {
                if (!drive.IsReady || !Offered(drive.RootDirectory.FullName, drive.DriveType, drive.VolumeLabel, system, stationFolders))
                {
                    continue;
                }

                var name = string.IsNullOrWhiteSpace(drive.VolumeLabel) ? "drive" : drive.VolumeLabel;
                var free = (drive.AvailableFreeSpace / (double)(1L << 30)).ToString("N1", CultureInfo.InvariantCulture);
                targets.Add(new BackupTarget($"{drive.Name} — {name}, {free} GB free", Path.Combine(drive.RootDirectory.FullName, FolderName)));
            }
            catch (IOException)
            {
                // A drive ejected while listing.
            }
        }

        return targets;
    }
}
