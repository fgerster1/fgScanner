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
    private readonly ProfileService _profileService;
    private readonly IndexingService _indexingService;
    private readonly TrashService _trashService;
    private readonly ActiveGroupStore _activeGroup;

    public GroupsViewModel(
        GroupService groupService,
        ProfileService profileService,
        IndexingService indexingService,
        TrashService trashService,
        ActiveGroupStore activeGroup)
    {
        _groupService = groupService;
        _profileService = profileService;
        _indexingService = indexingService;
        _trashService = trashService;
        _activeGroup = activeGroup;
        activeGroup.GroupContentChanged += () => _ = Detail?.ReloadRowsAsync();
        _ = InitializeAsync();
    }

    public ObservableCollection<Group> Groups { get; } = [];

    public ObservableCollection<Profile> Profiles { get; } = [];

    [ObservableProperty]
    private Profile? _selectedProfile;

    [ObservableProperty]
    private Group? _selectedGroup;

    [ObservableProperty]
    private GroupDetailViewModel? _detail;

    [ObservableProperty]
    private string _newGroupName = "";

    private async Task InitializeAsync()
    {
        await _profileService.EnsureDefaultAsync();
        await ReloadProfilesAsync();
        await RefreshAsync();
    }

    public async Task ReloadProfilesAsync()
    {
        var selectedId = SelectedProfile?.Id;
        Profiles.Clear();
        foreach (var profile in await _profileService.ListAsync())
        {
            Profiles.Add(profile);
        }

        SelectedProfile = Profiles.FirstOrDefault(p => p.Id == selectedId) ?? Profiles.FirstOrDefault();
    }

    partial void OnSelectedGroupChanged(Group? value)
    {
        _activeGroup.Current = value;
        _activeGroup.PendingValues = null;
        _ = LoadDetailAsync(value);
    }

    private async Task LoadDetailAsync(Group? group)
    {
        if (group is null)
        {
            Detail = null;
            return;
        }

        try
        {
            var detail = new GroupDetailViewModel(
                group, _groupService, _profileService, _indexingService, _trashService, _activeGroup);
            await detail.LoadAsync();
            Detail = detail;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Loading group {Group}", group.Name);
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

    private async Task<(Guid, int)?> ResolveProfileRefAsync()
    {
        if (SelectedProfile is null)
        {
            return null;
        }

        var schema = await _profileService.GetLatestSchemaAsync(SelectedProfile.Id);
        return (SelectedProfile.Id, schema.Version);
    }

    [RelayCommand]
    private async Task CreateGroupAsync()
    {
        if (string.IsNullOrWhiteSpace(NewGroupName))
        {
            return;
        }

        var dialog = new OpenFolderDialog { Title = "Choose where to create the group folder" };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            var group = await _groupService.CreateGroupAsync(
                dialog.FolderName, NewGroupName, await ResolveProfileRefAsync());
            NewGroupName = "";
            await RefreshAsync();
            SelectedGroup = Groups.First(g => g.Id == group.Id);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Creating group");
        }
    }

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
            var group = await _groupService.AdoptDirectoryAsync(dialog.FolderName, await ResolveProfileRefAsync());
            await RefreshAsync();
            SelectedGroup = Groups.First(g => g.Id == group.Id);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Adopting folder as group");
        }
    }
}
