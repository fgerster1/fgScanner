using CommunityToolkit.Mvvm.ComponentModel;
using FgScanner.Data;

namespace FgScanner.App.Views;

public sealed partial class ShellViewModel : ObservableObject
{
    public ShellViewModel(
        ScanViewModel scanViewModel,
        GroupsViewModel groupsViewModel,
        SearchViewModel searchViewModel,
        TrashViewModel trashViewModel,
        SettingsViewModel settingsViewModel,
        AppSettingsService appSettings)
    {
        ScanViewModel = scanViewModel;
        GroupsViewModel = groupsViewModel;
        SearchViewModel = searchViewModel;
        TrashViewModel = trashViewModel;
        SettingsViewModel = settingsViewModel;

        // Feature.Search flag (PLAN prompt 10): the section is hidden entirely when off.
        // Resolved once at startup — toggling it in Settings applies on next launch.
        var searchEnabled = FeatureFlags
            .IsEnabledAsync(appSettings, FeatureFlags.Search).GetAwaiter().GetResult();
        Sections = searchEnabled
            ? ["Scan", "Groups", "Search", "Trash", "Settings"]
            : ["Scan", "Groups", "Trash", "Settings"];
    }

    public IReadOnlyList<string> Sections { get; }

    public ScanViewModel ScanViewModel { get; }

    public GroupsViewModel GroupsViewModel { get; }

    public SearchViewModel SearchViewModel { get; }

    public TrashViewModel TrashViewModel { get; }

    public SettingsViewModel SettingsViewModel { get; }

    [ObservableProperty]
    private string _selectedSection = "Scan";
}
