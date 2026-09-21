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
    private readonly IStagedPageDiscarder _discarder;
    private CancellationTokenSource? _scanCts;

    public ScanViewModel(
        IScanService scanService,
        ScanSessionService sessionService,
        GroupService groupService,
        IndexingService indexingService,
        ActiveGroupStore activeGroup,
        ProfileOcrTrigger ocrTrigger,
        PageEditingToolset toolset,
        TrashService trashService,
        IStagedPageDiscarder? stagedPageDiscarder = null)
    {
        _discarder = stagedPageDiscarder ?? new RecycleBinDiscarder();
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

        Pages.CollectionChanged += (_, _) =>
        {
            SaveToGroupCommand.NotifyCanExecuteChanged();
            OpenPageViewerCommand.NotifyCanExecuteChanged();
        };
        SelectedPages.CollectionChanged += (_, _) => DeleteSelectedPagesCommand.NotifyCanExecuteChanged();

        foreach (var page in sessionService.Session.Pages)
        {
            Pages.Add(page);
        }
    }

    public string SaveTargetText =>
        _activeGroup.Current is { } g ? $"Save to group \"{g.Name}\"" : "Save to group (select one in Groups)";

    public IReadOnlyList<ScanDriver> Drivers { get; }

    /// <summary>
    /// Built once and never rebuilt; each entry is switched on or off when a device is chosen.
    /// Every source used to be offered whatever the scanner could do, under its bare enum name, so
    /// a flatbed-only device set to Duplex reached the driver and came back with a raw
    /// NoDuplexSupportException (SPEC-2026-006 §04).
    /// </summary>
    public IReadOnlyList<SourceOption> Sources { get; } =
    [
        new(ScanSource.Flatbed, "Flatbed"),
        new(ScanSource.Feeder, "Feeder (one side)"),
        new(ScanSource.Duplex, "Feeder (both sides, one pass)"),
    ];

    /// <summary>
    /// The capability probe for the device now selected. Exposed so a test can await the answer;
    /// the probe is started by the selection, which nothing else gives a handle on.
    /// </summary>
    public Task CapabilitiesSettled { get; private set; } = Task.CompletedTask;

    public IReadOnlyList<ScanBitDepth> BitDepths { get; } = Enum.GetValues<ScanBitDepth>();

    public IReadOnlyList<ScanPageSize> PageSizes { get; } = Enum.GetValues<ScanPageSize>();

    public IReadOnlyList<int> DpiChoices { get; } = [100, 150, 200, 300, 400, 600];

    public ObservableCollection<ScanDeviceInfo> Devices { get; } = [];

    public ObservableCollection<ScannedPage> Pages { get; } = [];

    /// <summary>The thumbnails selected on screen, kept in sync by the view.</summary>
    public ObservableCollection<ScannedPage> SelectedPages { get; } = [];

    /// <summary>
    /// Shows the viewer over these paths from a start index and returns the index it closed on.
    /// Replaceable so opening the viewer can be tested without a window.
    /// </summary>
    public Func<IReadOnlyList<string>, int, int> ShowPageViewer { get; set; } = Dialogs.PageViewerWindow.ShowModal;

    private bool CanOpenPageViewer() => Pages.Count > 0;

    /// <summary>
    /// Opens the scanned pages full size, before they are saved, so a crooked or double-fed page is
    /// caught while it can still be rescanned. View-only: nothing about the page changes here.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanOpenPageViewer))]
    private void OpenPageViewer(ScannedPage? page)
    {
        var ordered = Pages.OrderBy(p => p.SequenceNumber).ToList();
        var chosen = page ?? SelectedPages.FirstOrDefault();
        var start = chosen is null ? 0 : Math.Max(0, ordered.IndexOf(chosen));
        var landed = ShowPageViewer([.. ordered.Select(p => p.FilePath)], start);

        // Delete acts on the selection, so it follows the viewer: closing on a double-fed page and
        // pressing Delete must remove that page, not the one the viewer was opened on.
        if (landed >= 0 && landed < ordered.Count)
        {
            SelectedPages.Clear();
            SelectedPages.Add(ordered[landed]);
        }
    }

    /// <summary>Asks before deleting, with Cancel as the default answer. Replaceable so tests show no dialog.</summary>
    public Func<string, bool> ConfirmDelete { get; set; } = message =>
        System.Windows.MessageBox.Show(
            message,
            "Delete scanned pages",
            System.Windows.MessageBoxButton.OKCancel,
            System.Windows.MessageBoxImage.Warning,
            System.Windows.MessageBoxResult.Cancel) == System.Windows.MessageBoxResult.OK;

    /// <summary>
    /// Set while pages move into a group. Adoption moves the files, so a delete then recycles nothing
    /// and the page it meant to remove lands in the group anyway. The save that follows "Scan into
    /// this group" runs after IsScanning has cleared, so that guard does not cover it. A count, not a
    /// flag: Save to group can start while that auto-save is still running, and the first to finish
    /// must not re-enable Delete while the other is moving files.
    /// </summary>
    private int _savesRunning;

    private bool CanDeleteSelectedPages() => SelectedPages.Count > 0 && !IsScanning && _savesRunning == 0;

    /// <summary>
    /// Deletes the selected pages before they reach a group, to the Recycle Bin so a mis-click can be
    /// put back. They leave the recovery index before their files go, because an index naming a
    /// missing file stops recovery at that file. A file the Recycle Bin refuses stays in the session
    /// folder, no longer listed, and is removed with the folder.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanDeleteSelectedPages))]
    private void DeleteSelectedPages()
    {
        var doomed = Pages.Where(SelectedPages.Contains).ToList();
        var noun = doomed.Count == 1 ? "page" : "pages";
        if (!ConfirmDelete(
            $"Move {doomed.Count} scanned {noun} to the Recycle Bin? They have not been saved to a group."))
        {
            return;
        }

        var session = _sessionService.Session;
        try
        {
            session.ForgetPages(doomed.Select(p => p.FilePath));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // No file goes while the index on disk may still name it, so the pages stay on screen.
            // Nothing above this catches a command's exception: letting it out closes the app.
            Log.Warning(ex, "Could not update the recovery index in {Folder} before deleting pages", session.FolderPath);
            StatusText = $"Could not delete: the scan session's recovery index could not be updated ({ex.Message}). "
                + "Nothing was deleted; try again.";
            return;
        }

        var refused = new List<string>();
        foreach (var page in doomed)
        {
            if (!_discarder.TryDiscard(session.FolderPath, page.FilePath, out var reason))
            {
                refused.Add(Path.GetFileName(page.FilePath));
                Log.Warning("Could not move staged page {File} to the Recycle Bin: {Reason}", page.FilePath, reason);
            }

            Pages.Remove(page);
            SelectedPages.Remove(page);
        }

        Log.Information("Deleted {Count} staged page(s) from {Folder}", doomed.Count, session.FolderPath);
        StatusText = refused.Count == 0
            ? $"Deleted {doomed.Count} page(s) — in the Recycle Bin."
            : $"Deleted {doomed.Count} page(s). Could not move {string.Join(", ", refused)} to the Recycle Bin; "
                + (refused.Count == 1 ? "it is" : "they are") + " no longer listed and removed with the scan session.";
    }

    [ObservableProperty]
    private ScanDriver _selectedDriver;

    // Every command gated on CanScan, not just ScanCommand: a CanExecute that
    // is never re-evaluated leaves its button dead for the life of the window.
    // Selecting a device is the moment they all become possible.
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ScanCommand))]
    [NotifyCanExecuteChangedFor(nameof(BatchScanCommand))]
    [NotifyCanExecuteChangedFor(nameof(ScanAnnotatedCommand))]
    [NotifyCanExecuteChangedFor(nameof(ScanNoteFaceCommand))]
    private ScanDeviceInfo? _selectedDevice;

    [ObservableProperty]
    private ScanSource _source = ScanSource.Flatbed;

    /// <summary>
    /// Why the chosen source cannot be used, shown under the combo. A disabled entry explains
    /// itself only on hover, and a source chosen before the device was is still the selection.
    /// </summary>
    [ObservableProperty]
    private string _sourceWarning = "";

    partial void OnSourceChanged(ScanSource value)
    {
        SourceWarning = Sources.FirstOrDefault(o => o.Source == value && !o.IsSupported)?.Reason ?? "";
        OnPropertyChanged(nameof(CanFlipDuplexedPages));
    }

    /// <summary>
    /// Corrects backs that a one-pass duplex scanner hands back upside down. Off by default, and
    /// it reaches the driver only for Duplex: there are no backs to turn over on a source that
    /// scans one side.
    /// </summary>
    [ObservableProperty]
    private bool _flipDuplexedPages;

    public bool CanFlipDuplexedPages => Source == ScanSource.Duplex;

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
    [NotifyCanExecuteChangedFor(nameof(BatchScanCommand))]
    [NotifyCanExecuteChangedFor(nameof(ScanAnnotatedCommand))]
    [NotifyCanExecuteChangedFor(nameof(ScanNoteFaceCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelScanCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelAnnotatedCommand))]
    [NotifyCanExecuteChangedFor(nameof(SaveToGroupCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeleteSelectedPagesCommand))]
    private bool _isScanning;

    [ObservableProperty]
    private string _statusText = "Select a device and scan.";

    partial void OnSelectedDriverChanged(ScanDriver value) => _ = RefreshDevicesAsync();

    partial void OnSelectedDeviceChanged(ScanDeviceInfo? value) => CapabilitiesSettled = ProbeAsync(value);

    /// <summary>
    /// Asks the chosen device what it can do, once. Never called from the scan path: the answer
    /// cannot change between pages, and a round trip to the driver mid-run costs the operator time
    /// for nothing.
    /// </summary>
    private async Task ProbeAsync(ScanDeviceInfo? device)
    {
        var capabilities = ScanCapabilities.Everything;
        if (device is not null)
        {
            try
            {
                capabilities = await _scanService.GetCapabilitiesAsync(device);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // §16 R5: the probe is a convenience, not a gate. A driver that will not answer
                // must not be able to withhold a source the hardware has.
                Log.Warning(ex, "Could not read capabilities from {Device}; offering every source", device.Name);
            }
        }

        foreach (var option in Sources)
        {
            option.Apply(capabilities);
        }

        SourceWarning = Sources.FirstOrDefault(o => o.Source == Source && !o.IsSupported)?.Reason ?? "";
    }

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

            // The selection starts the capability probe; awaiting it here means anyone who awaited
            // the refresh is looking at the answer rather than at the list as it was before.
            await CapabilitiesSettled;
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
        FlipDuplexedPages = FlipDuplexedPages,
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
        catch (ScanException ex)
        {
            // Already in the operator's words, and it says what to do next — so it is shown as
            // written, without "Scan failed:" in front of an instruction.
            Log.Warning(ex, "Scan refused by the device");
            _sessionService.Session.Flush();
            StatusText = ex.Message;
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

        await SettledAsync();
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

        await SettledAsync();
    }

    /// <summary>
    /// The sheet in hand, if it carries notes. It owns the NoteState so the operator never
    /// types one — at roughly one sheet in four, a value typed that often is a value mistyped.
    /// </summary>
    public AnnotatedCaptureSequence Annotated { get; } = new();

    /// <summary>Whether a sheet with notes is part-scanned right now.</summary>
    public bool AnnotatedActive => Annotated.IsActive;

    /// <summary>
    /// Paper is mid-flight: a scan is running, or a sheet with notes is part-captured. Reloading
    /// this page's state now would strand an as-found capture with no clean partner, which is a
    /// whole-group refusal at import (CLAUDE.md), so callers wait for <see cref="CaptureSettled"/>.
    /// </summary>
    public bool CaptureInHand => IsScanning || AnnotatedActive;

    /// <summary>
    /// Raised once nothing is in hand any more — a scan finished, or a sheet was completed or
    /// abandoned. It returns a Task and is awaited so a deferred reload is part of the operation
    /// that released the page, rather than a race against it.
    /// </summary>
    public event Func<Task>? CaptureSettled;

    private async Task SettledAsync()
    {
        if (CaptureSettled is null || CaptureInHand)
        {
            return;
        }

        foreach (var handler in CaptureSettled.GetInvocationList().Cast<Func<Task>>())
        {
            await handler();
        }
    }

    /// <summary>
    /// What the operator does next, or empty when no sheet is in hand.
    ///
    /// The sequence is otherwise invisible: a sheet stays part-scanned with
    /// nothing on screen saying so, and the next ordinary scan silently becomes
    /// its clean capture.
    /// </summary>
    public string AnnotatedPrompt => Annotated.NoteStateForNextCapture switch
    {
        AnnotatedCaptureSequence.AsFound =>
            "Notes in place — scan the sheet exactly as you found it.",
        AnnotatedCaptureSequence.NoteFace =>
            "Scan the lifted note on its own.",
        AnnotatedCaptureSequence.Clean =>
            "Lift every note, then Scan the sheet clean. Put the notes back afterwards.",
        _ => "",
    };

    /// <summary>
    /// Announces the sequence's state to the view. A change nobody announces
    /// leaves Cancel hidden while a sheet is genuinely in hand -- and that is
    /// the one control this design says must be reachable, because an as-found
    /// with no clean partner is a whole-group refusal at import, by which time
    /// the box has been re-shelved.
    /// </summary>
    private void AnnouncedAnnotatedState()
    {
        OnPropertyChanged(nameof(AnnotatedActive));
        OnPropertyChanged(nameof(AnnotatedPrompt));
        ScanNoteFaceCommand.NotifyCanExecuteChanged();
        CancelAnnotatedCommand.NotifyCanExecuteChanged();
    }

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

        AnnouncedAnnotatedState();
        await ScanOneCaptureAsync();
    }

    /// <summary>Photographs the lifted note itself, for a note that cannot be read where it sits.</summary>
    [RelayCommand(CanExecute = nameof(CanScanNoteFace))]
    private async Task ScanNoteFaceAsync()
    {
        Annotated.TakeNoteFace();
        AnnouncedAnnotatedState();
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
            AnnouncedAnnotatedState();
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
        AnnouncedAnnotatedState();
        await SettledAsync();
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
        _savesRunning++;
        DeleteSelectedPagesCommand.NotifyCanExecuteChanged();
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
        finally
        {
            _savesRunning--;
            DeleteSelectedPagesCommand.NotifyCanExecuteChanged();
        }

        // An annotated sheet ends HERE, not at the end of a scan: the clean capture is taken with
        // the ordinary Scan key and staged, so the sequence is still in hand when ScanAsync
        // finishes. Without this, a settings change deferred during the sheet was never applied.
        await SettledAsync();
    }

    // ---- Patch-T separator sheets (PLAN prompt 10) ----

    [ObservableProperty]
    private bool _separatorSheetVisible;

    /// <summary>
    /// Re-reads the flags that decide which controls exist. Called again when Settings announces
    /// a change, so turning Patch-T on does not leave the operator without the button that prints
    /// the separator sheet the feature needs.
    /// </summary>
    public async Task LoadFeatureFlagsAsync()
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
