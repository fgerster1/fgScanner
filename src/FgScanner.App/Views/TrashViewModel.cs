using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FgScanner.App.Services;
using FgScanner.Data;
using Serilog;

namespace FgScanner.App.Views;

public sealed partial class TrashViewModel : ObservableObject
{
    private readonly TrashService _trashService;
    private readonly ActiveGroupStore _activeGroup;

    public TrashViewModel(TrashService trashService, ActiveGroupStore activeGroup)
    {
        _trashService = trashService;
        _activeGroup = activeGroup;
        _ = RefreshAsync();
    }

    public ObservableCollection<TrashItem> Items { get; } = [];

    [ObservableProperty]
    private TrashItem? _selectedItem;

    /// <summary>Multi-selection from the grid, kept in sync by the view.</summary>
    public ObservableCollection<TrashItem> SelectedItems { get; } = [];

    /// <summary>
    /// What a command should act on: the whole selection, falling back to the focused row. The
    /// fallback matters because a keyboard-only selection can leave SelectedItems empty while a
    /// row is clearly highlighted.
    /// </summary>
    private List<TrashItem> Targets() =>
        SelectedItems.Count > 0 ? [.. SelectedItems]
        : SelectedItem is { } one ? [one]
        : [];

    [ObservableProperty]
    private string _statusText = "";

    [RelayCommand]
    public async Task RefreshAsync()
    {
        Items.Clear();
        foreach (var item in await _trashService.ListAsync())
        {
            Items.Add(item);
        }

        StatusText = Items.Count == 0 ? "Trash is empty." : $"{Items.Count} item(s) in Trash.";
    }

    [RelayCommand]
    private async Task RestoreAsync() => await RestoreAllAsync(Targets());

    /// <summary>
    /// Restores every given item, returning how many came back. One that fails does not cost the
    /// rest: abandoning a batch halfway leaves the user to work out which half moved.
    /// </summary>
    public async Task<int> RestoreAllAsync(IReadOnlyList<TrashItem> items)
    {
        if (items.Count == 0)
        {
            return 0;
        }

        var restored = 0;
        var failure = "";
        foreach (var item in items)
        {
            try
            {
                await _trashService.RestoreAsync(item.Id);
                restored++;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Restoring trash item {Id}", item.Id);
                failure = ex.Message;
            }
        }

        await RefreshAsync();
        if (restored > 0)
        {
            _activeGroup.NotifyGroupContentChanged();
        }

        StatusText = failure.Length == 0
            ? $"Restored {restored} item(s)."
            : $"Restored {restored} of {items.Count}; the rest failed: {failure}";
        return restored;
    }

    /// <summary>
    /// Confirms, then permanently deletes the selection. Confirmation is new — this used to delete
    /// a scan outright on a single click, with the Trash being the last copy of it.
    /// </summary>
    [RelayCommand]
    private async Task DeletePermanentlyAsync()
    {
        var targets = Targets();
        if (targets.Count == 0)
        {
            return;
        }

        var answer = System.Windows.MessageBox.Show(
            targets.Count == 1
                ? $"Permanently delete this page from \"{targets[0].GroupName}\"?\n\nThis cannot be undone."
                : $"Permanently delete these {targets.Count} items?\n\nThis cannot be undone.",
            "Delete permanently",
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Warning,
            System.Windows.MessageBoxResult.No);
        if (answer == System.Windows.MessageBoxResult.Yes)
        {
            await DeleteAllAsync(targets);
        }
    }

    /// <summary>Deletes every given item for good, returning how many went. Assumes consent.</summary>
    public async Task<int> DeleteAllAsync(IReadOnlyList<TrashItem> items)
    {
        if (items.Count == 0)
        {
            return 0;
        }

        var deleted = 0;
        var failure = "";
        foreach (var item in items)
        {
            try
            {
                await _trashService.DeletePermanentlyAsync(item.Id);
                deleted++;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Deleting trash item {Id}", item.Id);
                failure = ex.Message;
            }
        }

        await RefreshAsync();
        StatusText = failure.Length == 0
            ? $"Deleted {deleted} item(s) permanently."
            : $"Deleted {deleted} of {items.Count}; the rest failed: {failure}";
        return deleted;
    }
}
