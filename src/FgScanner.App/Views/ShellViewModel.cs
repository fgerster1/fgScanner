using CommunityToolkit.Mvvm.ComponentModel;

namespace FgScanner.App.Views;

public sealed partial class ShellViewModel(
    ScanViewModel scanViewModel,
    GroupsViewModel groupsViewModel,
    TrashViewModel trashViewModel,
    SettingsViewModel settingsViewModel) : ObservableObject
{
    public IReadOnlyList<string> Sections { get; } = ["Scan", "Groups", "Trash", "Settings"];

    public ScanViewModel ScanViewModel { get; } = scanViewModel;

    public GroupsViewModel GroupsViewModel { get; } = groupsViewModel;

    public TrashViewModel TrashViewModel { get; } = trashViewModel;

    public SettingsViewModel SettingsViewModel { get; } = settingsViewModel;

    [ObservableProperty]
    private string _selectedSection = "Scan";
}
