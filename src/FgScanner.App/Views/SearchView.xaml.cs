using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using FgScanner.Data;

namespace FgScanner.App.Views;

public partial class SearchView : UserControl
{
    public SearchView()
    {
        InitializeComponent();
    }

    private void OnResultDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is SearchViewModel viewModel && viewModel.SelectedHit is not null)
        {
            viewModel.OpenSelectedCommand.Execute(null);
        }
    }
}

/// <summary>
/// Renders SearchService snippets: text between the ⟪⟫ markers becomes a bold run, so matches
/// stand out without HTML-ish string munging in the view model.
/// </summary>
public static class SnippetHighlighter
{
    public static readonly DependencyProperty SnippetProperty = DependencyProperty.RegisterAttached(
        "Snippet", typeof(string), typeof(SnippetHighlighter),
        new PropertyMetadata(null, OnSnippetChanged));

    public static void SetSnippet(TextBlock element, string? value) =>
        element.SetValue(SnippetProperty, value);

    public static string? GetSnippet(TextBlock element) =>
        (string?)element.GetValue(SnippetProperty);

    private static void OnSnippetChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextBlock textBlock)
        {
            return;
        }

        textBlock.Inlines.Clear();
        if (e.NewValue is not string snippet || snippet.Length == 0)
        {
            return;
        }

        var highlighted = false;
        foreach (var part in snippet.Split(SearchService.HighlightStart, SearchService.HighlightEnd))
        {
            if (part.Length > 0)
            {
                textBlock.Inlines.Add(highlighted
                    ? new Run(part) { FontWeight = FontWeights.Bold }
                    : new Run(part));
            }

            highlighted = !highlighted;
        }
    }
}
