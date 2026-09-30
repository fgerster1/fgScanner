using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FgScanner.Core.IndexPackages;
using Serilog;

namespace FgScanner.App.Views;

/// <summary>
/// The Index section (SPEC-2026-008): opens an index package through the
/// production <see cref="PackageReader"/> and surfaces its refusals
/// VERBATIM — the refusal texts are the contract's own words, written for
/// the operator, and the newer-version update message in particular must
/// arrive unrephrased. A refusal leaves the section usable; a later good
/// open clears it.
/// </summary>
public sealed partial class IndexViewModel : ObservableObject
{
    /// <summary>Pinned into every open for results provenance; set at
    /// startup beside IndexingService.AppVersion, overridable in tests.</summary>
    public string AppVersion { get; init; } =
        typeof(IndexViewModel).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

    [ObservableProperty]
    private IndexPackage? _package;

    [ObservableProperty]
    private string? _refusalMessage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanOpen))]
    private bool _busy;

    public bool CanOpen => !Busy;

    /// <summary>One line under the toolbar; null (hidden) until a package is open.</summary>
    [ObservableProperty]
    private string? _packageSummary;

    [RelayCommand]
    public async Task OpenPackageAsync(string packageDirectory)
    {
        Busy = true;
        try
        {
            // The reader hashes every file in the package; off the UI thread.
            var package = await Task.Run(
                () => PackageReader.Open(packageDirectory, AppVersion));
            Package = package;
            RefusalMessage = null;
            PackageSummary = string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                "{0} - {1} document(s).", package.PackageId, package.Documents.Count);
            Log.Information(
                "Index package {PackageId} opened: {Documents} document(s), app {AppVersion}",
                package.PackageId, package.Documents.Count, AppVersion);
        }
        catch (PackageRefusedException ex)
        {
            Package = null;
            PackageSummary = null;
            RefusalMessage = ex.Message;
            Log.Information("Index package refused: {Reason}", ex.Message);
        }
        finally
        {
            Busy = false;
        }
    }
}
