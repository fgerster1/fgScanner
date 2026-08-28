using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FgScanner.App.Services;
using FgScanner.Core.Evidence;
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
    private readonly ProfileOcrTrigger _ocrTrigger;
    private readonly PageEditingToolset _toolset;
    private readonly TrashService _trashService;
    private CancellationTokenSource? _scanCts;

    public ScanViewModel(
        IScanService scanService,
        ScanSessionService sessionService,
        GroupService groupService,
        IndexingService indexingService,
        ActiveGroupStore activeGroup,
        ProfileOcrTrigger ocrTrigger,
        PageEditingToolset toolset,
        TrashService trashService)
    {
        _scanService = scanService;
        _sessionService = sessionService;
        _groupService = groupService;
        _indexingService = indexingService;
        _activeGroup = activeGroup;
        _ocrTrigger = ocrTrigger;
        _toolset = toolset;
        _trashService = trashService;
        _ = LoadFeatureFlagsAsync();
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

    private ScanProfileOptions BuildOptions() => new()
    {
        Device = SelectedDevice,
        Source = Source,
        Dpi = Dpi,
        BitDepth = BitDepth,
        PageSize = PageSize,
        Brightness = Brightness,
        Contrast = Contrast,
    };

    /// <summary>One scanner pass streaming pages into the session; shared by Scan and Batch.</summary>
    private async Task<int> RunScanPassAsync(CancellationToken cancellationToken)
    {
        var pagesBefore = Pages.Count;
        await foreach (var page in _scanService.ScanAsync(BuildOptions(), _sessionService.Session, cancellationToken))
        {
            Pages.Add(page);
            StatusText = $"Scanned page {Pages.Count - pagesBefore}…";
        }

        return Pages.Count - pagesBefore;
    }

    /// <summary>
    /// Set by "Scan into this group": that gesture names its destination up front, so making the
    /// user press "Save to group" afterwards asks a question they already answered. Off for an
    /// ordinary scan, where pages stay on screen until the user decides where they go.
    /// </summary>
    public bool AutoSaveAfterScan { get; set; }

    /// <summary>Raised only after pages have actually landed in the group.</summary>
    public event Action? SavedToGroup;

    [RelayCommand(CanExecute = nameof(CanScan))]
    private async Task ScanAsync()
    {
        IsScanning = true;
        _scanCts = new CancellationTokenSource();
        try
        {
            var scanned = await RunScanPassAsync(_scanCts.Token);
            _sessionService.Session.Flush();
            StatusText = $"Scan complete — {scanned} page(s).";

            // Only on the success path. A cancelled or failed run leaves whatever arrived on
            // screen: its pages are still reviewable and its error text lives in this status line.
            if (AutoSaveAfterScan && scanned > 0)
            {
                IsScanning = false; // CanSaveToGroup refuses while a scan is in flight
                if (CanSaveToGroup())
                {
                    await SaveToGroupAsync();
                }
            }
        }
        catch (OperationCanceledException)
        {
            StatusText = "Scan canceled.";
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Scan failed");
            _sessionService.Session.Flush();
            StatusText = $"Scan failed: {ex.Message}";
        }
        finally
        {
            IsScanning = false;
            _scanCts.Dispose();
            _scanCts = null;
        }
    }

    /// <summary>Batch scanning (PLAN §5.8): several passes with a prompt or delay between them,
    /// then straight into the save-to-group/commit flow.</summary>
    [RelayCommand(CanExecute = nameof(CanScan))]
    private async Task BatchScanAsync()
    {
        var dialog = new Dialogs.BatchDialog { Owner = System.Windows.Application.Current.MainWindow };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        IsScanning = true;
        _scanCts = new CancellationTokenSource();
        var total = 0;
        try
        {
            for (var pass = 1; pass <= dialog.Count; pass++)
            {
                if (pass > 1)
                {
                    if (dialog.Mode == Dialogs.BatchMode.MultipleWithPrompt)
                    {
                        var answer = System.Windows.MessageBox.Show(
                            $"Pass {pass} of {dialog.Count}: load the next batch, then continue.",
                            "Batch scan",
                            System.Windows.MessageBoxButton.OKCancel,
                            System.Windows.MessageBoxImage.Information);
                        if (answer != System.Windows.MessageBoxResult.OK)
                        {
                            break;
                        }
                    }
                    else if (dialog.Mode == Dialogs.BatchMode.MultipleWithDelay)
                    {
                        StatusText = $"Waiting {dialog.DelaySeconds}s before pass {pass}…";
                        await Task.Delay(TimeSpan.FromSeconds(dialog.DelaySeconds), _scanCts.Token);
                    }
                }

                total += await RunScanPassAsync(_scanCts.Token);
                _sessionService.Session.Flush();
                StatusText = $"Batch pass {pass}/{dialog.Count} done — {total} page(s) so far.";
            }

            StatusText = $"Batch complete — {total} page(s).";
            if (_activeGroup.Current is not null && Pages.Count > 0)
            {
                await SaveToGroupAsync();
            }
        }
        catch (OperationCanceledException)
        {
            StatusText = $"Batch canceled after {total} page(s).";
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Batch scan failed");
            _sessionService.Session.Flush();
            StatusText = $"Batch failed after {total} page(s): {ex.Message}";
        }
        finally
        {
            IsScanning = false;
            _scanCts.Dispose();
            _scanCts = null;
        }
    }

    /// <summary>
    /// The sheet in hand, if it carries notes. It owns the NoteState so the operator never
    /// types one — at roughly one sheet in four, a value typed that often is a value mistyped.
    /// </summary>
    public AnnotatedCaptureSequence Annotated { get; } = new();

    /// <summary>
    /// Captures a sheet with its notes in place. The capture is saved on its own, because
    /// ApplyInitialValuesAsync stamps one dictionary onto every document adopted in a save
    /// and the clean capture must not inherit this one's NoteState.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanScan))]
    private async Task ScanAnnotatedAsync()
    {
        if (!Annotated.IsActive)
        {
            Annotated.Start();
        }

        await ScanOneCaptureAsync();
    }

    /// <summary>Photographs the lifted note itself, for a note that cannot be read where it sits.</summary>
    [RelayCommand(CanExecute = nameof(CanScanNoteFace))]
    private async Task ScanNoteFaceAsync()
    {
        Annotated.TakeNoteFace();
        await ScanOneCaptureAsync();
    }

    private bool CanScanNoteFace() =>
        CanScan() && Annotated.NoteStateForNextCapture == AnnotatedCaptureSequence.Clean;

    private async Task ScanOneCaptureAsync()
    {
        var wasAutoSave = AutoSaveAfterScan;
        AutoSaveAfterScan = true;
        try
        {
            await ScanAsync();
        }
        finally
        {
            AutoSaveAfterScan = wasAutoSave;
            ScanNoteFaceCommand.NotifyCanExecuteChanged();
            CancelAnnotatedCommand.NotifyCanExecuteChanged();
        }
    }

    private bool CanCancelAnnotated() => Annotated.IsActive && !IsScanning;

    /// <summary>
    /// Abandons the sheet and takes its captures with it. An as-found with no clean partner is
    /// a whole-group refusal at import, by which time the box has been re-shelved. The pages go
    /// to the trash rather than to /dev/null, so a mis-click is recoverable.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanCancelAnnotated))]
    private async Task CancelAnnotatedAsync()
    {
        var discarded = Annotated.Cancel();
        foreach (var documentId in discarded)
        {
            await _trashService.DeleteDocumentAsync(documentId);
        }

        StatusText = discarded.Count == 1
            ? "Annotated sheet abandoned — 1 capture moved to the trash."
            : $"Annotated sheet abandoned — {discarded.Count} captures moved to the trash.";
        _activeGroup.NotifyGroupContentChanged();
        ScanNoteFaceCommand.NotifyCanExecuteChanged();
        CancelAnnotatedCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// Adds the sheet-in-hand's NoteState to the operator's pending values without disturbing
    /// them, so the value lives for exactly one capture. Pending values persist across scans
    /// until the group changes, and a NoteState that outlived its sheet would stamp `as-found`
    /// onto every plain sheet after it.
    /// </summary>
    private IReadOnlyDictionary<string, string?>? StampNoteState(
        IReadOnlyDictionary<string, string?>? pending)
    {
        if (Annotated.NoteStateForNextCapture is not { } noteState)
        {
            return pending;
        }

        var stamped = pending is null
            ? []
            : new Dictionary<string, string?>(pending, StringComparer.Ordinal);
        stamped["NoteState"] = noteState;
        return stamped;
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
            var triage = await _toolset.Triage.TriageAsync(
                group, [.. Pages.OrderBy(p => p.SequenceNumber).Select(p => p.FilePath)]);
            var result = await _groupService.AdoptPagesAsync(
                group.Id, triage.FilesToAdopt, triage.IsBlankFlagged);
            var adopted = result.Adopted.Select(p => p.DocumentId).ToList();
            await _indexingService.ApplyInitialValuesAsync(
                group.Id, adopted, StampNoteState(_activeGroup.PendingValues));
            foreach (var documentId in adopted)
            {
                if (Annotated.IsActive)
                {
                    Annotated.RecordCapture(documentId);
                }
            }
            if (group.State == GroupState.Committed)
            {
                await _indexingService.ReexportAsync(group.Id);
            }

            await _ocrTrigger.EnqueueIfProfileEnabledAsync(group);
            _activeGroup.NotifyGroupContentChanged();

            var stuck = result.FailedSourceFiles.Select(f => f.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var summary = $"Saved {result.Adopted.Count} page(s) to \"{group.Name}\"."
                + DuplicateReport.Format(result.DuplicateSourceFiles)
                + (triage.DroppedCount > 0
                    ? $" {triage.DroppedCount} page(s) dropped by capture policy (see journal.txt)."
                    : "");

            if (stuck.Count > 0)
            {
                // Keep exactly the pages that could not be taken, and let the session forget the
                // rest — they have moved into the group. Clearing everything here would discard
                // scans that are still only in the session folder.
                var consumed = Pages.Where(p => !stuck.Contains(p.FilePath)).Select(p => p.FilePath).ToList();
                _sessionService.Session.ForgetPages(consumed);
                foreach (var page in Pages.Where(p => consumed.Contains(p.FilePath)).ToList())
                {
                    Pages.Remove(page);
                }

                StatusText = summary
                    + $" {stuck.Count} page(s) could not be saved and are still here — "
                    + $"try again in a moment. ({result.FailedSourceFiles[0].Reason})";
                Log.Warning(
                    "Adoption left {Count} page(s) in the session: {Reason}",
                    stuck.Count, result.FailedSourceFiles[0].Reason);
                return;
            }

            Pages.Clear();
            _sessionService.ResetSession();
            StatusText = summary;

            // Inside the try, never a finally: a failed save must leave the user here, with their
            // pages still in hand and the reason on screen.
            SavedToGroup?.Invoke();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Saving pages to group {Group}", group.Name);
            StatusText = $"Saving to group failed: {ex.Message}";
        }
    }

    // ---- Patch-T separator sheets (PLAN prompt 10) ----

    [ObservableProperty]
    private bool _separatorSheetVisible;

    private async Task LoadFeatureFlagsAsync()
    {
        try
        {
            SeparatorSheetVisible = await FeatureFlags.IsEnabledAsync(_toolset.Settings, FeatureFlags.PatchT);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Loading feature flags");
        }
    }

    /// <summary>Saves a printable Patch-T separator sheet as PDF and opens it for printing.</summary>
    [RelayCommand]
    private async Task SaveSeparatorSheetAsync()
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Save separator sheet",
            Filter = "PDF|*.pdf",
            FileName = "FG Scanner separator sheet.pdf",
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var tempPng = Path.Combine(Path.GetTempPath(), $"fgscanner-separator-{Guid.NewGuid():N}.png");
        try
        {
            FgScanner.Scanning.Capture.SeparatorSheet.CreatePng(tempPng);
            await _toolset.PdfExport.ExportAsync(
                [tempPng], dialog.FileName,
                new FgScanner.Scanning.Export.PdfExportOptions { Title = "FG Scanner separator sheet" });
            StatusText = $"Separator sheet saved: {dialog.FileName}. Print one copy per document boundary.";
            System.Diagnostics.Process.Start(
                new System.Diagnostics.ProcessStartInfo(dialog.FileName) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Separator sheet");
            StatusText = $"Separator sheet failed: {ex.Message}";
        }
        finally
        {
            try
            {
                File.Delete(tempPng);
            }
            catch (IOException)
            {
            }
        }
    }

    public void Dispose() => _scanCts?.Dispose();
}
