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
    private readonly PageEditingToolset _toolset;
    private readonly RetroProcessService _retroService;

    public GroupsViewModel(
        GroupService groupService,
        ProfileService profileService,
        IndexingService indexingService,
        TrashService trashService,
        ActiveGroupStore activeGroup,
        PageEditingToolset toolset,
        RetroProcessService retroService)
    {
        _groupService = groupService;
        _profileService = profileService;
        _indexingService = indexingService;
        _trashService = trashService;
        _activeGroup = activeGroup;
        _toolset = toolset;
        _retroService = retroService;
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
            Detail?.UndoRedo.Dispose();
            var detail = new GroupDetailViewModel(
                group, _groupService, _profileService, _indexingService, _trashService, _activeGroup, _toolset);
            await detail.LoadAsync();
            Detail = detail;
            if (PendingSelectDocument is { } documentId)
            {
                PendingSelectDocument = null;
                detail.SelectDocument(documentId);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Loading group {Group}", group.Name);
        }
    }

    /// <summary>Set before TrySelectGroup so the loaded detail selects this document's row.</summary>
    public Guid? PendingSelectDocument { get; set; }

    /// <summary>Session restore: reselect the group from the last run when it still exists.</summary>
    public void TrySelectGroup(Guid groupId)
    {
        if (Groups.FirstOrDefault(g => g.Id == groupId) is { } group)
        {
            SelectedGroup = group;
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

    /// <summary>Retro-processing (PLAN §5.7): register an existing folder's images and PDFs.</summary>
    [RelayCommand]
    private async Task ProcessExistingFolderAsync()
    {
        var dialog = new OpenFolderDialog { Title = "Choose the folder to process (its images and PDFs become pages)" };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            var report = await _retroService.ProcessFolderAsync(dialog.FolderName, await ResolveProfileRefAsync());
            await RefreshAsync();
            SelectedGroup = Groups.FirstOrDefault(g => g.Id == report.GroupId);

            var lines = new List<string>
            {
                $"Images registered: {report.AdoptedImages}",
                $"PDF pages rendered and registered: {report.AdoptedPdfPages}",
            };
            if (report.RematchedByChecksum.Count > 0)
            {
                lines.Add($"Renamed files re-matched by checksum: {report.RematchedByChecksum.Count}");
            }

            if (report.DuplicateFiles.Count > 0)
            {
                lines.Add($"Duplicates skipped (same content already registered): {string.Join(", ", report.DuplicateFiles.Take(8))}");
            }

            if (report.RowsWithoutFiles.Count > 0)
            {
                lines.Add($"Rows whose files are missing: {string.Join(", ", report.RowsWithoutFiles.Take(8))} — use Reconcile to fix.");
            }

            if (report.ForeignIndexFiles.Count > 0)
            {
                lines.Add($"⚠ This folder already has {string.Join(", ", report.ForeignIndexFiles)} not written by FG Scanner. " +
                    "Committing this group will replace them.");
            }

            System.Windows.MessageBox.Show(
                string.Join("\n", lines), "Process existing folder — report",
                System.Windows.MessageBoxButton.OK,
                report.ForeignIndexFiles.Count > 0
                    ? System.Windows.MessageBoxImage.Warning
                    : System.Windows.MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Retro-processing folder");
            System.Windows.MessageBox.Show(
                $"Processing failed: {ex.Message}", "Process existing folder",
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
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
