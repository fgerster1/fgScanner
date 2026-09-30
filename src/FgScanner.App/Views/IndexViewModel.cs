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

    /// <summary>Where the open package lives on disk; image paths in the
    /// seed are relative to it.</summary>
    private string? _packageDirectory;

    /// <summary>The seed's documents in the seed's own order — the portal
    /// exported them by priority, and reordering here would silently
    /// defeat the planner.</summary>
    public IReadOnlyList<SeedDocument> Documents =>
        Package?.Documents ?? [];

    [ObservableProperty]
    private SeedDocument? _selectedDocument;

    private int _pageIndex;

    partial void OnSelectedDocumentChanged(SeedDocument? value)
    {
        _pageIndex = 0;
        RaisePageChanged();
    }

    /// <summary>Absolute path of the page on screen; null with nothing open.</summary>
    public string? CurrentPageImagePath =>
        SelectedDocument is { } document && _packageDirectory is { } root
            ? System.IO.Path.Combine(
                root, document.Pages[_pageIndex].Image.Replace('/', System.IO.Path.DirectorySeparatorChar))
            : null;

    public string? PagePositionText => SelectedDocument is { } document
        ? string.Format(
            System.Globalization.CultureInfo.InvariantCulture,
            "{0} of {1}", _pageIndex + 1, document.Pages.Count)
        : null;

    [RelayCommand]
    public void NextPage()
    {
        // Clamped, never wrapped: a viewer that wraps silently is how an
        // operator reads page 1 believing it is page 3.
        if (SelectedDocument is { } document && _pageIndex < document.Pages.Count - 1)
        {
            _pageIndex++;
            RaisePageChanged();
        }
    }

    [RelayCommand]
    public void PreviousPage()
    {
        if (SelectedDocument is not null && _pageIndex > 0)
        {
            _pageIndex--;
            RaisePageChanged();
        }
    }

    private void RaisePageChanged()
    {
        OnPropertyChanged(nameof(CurrentPageImagePath));
        OnPropertyChanged(nameof(PagePositionText));
    }

    [RelayCommand]
    public async Task OpenPackageAsync(string packageDirectory)
    {
        Busy = true;
        try
        {
            // The reader hashes every file in the package; off the UI thread.
            var package = await Task.Run(
                () => PackageReader.Open(packageDirectory, AppVersion));
            _packageDirectory = packageDirectory;
            SelectedDocument = null;
            Package = package;
            OnPropertyChanged(nameof(Documents));
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
            _packageDirectory = null;
            SelectedDocument = null;
            OnPropertyChanged(nameof(Documents));
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
