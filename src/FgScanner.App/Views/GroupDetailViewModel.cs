using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FgScanner.App.Services;
using FgScanner.Core.Index;
using FgScanner.Data;
using FgScanner.Ocr;
using FgScanner.Scanning.Capture;
using Microsoft.Win32;
using Serilog;

namespace FgScanner.App.Views;

/// <summary>Entry grid + commit for one group: rows, pending values for the next scan, trash, missed pages.</summary>
public sealed partial class GroupDetailViewModel : ObservableObject
{
    private readonly GroupService _groupService;
    private readonly ProfileService _profileService;
    private readonly IndexingService _indexingService;
    private readonly TrashService _trashService;
    private readonly ActiveGroupStore _activeGroup;
    private readonly PageEditingToolset _toolset;

    /// <summary>Undo/redo for edits and reorders in this group (deletions excluded — they go to Trash).</summary>
    public UndoRedoService UndoRedo { get; } = new();

    public GroupDetailViewModel(
        Group group,
        GroupService groupService,
        ProfileService profileService,
        IndexingService indexingService,
        TrashService trashService,
        ActiveGroupStore activeGroup,
        PageEditingToolset toolset)
    {
        Group = group;
        _groupService = groupService;
        _profileService = profileService;
        _indexingService = indexingService;
        _trashService = trashService;
        _activeGroup = activeGroup;
        _toolset = toolset;
        UndoRedo.Changed += () =>
        {
            UndoCommand.NotifyCanExecuteChanged();
            RedoCommand.NotifyCanExecuteChanged();
        };
    }

    public Group Group { get; }

    public ObservableCollection<DocumentRow> Rows { get; } = [];

    /// <summary>Field editors for "values for the next scan" (pre-scan entry, PLAN §5.4).</summary>
    public ObservableCollection<PendingFieldEditor> PendingFields { get; } = [];

    public IReadOnlyList<FieldDefinition> Fields { get; private set; } = [];

    [ObservableProperty]
    private DocumentRow? _selectedRow;

    /// <summary>Multi-selection from the grid (kept in sync by the view) for apply-to-selected edits.</summary>
    public ObservableCollection<DocumentRow> SelectedRows { get; } = [];

    [ObservableProperty]
    private string _statusText = "";

    [ObservableProperty]
    private string _validationSummary = "";

    /// <summary>
    /// Set when this group is pinned to an older field layout than its profile's newest. Empty
    /// otherwise. A group created before its fields were defined resolves zero of them and renders
    /// an empty pane; saying nothing leaves the user to conclude the feature is broken.
    /// </summary>
    [ObservableProperty]
    private string _schemaNotice = "";

    public event Action? SchemaLoaded;

    public async Task LoadAsync()
    {
        Fields = [];
        SchemaNotice = "";
        if (Group.ProfileId is not null)
        {
            var schema = await _profileService.GetSchemaAsync(Group.ProfileId.Value, Group.SchemaVersion);
            Fields = schema.Fields;

            var latest = await _profileService.GetLatestSchemaAsync(Group.ProfileId.Value);
            if (latest.Version != Group.SchemaVersion)
            {
                SchemaNotice = Fields.Count == 0
                    ? $"This group uses field layout v{Group.SchemaVersion}, which has no fields. "
                        + $"\"{Group.Profile?.Name ?? "The profile"}\" now defines {latest.Fields.Count}."
                    : $"This group uses field layout v{Group.SchemaVersion}; "
                        + $"\"{Group.Profile?.Name ?? "the profile"}\" is on v{latest.Version}.";
            }
        }

        foreach (var stale in PendingFields)
        {
            stale.PropertyChanged -= OnPendingFieldChanged;
        }

        PendingFields.Clear();
        foreach (var field in Fields)
        {
            var editor = new PendingFieldEditor(field);
            editor.PropertyChanged += OnPendingFieldChanged;
            PendingFields.Add(editor);
        }

        await ReloadRowsAsync();
        SchemaLoaded?.Invoke();
        PushPendingValues();

        // Files from an "Open with FG Scanner" launch land in the first group the user opens.
        if (_activeGroup.PendingOpenFiles is { Count: > 0 } openFiles)
        {
            _activeGroup.PendingOpenFiles = null;
            await ImportFilePathsAsync(openFiles);
        }
    }

