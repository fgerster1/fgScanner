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
    private bool _isSupported = true;

    /// <summary>Why it cannot be chosen, in the operator's terms. Empty when it can.</summary>
    [ObservableProperty]
    private string _reason = "";

    public void Apply(ScanCapabilities capabilities)
    {
        IsSupported = capabilities.Supports(Source);
        Reason = IsSupported ? "" : $"This scanner does not report {Name.ToLowerInvariant()} support.";
    }
}
