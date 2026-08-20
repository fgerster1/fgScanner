using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FgScanner.App.Services;
using FgScanner.Data;
using Microsoft.Win32;
using Serilog;

namespace FgScanner.App.Views;

public sealed partial class GroupsViewModel : ObservableObject
{
    private readonly GroupService _groupService;
    private readonly ActiveGroupStore _activeGroup;

    public GroupsViewModel(GroupService groupService, ActiveGroupStore activeGroup)
    {
        _groupService = groupService;
        _activeGroup = activeGroup;
        _ = RefreshAsync();
    }

    public ObservableCollection<Group> Groups { get; } = [];

    public ObservableCollection<Page> SelectedGroupPages { get; } = [];

    [ObservableProperty]
    private Group? _selectedGroup;

    [ObservableProperty]
    private string _newGroupName = "";

    [ObservableProperty]
    private string _statusText = "";

    partial void OnSelectedGroupChanged(Group? value)
    {
        _activeGroup.Current = value;
        _ = LoadPagesAsync(value);
    }

    private async Task LoadPagesAsync(Group? group)
    {
        SelectedGroupPages.Clear();
        if (group is null)
        {
            return;
        }

        try
        {
            foreach (var page in await _groupService.GetPagesAsync(group.Id))
            {
                SelectedGroupPages.Add(page);
            }

            StatusText = $"{group.Name}: {SelectedGroupPages.Count} page(s). New scans will be saved here.";
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Loading pages for group {Group}", group.Name);
            StatusText = $"Could not load pages: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        var selectedId = SelectedGroup?.Id;
        Groups.Clear();
        foreach (var group in await _groupService.ListGroupsAsync())
        {
            Groups.Add(group);
        }

        SelectedGroup = Groups.FirstOrDefault(g => g.Id == selectedId) ?? Groups.FirstOrDefault();
    }

    /// <summary>Creates a new directory (named after the sanitized group name) under a chosen parent.</summary>
    [RelayCommand]
    private async Task CreateGroupAsync()
    {
        if (string.IsNullOrWhiteSpace(NewGroupName))
        {
            StatusText = "Enter a name for the new group first.";
            return;
        }

        var dialog = new OpenFolderDialog { Title = "Choose where to create the group folder" };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            var group = await _groupService.CreateGroupAsync(dialog.FolderName, NewGroupName);
            NewGroupName = "";
            await RefreshAsync();
            SelectedGroup = Groups.First(g => g.Id == group.Id);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Creating group");
            StatusText = $"Could not create group: {ex.Message}";
        }
    }

    /// <summary>Picks an existing directory; its name becomes the group.</summary>
    [RelayCommand]
    private async Task OpenFolderAsGroupAsync()
    {
        var dialog = new OpenFolderDialog { Title = "Choose the folder to use as a group" };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            var group = await _groupService.AdoptDirectoryAsync(dialog.FolderName);
            await RefreshAsync();
            SelectedGroup = Groups.First(g => g.Id == group.Id);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Adopting folder as group");
            StatusText = $"Could not open folder as group: {ex.Message}";
        }
    }
}
