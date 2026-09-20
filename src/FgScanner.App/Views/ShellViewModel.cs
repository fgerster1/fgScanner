using CommunityToolkit.Mvvm.ComponentModel;
using FgScanner.Data;
using Serilog;

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

        _appSettings = appSettings;

        // Feature.Search flag (PLAN prompt 10): the section is hidden entirely when off. The list
        // is rebuilt whenever the flag moves, so the setting no longer waits for a relaunch.
        ApplySections(FeatureFlags
            .IsEnabledAsync(appSettings, FeatureFlags.Search).GetAwaiter().GetResult());

        // A capture in hand wins over a settings change; the Scan page says when it is free again.
        ScanViewModel.CaptureSettled += OnCaptureSettledAsync;

        // A settings change has to reach the section that uses it while the program is running:
        // the section view models are singletons built once at startup, so anything they read in
        // their constructor is frozen until the next launch (SPEC-2026-004). The subscription
        // lives here, beside the other cross-section wiring, so it can be exercised without a
        // window — the code-behind cannot be tested.
        SettingsViewModel.SettingsChanged += OnSettingsChangedAsync;

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

    private readonly AppSettingsService _appSettings;

    /// <summary>What a settings change asked for while paper was in hand, applied once it is not.</summary>
    private SettingsChange _deferred;

    private async Task OnSettingsChangedAsync(SettingsChange change)
    {
        if (ScanViewModel.CaptureInHand)
        {
            _deferred |= change;
            Log.Information(
                "Settings change {Change} deferred — a capture is in hand", change);
            return;
        }

        await ApplyAsync(change);
    }

    private async Task OnCaptureSettledAsync()
    {
        if (_deferred == SettingsChange.None)
        {
            return;
        }

        var change = _deferred;
        Log.Information("Applying settings change {Change} deferred while paper was in hand", change);
        if (await ApplyAsync(change))
        {
            // Only once it has actually landed. Clearing first would lose the change for good if
            // the reload threw — and the reason it threw is usually temporary.
            _deferred = SettingsChange.None;
        }
    }

    /// <summary>Returns false when the reload failed; the caller keeps the change pending.</summary>
    private async Task<bool> ApplyAsync(SettingsChange change)
    {
        try
        {
            await ApplyCoreAsync(change);
            Log.Information("Settings change {Change} applied", change);
            return true;
        }
        catch (Exception ex)
        {
            // A settings save must not take the app down, and the reload runs at the end of a
            // scan — the worst possible moment to crash, with paper just off the scanner.
            Log.Error(ex, "Applying settings change {Change}", change);
            return false;
        }
    }

    private async Task ApplyCoreAsync(SettingsChange change)
    {
        if (change.HasFlag(SettingsChange.Profiles) || change.HasFlag(SettingsChange.Schema))
        {
            // ReloadProfilesAsync re-reads the Profile entities themselves, not just their names:
            // creating a group reads BaseDirectory off the instance this list holds, and the one
            // loaded at startup came from a context disposed long ago.
            //
            // The GROUP list is deliberately not refreshed here. Reloading it replaces every Group
            // instance, which changes SelectedGroup by reference and rebuilds the whole detail pane
            // — throwing away the values the operator has typed for the next scan. A profile rename
            // showing late in the group list is cosmetic; losing typed values is not.
            await GroupsViewModel.ReloadProfilesAsync();
        }

        // The open group keeps its own pinned field layout, but it must be told that the profile
        // moved on — the "Use latest field layout" button lives inside the notice banner, so
        // without this the control that applies the change is hidden exactly when it is needed.
        if (change.HasFlag(SettingsChange.Schema) && GroupsViewModel.Detail is { } detail)
        {
            await detail.RefreshSchemaAsync();
        }

        if (change.HasFlag(SettingsChange.Flags))
        {
            await ScanViewModel.LoadFeatureFlagsAsync();
            ApplySections(await FeatureFlags.IsEnabledAsync(_appSettings, FeatureFlags.Search));
        }
    }

    private void ApplySections(bool searchEnabled)
    {
        string[] wanted = searchEnabled
            ? ["Scan", "Groups", "Search", "Trash", "Settings"]
            : ["Scan", "Groups", "Trash", "Settings"];
        if (Sections.SequenceEqual(wanted))
        {
            return;
        }

        // Add and remove the entries that actually differ, rather than clearing the list. The
        // navigation ListBox binds SelectedItem two-way, so Clear() makes WPF push null back into
        // SelectedSection before the refill — which navigates to nothing and, because the section
        // lookup is by key, throws from inside a PropertyChanged handler.
        for (var i = Sections.Count - 1; i >= 0; i--)
        {
            if (!wanted.Contains(Sections[i]))
            {
                Sections.RemoveAt(i);
            }
        }

        for (var i = 0; i < wanted.Length; i++)
        {
            if (i >= Sections.Count)
            {
                Sections.Add(wanted[i]);
            }
            else if (Sections[i] != wanted[i])
            {
                Sections.Insert(i, wanted[i]);
            }
        }

        // Hiding the section on screen would leave the shell pointing at one the list no longer
        // offers, and the content host showing nothing at all.
        if (!Sections.Contains(SelectedSection))
        {
            SelectedSection = "Groups";
        }
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

    /// <summary>
    /// Observable, because the search section can be turned on or off while the program runs and
    /// a get-only list could not tell the navigation that it had.
    /// </summary>
    public System.Collections.ObjectModel.ObservableCollection<string> Sections { get; } = [];

    public ScanViewModel ScanViewModel { get; }

    public GroupsViewModel GroupsViewModel { get; }

    public SearchViewModel SearchViewModel { get; }

    public TrashViewModel TrashViewModel { get; }

    public SettingsViewModel SettingsViewModel { get; }

    [ObservableProperty]
    private string _selectedSection = "Scan";
}
