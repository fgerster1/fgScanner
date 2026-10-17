using System.IO;
using FgScanner.App.Services;
using FgScanner.App.Views;
using FgScanner.Core.Station;
using FgScanner.Data;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FgScanner.App.Tests;

/// <summary>
/// The Back up window (SPEC-SCANNER-2026-011 §03 item 4): Jim picks his stick, the backup lands in
/// a <c>FGScanner-Backup</c> folder on it, and he reads OK and the code — or the reason it did not.
/// The stick he used is remembered.
/// </summary>
public sealed class BackupViewModelTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("fg-backup-vm").FullName;

    private StationPaths Station => StationPaths.Resolve(Path.Combine(_root, "station-jim"));

    private string StickRoot => Path.Combine(_root, "E");

    private readonly AppSettingsService _settings;

    public BackupViewModelTests()
    {
        Directory.CreateDirectory(Station.Root);
        using (var db = new FgScannerDbContext(DbBootstrapper.BuildOptions(Station.Database)))
        {
            db.Database.Migrate();
            db.Profiles.Add(new Profile { Name = "Evidence", BaseDirectory = Path.Combine(_root, "Evidence-Scans") });
            db.SaveChanges();
        }

        Directory.CreateDirectory(Path.Combine(_root, "Evidence-Scans", "Dental"));
        File.WriteAllText(Path.Combine(_root, "Evidence-Scans", "Dental", "scan_00001.jpg"), "jpeg");
        Directory.CreateDirectory(StickRoot);
        _settings = new AppSettingsService(new Factory(Station.Database));
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private sealed class Factory(string dbPath) : IDbContextFactory<FgScannerDbContext>
    {
        public FgScannerDbContext CreateDbContext() => new(DbBootstrapper.BuildOptions(dbPath));
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private BackupViewModel ViewModel(string? whyNotNow = null) => new(
        new BackupService(Station, new WorkGate()), _settings, () => whyNotNow,
        () => [new BackupTarget($"{StickRoot} — USB stick", Path.Combine(StickRoot, BackupViewModel.FolderName))]);

    [Fact]
    public async Task A_backup_to_the_stick_lands_in_its_backup_folder_and_shows_the_code()
    {
        var vm = ViewModel();
        await vm.LoadAsync(Token);
        vm.SelectedTarget = vm.Targets.Single();

        await vm.BackupCommand.ExecuteAsync(null);

        Assert.True(vm.Succeeded);
        Assert.Matches("^OK [0-9a-f]{4}-[0-9a-f]{4}", vm.ResultText);
        Assert.Single(Directory.GetDirectories(Path.Combine(StickRoot, BackupViewModel.FolderName)));
        Assert.False(vm.IsRunning);
    }

    [Fact]
    public async Task The_stick_used_is_remembered()
    {
        var first = ViewModel();
        await first.LoadAsync(Token);
        first.SelectedTarget = first.Targets.Single();
        await first.BackupCommand.ExecuteAsync(null);

        var next = ViewModel();
        await next.LoadAsync(Token);

        Assert.Equal(Path.Combine(StickRoot, BackupViewModel.FolderName), next.SelectedTarget?.Folder);
    }

    [Fact]
    public async Task A_refusal_shows_NOT_OK_and_the_reason()
    {
        var vm = ViewModel(whyNotNow: "the Scan page has pages not yet saved — save or discard them first");
        await vm.LoadAsync(Token);
        vm.SelectedTarget = vm.Targets.Single();

        await vm.BackupCommand.ExecuteAsync(null);

        Assert.False(vm.Succeeded);
        Assert.Equal("NOT OK — the Scan page has pages not yet saved — save or discard them first", vm.ResultText);
    }

    /// <summary>§03 item 6: the same checked copy goes to Franz through the shared Drive folder;
    /// only the newest one is kept there, since Drive space is shared and Franz wants the latest.</summary>
    [Fact]
    public async Task A_test_copy_goes_to_Franzs_copies_folder_and_only_the_newest_stays()
    {
        var copies = Path.Combine(_root, "Drive", "to-franz", "copies");
        var vm = new BackupViewModel(new BackupService(Station, new WorkGate()), _settings, () => null, () => [], copies);
        await vm.LoadAsync(Token);
        Assert.True(vm.SendTestCopyCommand.CanExecute(null));

        await vm.SendTestCopyCommand.ExecuteAsync(null);
        await vm.SendTestCopyCommand.ExecuteAsync(null);

        Assert.True(vm.Succeeded);
        Assert.Matches("^OK [0-9a-f]{4}-[0-9a-f]{4}", vm.ResultText);
        Assert.Single(Directory.GetDirectories(copies));
        // A copy for Franz is not Jim's backup: "Last backup" speaks only of the stick.
        Assert.Null(FgScanner.Core.Station.LastBackupRecord.Read(Station.Root));
    }

    [Fact]
    public async Task Without_the_Drive_folder_a_test_copy_cannot_be_sent()
    {
        var vm = ViewModel();
        await vm.LoadAsync(Token);

        Assert.False(vm.SendTestCopyCommand.CanExecute(null));
    }

    /// <summary>Revision C CR-7: the setting the spec names decides how many stay.</summary>
    [Fact]
    public async Task Backup_Keep_decides_how_many_backups_stay()
    {
        await _settings.SetAsync(BackupViewModel.KeepSettingKey, "1", Token);
        var vm = ViewModel();
        await vm.LoadAsync(Token);
        vm.SelectedTarget = vm.Targets.Single();

        await vm.BackupCommand.ExecuteAsync(null);
        await vm.BackupCommand.ExecuteAsync(null);

        Assert.Single(Directory.GetDirectories(Path.Combine(StickRoot, BackupViewModel.FolderName)));
    }

    /// <summary>Revision C SR-6: Browse cannot pick a share or the Google Drive mount — the backup
    /// would upload Jim's evidence.</summary>
    [Theory]
    [InlineData(@"\\nas\share")]
    public async Task Browse_refuses_a_network_folder(string folder)
    {
        var vm = ViewModel();
        await vm.LoadAsync(Token);

        Assert.False(vm.UseFolder(folder));
        Assert.DoesNotContain(vm.Targets, t => t.Folder == folder);
        Assert.Contains("on this computer", vm.StatusText);
    }

    [Fact]
    public void The_drives_offered_leave_out_the_ones_holding_the_station()
    {
        Assert.False(BackupViewModel.Offered(@"C:\", DriveType.Fixed, "OS", @"C:\", [@"D:\"]));
        Assert.False(BackupViewModel.Offered(@"D:\", DriveType.Fixed, "DATA", @"C:\", [@"D:\Evidence-Scans"]));
        Assert.False(BackupViewModel.Offered(@"G:\", DriveType.Fixed, "Google Drive", @"C:\", []));
        Assert.False(BackupViewModel.Offered(@"Z:\", DriveType.Network, "nas", @"C:\", []));
        Assert.True(BackupViewModel.Offered(@"E:\", DriveType.Removable, "SANDISK", @"C:\", [@"C:\Evidence-Scans"]));
    }

    [Fact]
    public async Task With_no_stick_it_says_to_plug_one_in()
    {
        var vm = new BackupViewModel(new BackupService(Station, new WorkGate()), _settings, () => null, () => []);
        await vm.LoadAsync(Token);

        Assert.Null(vm.SelectedTarget);
        Assert.False(vm.BackupCommand.CanExecute(null));
        Assert.Contains("Plug in your USB stick", vm.StatusText);
    }
}