    public async Task ReloadRowsAsync()
    {
        Rows.Clear();
        var pages = await _groupService.GetPagesAsync(Group.Id);
        // Read stored values straight from the documents, keyed by id. Sourcing them from the
        // export projection skipped blank-flagged rows entirely (BUG-3).
        var storedValues = await _indexingService.GetStoredFieldValuesAsync(Group.Id);
        var sequence = 0;
        foreach (var page in pages)
        {
            sequence++;
            var values = new RowValues(Fields);
            if (storedValues.TryGetValue(page.DocumentId, out var stored))
            {
                values.Load(stored);
            }

            var documentRow = new DocumentRow
            {
                DocumentId = page.DocumentId,
                PageId = page.Id,
                Sequence = sequence,
                ImageName = page.FileName,
                ImagePath = Path.Combine(Group.DirectoryPath, page.FileName),
                Folder = Group.DirectoryPath,
                OcrStatus = FormatOcrStatus(page),
                AiStatus = page.AiStatus.ToString(),
                OcrText = page.OcrText,
                AiDescription = page.AiDescription,
                Values = values,
            };
            values.ValueChanged += () => _ = PersistRowAsync(documentRow);
            Rows.Add(documentRow);
        }

        StatusText = $"{Rows.Count} page(s). State: {Group.State}.";
    }

    /// <summary>
    /// Moves this group onto its profile's newest field layout. Offered rather than performed
    /// silently: the new layout's required fields land on rows that were filled under the old one,
    /// so the user is told how many rows that affects before agreeing.
    /// </summary>
    [RelayCommand]
    private async Task UseLatestFieldLayoutAsync()
    {
        if (Group.ProfileId is null)
        {
            return;
        }

        try
        {
            var latest = await _profileService.GetLatestSchemaAsync(Group.ProfileId.Value);
            var required = latest.Fields.Count(f => f.Required);
            var warning = required > 0 && Rows.Count > 0
                ? $"\n\n{required} of them are required, so the {Rows.Count} existing row(s) will "
                    + "need values before this group can be committed. Fill them in one step with "
                    + "\"Apply to all rows\"."
                : "";
            var answer = System.Windows.MessageBox.Show(
                $"Move this group from field layout v{Group.SchemaVersion} to v{latest.Version} "
                    + $"({latest.Fields.Count} field(s))?{warning}\n\nValues already entered are kept.",
                "Use latest field layout",
                System.Windows.MessageBoxButton.OKCancel,
                System.Windows.MessageBoxImage.Question);
            if (answer != System.Windows.MessageBoxResult.OK)
            {
                return;
            }

            await _groupService.UpgradeSchemaVersionAsync(Group.Id, latest.Version);
            Group.SchemaVersion = latest.Version;
            await LoadAsync();
            StatusText = $"Now using field layout v{latest.Version}.";
        }
        catch (InvalidOperationException ex)
        {
            StatusText = $"Could not change the field layout: {ex.Message}";
        }
    }

    /// <summary>
    /// Writes the values typed above onto every row that does not already have one. This is the
    /// way out for pages scanned before the fields existed — the pre-scan values only ever reach
    /// pages adopted afterwards.
    /// </summary>
    [RelayCommand]
    private async Task ApplyValuesToAllRowsAsync()
    {
        var values = PendingFields
            .Where(p => !string.IsNullOrWhiteSpace(p.Value))
            .ToDictionary(p => p.Field.Name, p => (string?)p.Value);
        if (values.Count == 0)
        {
            StatusText = "Type a value above first, then apply it to the rows.";
            return;
        }

        if (Rows.Count == 0)
        {
            StatusText = "This group has no pages yet — these values will apply to the next scan.";
            return;
        }

        try
        {
            var answer = System.Windows.MessageBox.Show(
                $"Apply {values.Count} value(s) to the {Rows.Count} row(s) in this group?\n\n"
                    + "Rows that already have a value keep it.",
                "Apply to all rows",
                System.Windows.MessageBoxButton.OKCancel,
                System.Windows.MessageBoxImage.Question);
            if (answer != System.Windows.MessageBoxResult.OK)
            {
                return;
            }

            var filled = await _indexingService.ApplyValuesToAllAsync(Group.Id, values, overwrite: false);
            await ReloadRowsAsync();
            StatusText = filled == 0
                ? "Every row already had a value for those fields."
                : $"Filled {filled} row(s).";
        }
        catch (InvalidOperationException ex)
        {
            StatusText = $"Could not apply those values: {ex.Message}";
        }
    }

