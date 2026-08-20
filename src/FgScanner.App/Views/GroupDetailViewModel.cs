using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FgScanner.App.Services;
using FgScanner.Core.Index;
using FgScanner.Data;
using FgScanner.Ocr;
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

    public event Action? SchemaLoaded;

    public async Task LoadAsync()
    {
        Fields = [];
        if (Group.ProfileId is not null)
        {
            var schema = await _profileService.GetSchemaAsync(Group.ProfileId.Value, Group.SchemaVersion);
            Fields = schema.Fields;
        }

        PendingFields.Clear();
        foreach (var field in Fields)
        {
            PendingFields.Add(new PendingFieldEditor(field));
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
        var documents = await _indexingService.BuildExportDataAsync(Group.Id);
        var byImage = documents.Rows.ToDictionary(r => r.ImageName, StringComparer.OrdinalIgnoreCase);
        var sequence = 0;
        foreach (var page in pages)
        {
            sequence++;
            var values = new RowValues(Fields);
            if (byImage.TryGetValue(page.FileName, out var row))
            {
                values.Load(row.CustomValues);
            }

            var documentRow = new DocumentRow
            {
                DocumentId = page.DocumentId,
                PageId = page.Id,
                Sequence = sequence,
                ImageName = page.FileName,
                ImagePath = Path.Combine(Group.DirectoryPath, page.FileName),
                OcrStatus = FormatOcrStatus(page),
                AiStatus = page.AiStatus.ToString(),
                Values = values,
            };
            values.ValueChanged += () => _ = PersistRowAsync(documentRow);
            Rows.Add(documentRow);
        }

        StatusText = $"{Rows.Count} page(s). State: {Group.State}.";
    }

    /// <summary>Mean word confidence below 65 flags the page for review (PLAN §5.5).</summary>
    private static string FormatOcrStatus(Page page) => page.OcrStatus switch
    {
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

    [ObservableProperty]
    private string? _value;
}
