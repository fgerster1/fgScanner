using CommunityToolkit.Mvvm.ComponentModel;
using FgScanner.Scanning;

namespace FgScanner.App.Views;

/// <summary>
/// One entry in the Scan page's Source list. The list is built once and never replaced — each
/// entry learns whether the chosen device supports it — because the combo's selection is bound
/// two-way, and WPF writes a nulled selection back into the view model the moment its items go
/// (CLAUDE.md).
/// </summary>
public sealed partial class SourceOption(ScanSource source, string name) : ObservableObject
{
    public ScanSource Source { get; } = source;

    /// <summary>
    /// What the operator reads. "Feeder" and "Duplex" are the driver's words, and neither says
    /// which one scans both sides of a sheet in a single pass.
    /// </summary>
    public string Name { get; } = name;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Announcement))]
    private bool _isSupported = true;

    /// <summary>Why it cannot be chosen, in the operator's terms. Empty when it can.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Announcement))]
    private string _reason = "";

    /// <summary>
    /// What a screen reader reads out, which the view binds to AutomationProperties.Name.
    /// DisplayMemberPath sets what is drawn and not what is exposed, so without this every entry
    /// announces this class's type name — and the reason a source cannot be chosen reaches nobody
    /// working by keyboard, which is the second route §09 and AC-1 ask for precisely because WPF
    /// suppresses a tooltip on a disabled control.
    /// </summary>
    public string Announcement => IsSupported ? Name : $"{Name}. {Reason}";

    public void Apply(ScanCapabilities capabilities)
    {
        IsSupported = capabilities.Supports(Source);
        Reason = IsSupported ? "" : $"This scanner does not report {Name.ToLowerInvariant()} support.";
    }
}