    /// <summary>
    /// Reviews suspected duplicates in this group. Deletion goes through the Trash, so a wrong
    /// answer to an image hint stays recoverable.
    /// </summary>
    [RelayCommand]
    private async Task CheckDuplicatesAsync()
    {
        try
        {
            StatusText = "Checking for duplicates…";
            var candidates = await _toolset.Duplicates.FindAsync(
                Group.Id, ImageHasher.Compute, ImageHasher.DefaultThreshold);
            if (candidates.Count == 0)
            {
                StatusText = "No duplicates found.";
                return;
            }

            var dialog = new Dialogs.DuplicateReviewDialog(candidates, Group.Name)
            {
                Owner = System.Windows.Application.Current?.MainWindow,
            };
            if (dialog.ShowDialog() != true || dialog.ToDelete.Count == 0)
            {
                StatusText = $"{candidates.Count} suspected duplicate(s) — nothing deleted.";
                return;
            }

            foreach (var candidate in dialog.ToDelete)
            {
                await _trashService.DeleteDocumentAsync(candidate.RightDocumentId);
            }

            await ReloadRowsAsync();
            if (Group.State == GroupState.Committed)
            {
                await _indexingService.ReexportAsync(Group.Id);
            }

            StatusText = $"Moved {dialog.ToDelete.Count} duplicate page(s) to Trash.";
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Checking duplicates in {Group}", Group.Name);
            StatusText = $"Duplicate check failed: {ex.Message}";
        }
    }

