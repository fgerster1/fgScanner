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
    private async Task RestoreAsync()
    {
        if (SelectedItem is null)
        {
            return;
        }

        try
        {
            await _trashService.RestoreAsync(SelectedItem.Id);
            StatusText = $"Restored to \"{SelectedItem.GroupName}\".";
            await RefreshAsync();
            _activeGroup.NotifyGroupContentChanged();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Restoring trash item");
            StatusText = $"Restore failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task DeletePermanentlyAsync()
    {
        if (SelectedItem is null)
        {
            return;
        }

        try
        {
            await _trashService.DeletePermanentlyAsync(SelectedItem.Id);
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Deleting trash item");
            StatusText = $"Delete failed: {ex.Message}";
        }
    }
}
