using CommunityToolkit.Mvvm.ComponentModel;

namespace FgScanner.App.Views;

public sealed partial class ShellViewModel : ObservableObject
{
    public IReadOnlyList<string> Sections { get; } = ["Scan", "Groups", "Settings"];

    [ObservableProperty]
    private string _selectedSection = "Scan";
}