    /// <summary>Opens Explorer at the selected page, since the grid now shows where files live.</summary>
    [RelayCommand]
    private void OpenContainingFolder()
    {
        if (SelectedRow is not { } row)
        {
            return;
        }

        try
        {
            // /select, highlights the file itself rather than just opening the folder.
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "explorer.exe",
                ArgumentList = { "/select,", row.ImagePath },
                UseShellExecute = false,
            });
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Opening containing folder for {Path}", row.ImagePath);
            StatusText = $"Could not open the folder: {ex.Message}";
        }
    }

    /// <summary>Selects the row for a document (used by search-result navigation).</summary>
    public void SelectDocument(Guid documentId) =>
        SelectedRow = Rows.FirstOrDefault(r => r.DocumentId == documentId) ?? SelectedRow;

    /// <summary>Mean word confidence below 65 flags the page for review (PLAN §5.5).</summary>
    private static string FormatOcrStatus(Page page) => page.OcrStatus switch
    {
        _ when page.IsBlank => "Blank — excluded",
        OcrStatus.Yes when page.OcrMeanConfidence is { } c && c < OcrPipeline.LowConfidenceThreshold =>
            string.Create(
                System.Globalization.CultureInfo.InvariantCulture, $"Yes ⚠ {c:0}% — review"),
        OcrStatus.Yes when page.OcrMeanConfidence is { } c =>
            string.Create(System.Globalization.CultureInfo.InvariantCulture, $"Yes ({c:0}%)"),
        var status => status.ToString(),
    };

    [RelayCommand]
    private async Task OcrPagesAsync()
    {
        try
        {
            var queued = await _toolset.OcrQueue.EnqueueGroupAsync(Group.Id);
            await ReloadRowsAsync();
            StatusText = queued == 0
                ? "All pages are already OCRed or queued."
                : $"{queued} page(s) queued for OCR — text lands in .md sidecars and the index.";
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Queueing OCR");
            StatusText = $"OCR queueing failed: {ex.Message}";
        }
    }

    /// <summary>The AI feature stays hidden until a key is stored (PLAN §5.6).</summary>
    public bool AiAvailable => !AiOptOutPolicy.IsOptedOut && _toolset.Credentials.HasKey;

    /// <summary>Reconcile (PLAN §5.7): re-match renames by checksum, report vanished files.</summary>
    [RelayCommand]
    private async Task ReconcileAsync()
    {
        try
        {
            var report = await _toolset.Retro.ReconcileAsync(Group.Id);
            await ReloadRowsAsync();
            if (report.RematchedByChecksum.Count > 0)
            {
                StatusText = $"Re-matched {report.RematchedByChecksum.Count} renamed file(s) by checksum.";
            }

            if (report.RowsWithoutFiles.Count == 0)
            {
                StatusText = report.RematchedByChecksum.Count > 0
                    ? StatusText
                    : "Reconcile: rows and files match.";
                return;
            }

            var answer = System.Windows.MessageBox.Show(
                $"{report.RowsWithoutFiles.Count} row(s) have no file on disk:\n" +
                string.Join("\n", report.RowsWithoutFiles.Take(10)) +
                "\n\nMove these rows to the Trash (restorable)?",
                "Reconcile",
                System.Windows.MessageBoxButton.YesNo,
                System.Windows.MessageBoxImage.Question);
            if (answer == System.Windows.MessageBoxResult.Yes)
            {
                var removed = await _toolset.Retro.RemoveRowsWithoutFilesAsync(Group.Id);
                await ReloadRowsAsync();
                if (Group.State == GroupState.Committed)
                {
                    await _indexingService.ReexportAsync(Group.Id);
                }

                StatusText = $"{removed} row(s) moved to Trash.";
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Reconcile");
            StatusText = $"Reconcile failed: {ex.Message}";
        }
    }

    /// <summary>Selective re-run (PLAN §5.7): apply a better model or fixed setting to a subset.</summary>
    [RelayCommand]
    private async Task ReprocessAsync()
    {
        var dialog = new Dialogs.ReprocessDialog { Owner = System.Windows.Application.Current.MainWindow };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            var ocrQueued = 0;
            var aiQueued = 0;
            switch (dialog.Scope)
            {
                case Dialogs.ReprocessScope.OcrOnly:
                    ocrQueued = await _toolset.OcrQueue.EnqueueGroupAsync(Group.Id);
                    break;
                case Dialogs.ReprocessScope.AiOnly when AiAvailable:
                    aiQueued = await _toolset.AiQueue.EnqueueGroupAsync(Group.Id);
                    break;
                case Dialogs.ReprocessScope.RedoEverything:
                    ocrQueued = await _toolset.OcrQueue.EnqueueGroupAsync(Group.Id, force: true);
                    if (AiAvailable)
                    {
                        aiQueued = await _toolset.AiQueue.EnqueueGroupAsync(Group.Id, force: true);
                    }

                    break;
                default:
                    ocrQueued = await _toolset.OcrQueue.EnqueueGroupAsync(Group.Id);
                    if (AiAvailable)
                    {
                        aiQueued = await _toolset.AiQueue.EnqueueGroupAsync(Group.Id);
                    }

                    break;
            }

            await ReloadRowsAsync();
            StatusText = $"Queued: {ocrQueued} OCR, {aiQueued} AI page(s)." +
                (AiAvailable ? "" : " (AI skipped — no key stored.)");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Re-process");
            StatusText = $"Re-process failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task DescribePagesAsync()
    {
        if (!AiAvailable)
        {
            StatusText = "Add your Gemini API key in Settings first.";
            return;
        }

        try
        {
            var billablePages = await _toolset.AiQueue.CountBillablePagesAsync(Group.Id);
            if (billablePages == 0)
            {
                StatusText = "All pages already have descriptions or are queued.";
                return;
            }

            var model = await _toolset.Settings.GetAsync(
                AiWorker.ModelSettingKey, Ai.GeminiDescriptionProvider.DefaultModel);
            var estimate = Ai.CostEstimator.EstimateUsd(billablePages, model);
            var estimateText = estimate.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture);
            var answer = System.Windows.MessageBox.Show(
                $"Describe {billablePages} page(s) with {model}?\n\n" +
                $"Estimated cost: ${estimateText} (billed to your own Google account).\n" +
                "Blank pages are skipped locally without an API call.",
                "AI descriptions — estimate",
                System.Windows.MessageBoxButton.OKCancel,
                System.Windows.MessageBoxImage.Information);
            if (answer != System.Windows.MessageBoxResult.OK)
            {
                return;
            }

            var queued = await _toolset.AiQueue.EnqueueGroupAsync(Group.Id);
            await ReloadRowsAsync();
            StatusText = $"{queued} page(s) queued for AI description.";
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Queueing AI descriptions");
            StatusText = $"AI queueing failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task ReOcrAllAsync()
    {
        try
        {
            var queued = await _toolset.OcrQueue.EnqueueGroupAsync(Group.Id, force: true);
            await ReloadRowsAsync();
            StatusText = $"{queued} page(s) queued for re-OCR; replaced .md files go to Trash.";
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Queueing re-OCR");
            StatusText = $"Re-OCR queueing failed: {ex.Message}";
        }
    }

    private async Task PersistRowAsync(DocumentRow row)
    {
        try
        {
            await _indexingService.SetFieldValuesAsync(row.DocumentId, row.Values.Snapshot());
            if (Group.State == GroupState.Committed)
            {
                await _indexingService.ReexportAsync(Group.Id);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Persisting row {Doc}", row.DocumentId);
            StatusText = $"Save failed: {ex.Message}";
        }
    }

    /// <summary>
    /// Republishes on every keystroke. Publishing once at load time captured nothing but nulls, so
    /// everything typed before a scan was silently dropped (BUG-2, docs/roadmap-v0.2.md).
    /// </summary>
    private void OnPendingFieldChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PendingFieldEditor.Value))
        {
            PushPendingValues();
        }
    }

    /// <summary>Values entered before scanning; applied to the next adopted documents.</summary>
    public void PushPendingValues() =>
        _activeGroup.PendingValues = PendingFields
            .Where(f => !string.IsNullOrEmpty(f.Value))
            .ToDictionary(f => f.Field.Name, f => (string?)f.Value);

    [RelayCommand]
    private async Task CommitAsync()
    {
        var validation = await _indexingService.ValidateAsync(Group.Id);
        if (validation.HasErrors)
        {
            ValidationSummary = $"{validation.ErrorCount} problem(s) block the commit:\n" + string.Join("\n",
                validation.Documents.Where(d => d.Errors.Count > 0)
                    .SelectMany(d => d.Errors.Select(e => $"  {d.ImageName}: {e}"))
                    .Take(12));
            StatusText = "Fix the highlighted fields, then commit again.";
            return;
        }

        ValidationSummary = "";
        var (_, export) = await _indexingService.CommitGroupAsync(Group.Id);
        Group.State = GroupState.Committed;
        var locked = export?.Results.Where(r => r.Outcome == ExportOutcome.Locked).ToList() ?? [];
        StatusText = locked.Count == 0
            ? $"Committed. Index files written: {string.Join(", ", export!.Results.Select(r => Path.GetFileName(r.Path)))}."
            : $"Committed. {locked[0].Message}";
    }

    [RelayCommand]
    private async Task ReexportAsync()
    {
        var export = await _indexingService.ReexportAsync(Group.Id);
        StatusText = export.AllSucceeded
            ? "Index files refreshed."
            : export.Results.First(r => r.Outcome != ExportOutcome.Success).Message ?? "Export problem.";
    }

    [RelayCommand]
    private async Task AddMissedPageAsync()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Choose the missed page image",
            Filter = "Images|*.png;*.jpg;*.jpeg;*.tif;*.tiff;*.bmp",
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var position = SelectedRow is null ? 0 : SelectedRow.Sequence + 1;
        try
        {
            await _indexingService.InsertMissedPageAsync(Group.Id, dialog.FileName, position);
            await ReloadRowsAsync();
            if (Group.State == GroupState.Committed)
            {
                await _indexingService.ReexportAsync(Group.Id);
            }

            StatusText = position == 0 ? "Page added at the end." : $"Page inserted at position {position}.";
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Adding missed page");
            StatusText = $"Could not add page: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task DeleteSelectedAsync()
    {
        if (SelectedRow is null)
        {
            return;
        }

        try
        {
            await _trashService.DeleteDocumentAsync(SelectedRow.DocumentId);
            await ReloadRowsAsync();
            if (Group.State == GroupState.Committed)
            {
                await _indexingService.ReexportAsync(Group.Id);
            }

            StatusText = "Page moved to Trash (restorable for 30 days).";
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Deleting page");
            StatusText = $"Delete failed: {ex.Message}";
        }
    }
}

public sealed partial class PendingFieldEditor(FieldDefinition field) : ObservableObject
{
    public FieldDefinition Field { get; } = field;

    public IReadOnlyList<string>? Choices { get; } = IndexingService.ParseChoices(field.ListChoicesJson);

    public bool IsList => Field.Type == FieldType.List;

    public bool IsDate => Field.Type == FieldType.Date;

    [ObservableProperty]
    private string? _value;

    /// <summary>
    /// The same value as a date, for the picker. Kept as the canonical ISO-8601 string underneath
    /// (CLAUDE.md), so what the picker writes and what a user types by hand are indistinguishable
    /// downstream — and a value applied to every row in a batch cannot be a locale-shaped surprise.
    /// </summary>
    public DateTime? DateValue
    {
        get => DateTime.TryParse(
            Value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed) ? parsed : null;
        set => Value = value?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    partial void OnValueChanged(string? value) => OnPropertyChanged(nameof(DateValue));
}
