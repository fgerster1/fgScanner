using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FgScanner.Data;
using Serilog;

namespace FgScanner.App.Views;

/// <summary>Full-text search over OCR text, index fields, and AI descriptions (PLAN prompt 10).</summary>
public sealed partial class SearchViewModel(SearchService searchService) : ObservableObject
{
    [ObservableProperty]
    private string _query = "";

    [ObservableProperty]
    private string _statusText = "Type a query — OCR text, field values, and AI descriptions are searched.";

    [ObservableProperty]
    private SearchHit? _selectedHit;

    public ObservableCollection<SearchHit> Hits { get; } = [];

    /// <summary>Raised when the user activates a result; the shell navigates to the group/page.</summary>
    public event Action<SearchHit>? OpenRequested;

    [RelayCommand]
    private async Task SearchAsync()
    {
        Hits.Clear();
        if (string.IsNullOrWhiteSpace(Query))
        {
            return;
        }

        try
        {
            var results = await searchService.SearchAsync(Query);
            foreach (var hit in results)
            {
                Hits.Add(hit);
            }

            StatusText = Hits.Count == 0
                ? "No matches."
                : $"{Hits.Count} match(es). Double-click a result to open its group.";
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Search for {Query}", Query);
            StatusText = $"Search failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private void OpenSelected()
    {
        if (SelectedHit is { } hit)
        {
            OpenRequested?.Invoke(hit);
        }
    }
}
