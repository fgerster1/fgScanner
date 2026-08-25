using System.Collections.ObjectModel;
using System.Windows;
using FgScanner.Data;

namespace FgScanner.App.Views.Dialogs;

/// <summary>One reviewable pair. Nothing is deleted unless the user ticks it.</summary>
public sealed class DuplicateRow
{
    public required DuplicateCandidate Candidate { get; init; }

    public bool Delete { get; set; }

    public string Kind => Candidate.Kind.ToString();

    public string ScoreText => Candidate.Score.ToString("P0", System.Globalization.CultureInfo.CurrentCulture);

    public string Keep => Candidate.LeftFileName;

    public string Remove => Candidate.RightFileName;
}

/// <summary>
/// Presents suspected duplicates and lets the user decide each one. Nothing is ticked by default:
/// an image match is a hint, and auto-selecting pages for deletion on a hint is how scans get lost.
/// </summary>
public partial class DuplicateReviewDialog : Window
{
    public DuplicateReviewDialog(IReadOnlyList<DuplicateCandidate> candidates, string groupName)
    {
        InitializeComponent();
        PromptText.Text = $"{candidates.Count} suspected duplicate pair(s) in \"{groupName}\".";
        Rows = [.. candidates.Select(c => new DuplicateRow { Candidate = c })];
        Grid.ItemsSource = Rows;
    }

    public ObservableCollection<DuplicateRow> Rows { get; }

    /// <summary>The pages the user chose to remove.</summary>
    public IReadOnlyList<DuplicateCandidate> ToDelete =>
        [.. Rows.Where(r => r.Delete).Select(r => r.Candidate)];

    private void OnOk(object sender, RoutedEventArgs e) => DialogResult = true;
}
