using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FgScanner.Data;
using Serilog;

namespace FgScanner.App.Views;

/// <summary>One entry in the scope combo: every group, or a single named group.</summary>
public sealed record SearchScope(string Label, Guid? GroupId);

/// <summary>Full-text search over OCR text, index fields, and AI descriptions (PLAN prompt 10).</summary>
public sealed partial class SearchViewModel(SearchService searchService, GroupService groupService)
    : ObservableObject
{
    private static readonly SearchScope Everywhere = new("All groups", null);

    /// <summary>Rebuilt each time the section is shown, so new groups appear without a restart.</summary>
    public ObservableCollection<SearchScope> Scopes { get; } = [Everywhere];

    [ObservableProperty]
    private SearchScope _selectedScope = Everywhere;

    public async Task RefreshScopesAsync()
    {
        var previous = SelectedScope.GroupId;
        Scopes.Clear();
        Scopes.Add(Everywhere);
        foreach (var group in await groupService.ListGroupsAsync())
        {
            Scopes.Add(new SearchScope($"In: {group.Name}", group.Id));
        }

        SelectedScope = Scopes.FirstOrDefault(s => s.GroupId == previous) ?? Everywhere;
    }

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
            var results = await searchService.SearchAsync(Query, groupId: SelectedScope.GroupId);
            foreach (var hit in results)
            {
                Hits.Add(hit);
            }

            var scope = SelectedScope.GroupId is null ? "" : $" in \"{SelectedScope.Label[4..]}\"";
            StatusText = Hits.Count == 0
                ? $"No matches{scope}."
                : $"{Hits.Count} match(es){scope}. Double-click a result to open its group.";
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
