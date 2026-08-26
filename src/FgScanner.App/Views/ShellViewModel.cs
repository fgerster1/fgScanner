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

        // "Scan into this group" is a round trip: it borrows the real Scan screen and gives the
        // user back to Groups when the pages have landed. The navigation policy lives here rather
        // than in the window's code-behind so it can be exercised without a UI.
        GroupsViewModel.ScanRequested += () =>
        {
            SelectedSection = "Scan";
            _returningToGroups = true;
            ScanViewModel.AutoSaveAfterScan = true;
        };

        // The return hangs off the SAVE, never the scan: until pages are saved they are not in the
        // group, so returning earlier would strand them on a screen the user just left.
        ScanViewModel.SavedToGroup += () =>
        {
            if (!_returningToGroups)
            {
                return;
            }

            ClearPendingReturn();
            SelectedSection = "Groups";
        };
    }

    private bool _returningToGroups;

    private void ClearPendingReturn()
    {
        _returningToGroups = false;
        ScanViewModel.AutoSaveAfterScan = false;
    }

    partial void OnSelectedSectionChanged(string value)
    {
        // Walking away from Scan abandons the round trip. Otherwise a save made much later, during
        // an ordinary visit to the Scan section, would yank the user to Groups out of nowhere.
        if (value != "Scan" && _returningToGroups)
        {
            ClearPendingReturn();
        }
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
