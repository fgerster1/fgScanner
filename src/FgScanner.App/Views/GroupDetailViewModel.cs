using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FgScanner.App.Services;
using FgScanner.Core.Index;
using FgScanner.Data;
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

    public GroupDetailViewModel(
        Group group,
        GroupService groupService,
        ProfileService profileService,
        IndexingService indexingService,
        TrashService trashService,
        ActiveGroupStore activeGroup)
    {
        Group = group;
        _groupService = groupService;
        _profileService = profileService;
        _indexingService = indexingService;
        _trashService = trashService;
        _activeGroup = activeGroup;
    }

    public Group Group { get; }

    public ObservableCollection<DocumentRow> Rows { get; } = [];

    /// <summary>Field editors for "values for the next scan" (pre-scan entry, PLAN §5.4).</summary>
    public ObservableCollection<PendingFieldEditor> PendingFields { get; } = [];

    public IReadOnlyList<FieldDefinition> Fields { get; private set; } = [];

    [ObservableProperty]
    private DocumentRow? _selectedRow;

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
                Sequence = sequence,
                ImageName = page.FileName,
                ImagePath = Path.Combine(Group.DirectoryPath, page.FileName),
                OcrStatus = page.OcrStatus.ToString(),
                AiStatus = page.AiStatus.ToString(),
                Values = values,
            };
            values.ValueChanged += () => _ = PersistRowAsync(documentRow);
            Rows.Add(documentRow);
        }

        StatusText = $"{Rows.Count} page(s). State: {Group.State}.";
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
