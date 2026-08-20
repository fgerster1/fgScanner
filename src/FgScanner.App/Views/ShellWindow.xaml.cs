using System.ComponentModel;
using System.Windows;

namespace FgScanner.App.Views;

public partial class ShellWindow : Window
{
    private readonly ShellViewModel _viewModel;
    private readonly Dictionary<string, FrameworkElement> _sections;

    public ShellWindow(ShellViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        _sections = new Dictionary<string, FrameworkElement>
        {
            ["Scan"] = new ScanView { DataContext = viewModel.ScanViewModel },
            ["Groups"] = new GroupsView { DataContext = viewModel.GroupsViewModel },
            ["Trash"] = new TrashView { DataContext = viewModel.TrashViewModel },
            ["Settings"] = new SettingsView { DataContext = viewModel.SettingsViewModel },
        };
        viewModel.PropertyChanged += OnViewModelPropertyChanged;
        ShowSection(viewModel.SelectedSection);
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
