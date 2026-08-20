using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FgScanner.App.Services;
using FgScanner.Scanning;
using Serilog;

namespace FgScanner.App.Views;

public sealed partial class ScanViewModel : ObservableObject, IDisposable
{
    private readonly IScanService _scanService;
    private readonly ScanSessionService _sessionService;
    private CancellationTokenSource? _scanCts;

    public ScanViewModel(IScanService scanService, ScanSessionService sessionService)
    {
        _scanService = scanService;
        _sessionService = sessionService;
        Drivers = [.. scanService.AvailableDrivers];
        _selectedDriver = Drivers[0];

        foreach (var page in sessionService.Session.Pages)
        {
            Pages.Add(page);
        }
    }

    public IReadOnlyList<ScanDriver> Drivers { get; }

    public IReadOnlyList<ScanSource> Sources { get; } = Enum.GetValues<ScanSource>();

    public IReadOnlyList<ScanBitDepth> BitDepths { get; } = Enum.GetValues<ScanBitDepth>();

    public IReadOnlyList<ScanPageSize> PageSizes { get; } = Enum.GetValues<ScanPageSize>();

    public IReadOnlyList<int> DpiChoices { get; } = [100, 150, 200, 300, 400, 600];

    public ObservableCollection<ScanDeviceInfo> Devices { get; } = [];

    public ObservableCollection<ScannedPage> Pages { get; } = [];

    [ObservableProperty]
    private ScanDriver _selectedDriver;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ScanCommand))]
    private ScanDeviceInfo? _selectedDevice;

    [ObservableProperty]
    private ScanSource _source = ScanSource.Flatbed;

    [ObservableProperty]
    private int _dpi = 300;

    [ObservableProperty]
    private ScanBitDepth _bitDepth = ScanBitDepth.Color;

    [ObservableProperty]
    private ScanPageSize _pageSize = ScanPageSize.Letter;

    [ObservableProperty]
    private int _brightness;

    [ObservableProperty]
    private int _contrast;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ScanCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelScanCommand))]
    private bool _isScanning;

    [ObservableProperty]
    private string _statusText = "Select a device and scan.";

    partial void OnSelectedDriverChanged(ScanDriver value) => _ = RefreshDevicesAsync();

    [RelayCommand]
    private async Task RefreshDevicesAsync()
    {
        Devices.Clear();
        SelectedDevice = null;
        StatusText = $"Searching for {SelectedDriver} devices…";
        try
        {
            var devices = await _scanService.ListDevicesAsync(SelectedDriver);
            foreach (var device in devices)
            {
                Devices.Add(device);
            }

            SelectedDevice = Devices.FirstOrDefault();
            StatusText = Devices.Count == 0 ? $"No {SelectedDriver} devices found." : $"{Devices.Count} device(s) found.";
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Log.Error(ex, "Device enumeration failed for {Driver}", SelectedDriver);
            StatusText = $"Device search failed: {ex.Message}";
        }
    }

    private bool CanScan() => SelectedDevice is not null && !IsScanning;

    [RelayCommand(CanExecute = nameof(CanScan))]
    private async Task ScanAsync()
    {
        var options = new ScanProfileOptions
        {
            Device = SelectedDevice,
            Source = Source,
            Dpi = Dpi,
            BitDepth = BitDepth,
            PageSize = PageSize,
            Brightness = Brightness,
            Contrast = Contrast,
        };

        IsScanning = true;
        _scanCts = new CancellationTokenSource();
        var pagesBefore = Pages.Count;
        try
        {
            await foreach (var page in _scanService.ScanAsync(options, _sessionService.Session, _scanCts.Token))
            {
                Pages.Add(page);
                StatusText = $"Scanned page {Pages.Count - pagesBefore}…";
            }

            _sessionService.Session.Flush();
            StatusText = $"Scan complete — {Pages.Count - pagesBefore} page(s).";
        }
        catch (OperationCanceledException)
        {
            StatusText = $"Scan canceled after {Pages.Count - pagesBefore} page(s).";
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Scan failed");
            _sessionService.Session.Flush();
            StatusText = $"Scan failed after {Pages.Count - pagesBefore} page(s): {ex.Message}";
        }
        finally
        {
            IsScanning = false;
            _scanCts.Dispose();
            _scanCts = null;
        }
    }

    private bool CanCancelScan() => IsScanning;

    [RelayCommand(CanExecute = nameof(CanCancelScan))]
    private void CancelScan() => _scanCts?.Cancel();

    public void Dispose() => _scanCts?.Dispose();
}
