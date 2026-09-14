using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using FgScanner.Core;
using FgScanner.Data;

namespace FgScanner.App.Views;

public partial class ShellWindow : Window
{
    private readonly ShellViewModel _viewModel;
    private readonly AppSettingsService _settings;
    private readonly Dictionary<string, FrameworkElement> _sections;

    public ShellWindow(ShellViewModel viewModel, AppSettingsService settings)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _settings = settings;
        DataContext = viewModel;
        _sections = new Dictionary<string, FrameworkElement>
        {
            ["Scan"] = new ScanView { DataContext = viewModel.ScanViewModel },
            ["Groups"] = new GroupsView { DataContext = viewModel.GroupsViewModel },
            ["Search"] = new SearchView { DataContext = viewModel.SearchViewModel },
            ["Trash"] = new TrashView { DataContext = viewModel.TrashViewModel },
            ["Settings"] = new SettingsView { DataContext = viewModel.SettingsViewModel },
        };
        viewModel.PropertyChanged += OnViewModelPropertyChanged;
        viewModel.SearchViewModel.OpenRequested += OpenSearchHit;
        viewModel.SettingsViewModel.ShortcutsChanged += map => ApplyShortcuts(map);
        ShowSection(viewModel.SelectedSection);
        Loaded += async (_, _) =>
        {
            ApplyShortcuts(ShortcutMap.FromJson(
                await _settings.GetAsync(SettingsViewModel.ShortcutsSettingKey, "")));
            await RestoreSessionAsync();
        };
        // "Scan into this group" reuses the real Scan screen rather than a second copy of it;
        // selecting the group has already pointed ActiveGroupStore at it. The jump there and the
        // return afterwards both live in ShellViewModel, where they can be tested without a UI.
        Closing += (_, _) => SaveSession();
    }

    /// <summary>
    /// Fits the design size to the screen the window actually opened on, and centres it there.
    /// A fixed 1200x760 is taller than a 1366x768 laptop can show above its taskbar. Done here
    /// because by now WPF has placed the window — CenterScreen puts it on the monitor under the
    /// mouse — but has not shown it yet.
    /// </summary>
    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var bounds = WindowSizing.FitToWorkArea(Width, Height, MonitorWorkArea.For(this));
        Left = bounds.Left;
        Top = bounds.Top;
        Width = bounds.Width;
        Height = bounds.Height;
    }

    /// <summary>Search-result navigation: jump to the Groups section and select the hit's page.</summary>
    private void OpenSearchHit(FgScanner.Data.SearchHit hit)
    {
        _viewModel.SelectedSection = "Groups";
        var groups = _viewModel.GroupsViewModel;
        if (groups.SelectedGroup?.Id == hit.GroupId)
        {
            groups.Detail?.SelectDocument(hit.DocumentId);
            return;
        }

        groups.PendingSelectDocument = hit.DocumentId;
        groups.TrySelectGroup(hit.GroupId);
    }

    // ---- rebindable shortcuts (PLAN §5.8, NAPS2 defaults) ----

    private void ApplyShortcuts(ShortcutMap map)
    {
        InputBindings.Clear();
        foreach (var (action, gesture) in map.Bindings)
        {
            if (string.IsNullOrEmpty(gesture)
                || !ShortcutMap.TryParseGesture(gesture, out var modifierNames, out var keyName)
                || !Enum.TryParse<Key>(keyName, ignoreCase: true, out var key)
                || !ShortcutRouter.Handles(action))
            {
                continue;
            }

            var modifiers = ModifierKeys.None;
            foreach (var name in modifierNames)
            {
                modifiers |= name switch
                {
                    "Ctrl" => ModifierKeys.Control,
                    "Shift" => ModifierKeys.Shift,
                    "Alt" => ModifierKeys.Alt,
                    "Win" => ModifierKeys.Windows,
                    _ => ModifierKeys.None,
                };
            }

            // KeyBinding's Key/Modifiers setters accept bare keys that KeyGesture would reject.
            InputBindings.Add(new KeyBinding
            {
                Key = key,
                Modifiers = modifiers,
                Command = new DelegatingCommand(() => RunShortcut(action)),
            });
        }
    }

    /// <summary>
    /// Resolved when the key is pressed, not when the bindings are applied, because the section
    /// showing changes after binding. Routed by the section on screen rather than the nav selection,
    /// which Ctrl+Click can clear while Groups stays showing and would leave the page keys dead.
    /// </summary>
    private void RunShortcut(string action)
    {
        var target = ShortcutRouter.Route(action, _shownSection ?? "");
        if (target == ShortcutTarget.SelectProfile)
        {
            SelectProfile(action);
            return;
        }

        if (CommandFor(target) is { } command && command.CanExecute(null))
        {
            command.Execute(null);
        }
    }

    private void SelectProfile(string action)
    {
        var profiles = _viewModel.GroupsViewModel.Profiles;
        if (ShortcutRouter.TryProfileIndex(action, out var index) && index >= 1 && index <= profiles.Count)
        {
            _viewModel.GroupsViewModel.SelectedProfile = profiles[index - 1];
        }
    }

    /// <summary>Group targets act on whichever group is open; with none open they do nothing.</summary>
    private ICommand? CommandFor(ShortcutTarget target)
    {
        var scan = _viewModel.ScanViewModel;
        var detail = _viewModel.GroupsViewModel.Detail;
        return target switch
        {
            ShortcutTarget.Scan => scan.ScanCommand,
            ShortcutTarget.ScanAnnotated => scan.ScanAnnotatedCommand,
            ShortcutTarget.ScanNoteFace => scan.ScanNoteFaceCommand,
            ShortcutTarget.SaveToGroup => scan.SaveToGroupCommand,
            ShortcutTarget.GroupCommit => detail?.CommitCommand,
            ShortcutTarget.GroupUndo => detail?.UndoCommand,
            ShortcutTarget.GroupRedo => detail?.RedoCommand,
            ShortcutTarget.GroupRotateLeft => detail?.RotateLeftCommand,
            ShortcutTarget.GroupRotateRight => detail?.RotateRightCommand,
            ShortcutTarget.GroupDeletePage => detail?.DeleteSelectedCommand,
            ShortcutTarget.ScanDeleteStagedPages => scan.DeleteSelectedPagesCommand,
            _ => null,
        };
    }

    private sealed class DelegatingCommand(Action execute) : ICommand
    {
        public event EventHandler? CanExecuteChanged
        {
            add { }
            remove { }
        }

        public bool CanExecute(object? parameter) => true;

        public void Execute(object? parameter) => execute();
    }

    // ---- session restore (PLAN §5.8) ----

    private async Task RestoreSessionAsync()
    {
        var section = await _settings.GetAsync("Session.LastSection", "Scan");
        if (_sections.ContainsKey(section))
        {
            _viewModel.SelectedSection = section;
        }

        var storedGroup = await _settings.GetAsync("Session.LastGroupId", "");
        if (Guid.TryParse(storedGroup, out var groupId))
        {
            _viewModel.GroupsViewModel.TrySelectGroup(groupId);
        }
    }

    private void SaveSession()
    {
        try
        {
            _settings.SetAsync("Session.LastSection", _viewModel.SelectedSection).GetAwaiter().GetResult();
            _settings.SetAsync(
                "Session.LastGroupId",
                _viewModel.GroupsViewModel.SelectedGroup?.Id.ToString() ?? "").GetAwaiter().GetResult();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Serilog.Log.Error(ex, "Saving session state");
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ShellViewModel.SelectedSection))
        {
            ShowSection(_viewModel.SelectedSection);
        }
    }

    /// <summary>
    /// The size below which each section scrolls instead of squeezing. Chosen to fit the smallest
    /// supported screen (1280x1024) at 100% and 125% scaling, so a full-size window never scrolls
    /// there (SPEC-2026-001 §08). Groups is the widest: groups list 270 + gap 12 + grid 240 +
    /// splitter 6 + preview 200. Settings is a long page, so its height is left to its content (NaN).
    /// </summary>
    private static readonly Dictionary<string, (double Width, double Height)> SectionMinimums = new()
    {
        ["Scan"] = (600, 480),
        ["Groups"] = (760, 520),
        ["Search"] = (600, 400),
        ["Trash"] = (600, 400),
        ["Settings"] = (700, double.NaN),
    };

    /// <summary>
    /// Where each section was scrolled to when the user left it. The sections share one host, so
    /// without this a section opened at the previous section's offset: Groups scrolled right and down
    /// because Settings had been.
    /// </summary>
    private readonly Dictionary<string, Point> _sectionOffsets = new(StringComparer.Ordinal);

    private string? _shownSection;

    private void ShowSection(string section)
    {
        if (_sections.TryGetValue(section, out var view))
        {
            if (_shownSection is not null)
            {
                _sectionOffsets[_shownSection] = new Point(SectionHost.HorizontalOffset, SectionHost.VerticalOffset);
            }

            var minimum = SectionMinimums.GetValueOrDefault(section, (0, 0));
            SectionHost.MinContentWidth = minimum.Width;
            SectionHost.MinContentHeight = minimum.Height;
            SectionHost.Content = view;
            var offset = _sectionOffsets.GetValueOrDefault(section);
            SectionHost.ScrollToHorizontalOffset(offset.X);
            SectionHost.ScrollToVerticalOffset(offset.Y);
            _shownSection = section;
            if (section == "Trash" && view is TrashView { DataContext: TrashViewModel trash })
            {
                _ = trash.RefreshAsync();
            }

            // Rebuild the scope list on entry so groups created since startup are selectable.
            if (section == "Search" && view is SearchView { DataContext: SearchViewModel search })
            {
                _ = search.RefreshScopesAsync();
            }
        }
    }
}
