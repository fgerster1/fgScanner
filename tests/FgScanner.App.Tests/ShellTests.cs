using System.IO;
using FgScanner.App.Services;
using FgScanner.App.Views;
using FgScanner.Scanning;
using Xunit;

namespace FgScanner.App.Tests;

public sealed class ShellTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "fgscanner-tests", Guid.NewGuid().ToString("N"));
    private readonly ScanSessionService _sessionService;

    public ShellTests()
    {
        _sessionService = new ScanSessionService(_root);
    }

    public void Dispose()
    {
        _sessionService.Dispose();
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private ScanViewModel CreateScanViewModel(FakeScanService? service = null) =>
        new(service ?? new FakeScanService(), _sessionService);

    [Fact]
    public void Shell_starts_on_scan_section()
    {
        var shell = new ShellViewModel(CreateScanViewModel());
        Assert.Equal(["Scan", "Groups", "Settings"], shell.Sections);
        Assert.Equal("Scan", shell.SelectedSection);
    }

    [Fact]
    public async Task Scan_command_streams_pages_into_the_thumbnail_list()
    {
        var vm = CreateScanViewModel(new FakeScanService { PageCount = 3 });
        await vm.RefreshDevicesCommand.ExecuteAsync(null);
        Assert.NotNull(vm.SelectedDevice);

        vm.Source = ScanSource.Feeder;
        await vm.ScanCommand.ExecuteAsync(null);

        Assert.Equal(3, vm.Pages.Count);
        Assert.All(vm.Pages, p => Assert.True(File.Exists(p.FilePath)));
        Assert.False(vm.IsScanning);
        Assert.Contains("complete", vm.StatusText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Driver_failure_keeps_partial_pages_and_reports_error()
    {
        var vm = CreateScanViewModel(new FakeScanService
        {
            PageCount = 5,
            Error = new IOException("paper jam"),
            ErrorAfterPages = 2,
        });
        await vm.RefreshDevicesCommand.ExecuteAsync(null);
        vm.Source = ScanSource.Feeder;

        await vm.ScanCommand.ExecuteAsync(null);

        Assert.Equal(2, vm.Pages.Count);
        Assert.Contains("failed", vm.StatusText, StringComparison.OrdinalIgnoreCase);
        Assert.False(vm.IsScanning);
    }

    [Fact]
    public void Recovered_pages_appear_in_the_list_at_startup()
    {
        // A crashed session with two pages…
        var crashedRoot = _root; // same recovery root the service watches
        var crashed = FgScanner.Scanning.Recovery.RecoverySession.Create(crashedRoot);
        File.WriteAllBytes(crashed.ReserveNextPagePath("png"), [1]);
        crashed.CommitPage(new ScannedPage(Path.Combine(crashed.FolderPath, "page-00001.png"), 1));
        crashed.Flush();
        crashed.Dispose();

        var orphan = Assert.Single(_sessionService.FindOrphanedSessions());
        var recovered = _sessionService.RecoverInto(orphan);

        var vm = CreateScanViewModel();
        Assert.Single(recovered);
        Assert.Single(vm.Pages);
    }
}
