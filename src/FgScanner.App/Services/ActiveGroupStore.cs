using CommunityToolkit.Mvvm.ComponentModel;
using FgScanner.Data;

namespace FgScanner.App.Services;

/// <summary>The group new scans get saved into; shared between the Groups and Scan sections.</summary>
public sealed partial class ActiveGroupStore : ObservableObject
{
    [ObservableProperty]
    private Group? _current;

    /// <summary>Field values entered before scanning; applied to the next adopted documents (PLAN §5.4).</summary>
    public IReadOnlyDictionary<string, string?>? PendingValues { get; set; }

    /// <summary>Files handed over by "Open with FG Scanner"; imported into the next opened group.</summary>
    public IReadOnlyList<string>? PendingOpenFiles { get; set; }

    /// <summary>Raised after scans were saved into the current group so open views refresh.</summary>
    public event Action? GroupContentChanged;

    public void NotifyGroupContentChanged() => GroupContentChanged?.Invoke();
}
