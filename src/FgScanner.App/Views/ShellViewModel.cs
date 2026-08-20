using CommunityToolkit.Mvvm.ComponentModel;

namespace FgScanner.App.Views;

public sealed partial class ShellViewModel(ScanViewModel scanViewModel) : ObservableObject
{
    public IReadOnlyList<string> Sections { get; } = ["Scan", "Groups", "Settings"];

    public ScanViewModel ScanViewModel { get; } = scanViewModel;

    [ObservableProperty]
    private string _selectedSection = "Scan";
}
