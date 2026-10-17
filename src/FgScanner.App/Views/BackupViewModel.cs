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
public sealed partial class BackupViewModel(
    BackupService backup,
    AppSettingsService settings,
    Func<string?> whyNotNow,
    Func<IReadOnlyList<BackupTarget>> findTargets) : ObservableObject
{
    public const string FolderName = "FGScanner-Backup";

    public const string FolderSettingKey = "Backup.Folder";

    /// <summary>Not cleared on reload: the drive list binds SelectedItem (CLAUDE.md, never clear a
    /// collection bound to a Selector).</summary>
    public ObservableCollection<BackupTarget> Targets { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(BackupCommand))]
    private BackupTarget? _selectedTarget;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(BackupCommand))]
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

    /// <summary>A folder chosen with Browse.</summary>
    public void UseFolder(string folder)
    {
        var target = Targets.FirstOrDefault(t => SameFolder(t.Folder, folder)) ?? new BackupTarget(folder, folder);
        if (!Targets.Contains(target))
        {
            Targets.Add(target);
        }

        SelectedTarget = target;
        StatusText = "Leave the stick in until this says OK.";
    }

    private bool CanBackup() => SelectedTarget is not null && !IsRunning;

    [RelayCommand(CanExecute = nameof(CanBackup))]
    private async Task BackupAsync()
    {
        var target = SelectedTarget!;
        IsRunning = true;
        Succeeded = false;
        ResultText = "";
        StatusText = "Backing up — leave the stick in until this says OK.";
        try
        {
            var refusal = whyNotNow();
            if (refusal is null && !TryCreate(target.Folder, out var problem))
            {
                refusal = problem;
            }

            var outcome = refusal is null
                ? await backup.BackupAsync(target.Folder, null, CancellationToken.None)
                : BackupOutcome.Refused(refusal);
            Succeeded = outcome.Problem is null;
            if (Succeeded)
            {
                await settings.SetAsync(FolderSettingKey, target.Folder);
                ResultText = $"OK {outcome.Code} — {outcome.Files.ToString("N0", CultureInfo.InvariantCulture)} files, " +
                             $"{(outcome.Bytes / (double)(1L << 20)).ToString("N0", CultureInfo.InvariantCulture)} MB";
                StatusText = "Write the code on the stick's label.";
            }
            else
            {
                ResultText = $"NOT OK — {outcome.Problem}";
                StatusText = "Nothing on this computer was changed.";
            }
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
    /// Sticks, and the non-system disks a USB drive may show up as. The system drive is left out:
    /// a backup on the same disk as the station is lost with it.
    /// </summary>
    public static IReadOnlyList<BackupTarget> FindDrives()
    {
        var system = Path.GetPathRoot(Environment.SystemDirectory);
        var targets = new List<BackupTarget>();
        foreach (var drive in DriveInfo.GetDrives())
        {
            try
            {
                // Google Drive for desktop mounts as a fixed disk; a backup there would upload Jim's
                // evidence by accident. The test copy for Franz goes to Drive on purpose, elsewhere.
                if (!drive.IsReady || drive.DriveType is not (DriveType.Removable or DriveType.Fixed)
                    || string.Equals(drive.RootDirectory.FullName, system, StringComparison.OrdinalIgnoreCase)
                    || drive.VolumeLabel.Contains("Google Drive", StringComparison.OrdinalIgnoreCase))
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
