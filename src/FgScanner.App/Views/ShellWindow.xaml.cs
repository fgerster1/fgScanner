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
            ["Trash"] = new TrashView { DataContext = viewModel.TrashViewModel },
            ["Settings"] = new SettingsView { DataContext = viewModel.SettingsViewModel },
        };
        viewModel.PropertyChanged += OnViewModelPropertyChanged;
        viewModel.SettingsViewModel.ShortcutsChanged += map => ApplyShortcuts(map);
        ShowSection(viewModel.SelectedSection);
        Loaded += async (_, _) =>
        {
            ApplyShortcuts(ShortcutMap.FromJson(
                await _settings.GetAsync(SettingsViewModel.ShortcutsSettingKey, "")));
            await RestoreSessionAsync();
        };
        Closing += (_, _) => SaveSession();
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
                || CommandFor(action) is not { } command)
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
            InputBindings.Add(new KeyBinding { Key = key, Modifiers = modifiers, Command = command });
        }
    }

    private ICommand? CommandFor(string action) => action switch
    {
        ShortcutMap.Actions.Scan => _viewModel.ScanViewModel.ScanCommand,
        ShortcutMap.Actions.SaveToGroup => _viewModel.ScanViewModel.SaveToGroupCommand,
        ShortcutMap.Actions.Commit => DetailCommand(d => d.CommitCommand),
        ShortcutMap.Actions.Undo => DetailCommand(d => d.UndoCommand),
        ShortcutMap.Actions.Redo => DetailCommand(d => d.RedoCommand),
        ShortcutMap.Actions.RotateLeft => DetailCommand(d => d.RotateLeftCommand),
        ShortcutMap.Actions.RotateRight => DetailCommand(d => d.RotateRightCommand),
        ShortcutMap.Actions.DeletePage => DetailCommand(d => d.DeleteSelectedCommand),
        var name when name.StartsWith("Profile", StringComparison.Ordinal)
            && int.TryParse(name["Profile".Length..], out var index) =>
            new DelegatingCommand(() =>
            {
                var profiles = _viewModel.GroupsViewModel.Profiles;
                if (index >= 1 && index <= profiles.Count)
                {
                    _viewModel.GroupsViewModel.SelectedProfile = profiles[index - 1];
                }
            }),
        _ => null,
    };

    /// <summary>Routes to whichever group is currently open; a no-op when none is.</summary>
    private DelegatingCommand DetailCommand(Func<GroupDetailViewModel, ICommand> pick) =>
        new DelegatingCommand(() =>
        {
            if (_viewModel.GroupsViewModel.Detail is { } detail)
            {
                var command = pick(detail);
                if (command.CanExecute(null))
                {
                    command.Execute(null);
                }
            }
        });

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

    private void ShowSection(string section)
    {
        if (_sections.TryGetValue(section, out var view))
        {
            SectionHost.Content = view;
            if (section == "Trash" && view is TrashView { DataContext: TrashViewModel trash })
            {
                _ = trash.RefreshAsync();
            }
        }
    }
}
