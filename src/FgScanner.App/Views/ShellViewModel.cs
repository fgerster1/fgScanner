using CommunityToolkit.Mvvm.ComponentModel;

namespace FgScanner.App.Views;

public sealed partial class ShellViewModel(ScanViewModel scanViewModel, GroupsViewModel groupsViewModel) : ObservableObject
{
    public IReadOnlyList<string> Sections { get; } = ["Scan", "Groups", "Settings"];

    public ScanViewModel ScanViewModel { get; } = scanViewModel;

    public GroupsViewModel GroupsViewModel { get; } = groupsViewModel;

    [ObservableProperty]
    private string _selectedSection = "Scan";
}
