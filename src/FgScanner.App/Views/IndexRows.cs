using FgScanner.Core.IndexPackages;

namespace FgScanner.App.Views;

/// <summary>
/// Plain names for the ids an index package speaks in (SPEC-2026-009 §08-1). Display only: what is
/// staged and exported is always the id, so a label can never change the answer file.
/// </summary>
public sealed class IndexLabels(IndexPackage package)
{
    private readonly Dictionary<string, string> _people =
        package.People.ToDictionary(p => p.Id, p => p.DisplayName, StringComparer.Ordinal);

    private readonly Dictionary<string, string> _subjects =
        package.Subjects.ToDictionary(s => s.Id, s => s.Label, StringComparer.Ordinal);

    private readonly Dictionary<string, string> _docTypes =
        package.DocTypes.ToDictionary(t => t.Id, t => t.Label, StringComparer.Ordinal);

    public string? Person(string id) => _people.GetValueOrDefault(id);

    public string? Subject(string id) => _subjects.GetValueOrDefault(id);

    public string? DocType(string id) => _docTypes.GetValueOrDefault(id);

    /// <summary>What a field is called on screen; unknown fields keep their own name.</summary>
    public static string FieldLabel(string field) => field switch
    {
        IndexAnswerVocabulary.DocType => "Document type",
        IndexAnswerVocabulary.Date => "Date",
        IndexAnswerVocabulary.Person => "Person",
        IndexAnswerVocabulary.Subject => "Subject",
        IndexAnswerVocabulary.KeyFlag => "Key document",
        "payee" => "Payee",
        "amount" => "Amount",
        "expense_category" => "Expense category",
        _ => field,
    };

    /// <summary>
    /// The value as Jim reads it, plus the id to show beside it (null when the value is not an id)
    /// and whether the id was found. A person id missing from the list shows as itself, because a
    /// blank or a guessed name would hide that the package and the list disagree.
    /// </summary>
    public (string Label, string? Id, bool Known) Value(string field, string value)
    {
        string? label = field switch
        {
            IndexAnswerVocabulary.Person or "payee" => Person(value),
            IndexAnswerVocabulary.Subject => Subject(value),
            IndexAnswerVocabulary.DocType => DocType(value),
            _ => value,
        };
        var isId = field is IndexAnswerVocabulary.Person or "payee"
            or IndexAnswerVocabulary.Subject or IndexAnswerVocabulary.DocType;
        return label is null ? (value, value, false) : (label, isId ? value : null, true);
    }
}

/// <summary>One AI suggestion as the Suggestions list shows it. <see cref="Source"/> is what Accept
/// stages; the rest is display.</summary>
public sealed record SuggestionRow(
    SeedSuggestion Source, string FieldLabel, string ValueLabel, string? IdText, bool CanAccept, string? InfoText)
{
    public const string NotAsked = "for information — not asked in this batch";
    public const string NotOnList = "not on the people list";

    public static SuggestionRow From(SeedSuggestion suggestion, IndexLabels labels)
    {
        var (label, id, known) = labels.Value(suggestion.Field, suggestion.Value);
        // A seed can carry fields the contract does not (PKG-0002: amount, payee, expense_category);
        // an Accept on one could only fail, so it is shown for its reason quote and nothing more.
        var asked = IndexAnswerVocabulary.Fields.Contains(suggestion.Field);
        var info = !asked ? NotAsked : known ? null : NotOnList;
        return new SuggestionRow(
            suggestion, IndexLabels.FieldLabel(suggestion.Field), label, id, asked, info);
    }
}

/// <summary>One staged answer as the panel lists it; <see cref="Source"/> is what Remove unstages.</summary>
public sealed record StagedAnswerRow(StagedAnswer Source, string FieldLabel, string ValueLabel, string? IdText)
{
    public static StagedAnswerRow From(StagedAnswer answer, IndexLabels labels)
    {
        var (label, id, _) = labels.Value(answer.Field, answer.Value);
        // A typed name or a withdrawal is not an id: it shows as itself.
        return new StagedAnswerRow(answer, IndexLabels.FieldLabel(answer.Field),
            answer.Value.Length == 0 ? "(withdrawn)" : label, id == label ? null : id);
    }
}
