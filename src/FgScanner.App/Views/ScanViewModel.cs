using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FgScanner.App.Services;
using FgScanner.Data;
using FgScanner.Scanning;
using Serilog;

namespace FgScanner.App.Views;

public sealed partial class ScanViewModel : ObservableObject, IDisposable
{
    private readonly IScanService _scanService;
    private readonly ScanSessionService _sessionService;
    private readonly GroupService _groupService;
    private readonly IndexingService _indexingService;
    private readonly ActiveGroupStore _activeGroup;
    private CancellationTokenSource? _scanCts;

    public ScanViewModel(
        IScanService scanService,
        ScanSessionService sessionService,
        GroupService groupService,
        IndexingService indexingService,
        ActiveGroupStore activeGroup)
    {
        _scanService = scanService;
        _sessionService = sessionService;
        _groupService = groupService;
        _indexingService = indexingService;
        _activeGroup = activeGroup;
        Drivers = [.. scanService.AvailableDrivers];
        _selectedDriver = Drivers[0];
        activeGroup.PropertyChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(SaveTargetText));
            SaveToGroupCommand.NotifyCanExecuteChanged();
        };

        Pages.CollectionChanged += (_, _) => SaveToGroupCommand.NotifyCanExecuteChanged();

        foreach (var page in sessionService.Session.Pages)
        {
            Pages.Add(page);
        }
    }

    public string SaveTargetText =>
        _activeGroup.Current is { } g ? $"Save to group \"{g.Name}\"" : "Save to group (select one in Groups)";

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
    [NotifyCanExecuteChangedFor(nameof(SaveToGroupCommand))]
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

    private bool CanSaveToGroup() => _activeGroup.Current is not null && Pages.Count > 0 && !IsScanning;

    /// <summary>Moves the session's pages into the active group (files + DB rows), then resets the session.</summary>
    [RelayCommand(CanExecute = nameof(CanSaveToGroup))]
    private async Task SaveToGroupAsync()
    {
        var group = _activeGroup.Current!;
        try
        {
            var result = await _groupService.AdoptPagesAsync(
                group.Id, Pages.OrderBy(p => p.SequenceNumber).Select(p => p.FilePath));
            await _indexingService.ApplyInitialValuesAsync(
                group.Id, [.. result.Adopted.Select(p => p.DocumentId)], _activeGroup.PendingValues);
            if (group.State == GroupState.Committed)
            {
                await _indexingService.ReexportAsync(group.Id);
            }

            _activeGroup.NotifyGroupContentChanged();
            Pages.Clear();
            _sessionService.ResetSession();
            StatusText = result.DuplicateSourceFiles.Count == 0
                ? $"Saved {result.Adopted.Count} page(s) to \"{group.Name}\"."
                : $"Saved {result.Adopted.Count} page(s) to \"{group.Name}\"; {result.DuplicateSourceFiles.Count} duplicate(s) skipped.";
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Saving pages to group {Group}", group.Name);
            StatusText = $"Saving to group failed: {ex.Message}";
        }
    }

    public void Dispose() => _scanCts?.Dispose();
}
