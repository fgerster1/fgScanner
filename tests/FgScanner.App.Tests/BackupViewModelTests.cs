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
