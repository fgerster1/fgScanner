using CommunityToolkit.Mvvm.ComponentModel;
using FgScanner.Data;

namespace FgScanner.App.Services;

/// <summary>The group new scans get saved into; shared between the Groups and Scan sections.</summary>
public sealed partial class ActiveGroupStore : ObservableObject
{
    [ObservableProperty]
    private Group? _current;
}
