namespace FgScanner.Core.IndexPackages;

/// <summary>
/// An opened index package (JimsStuff SPEC-2026-005, contract-slice; the contract lives in JimsStuff
/// docs/contract/, vendored here under docs/contract-vendored/). This is the
/// portal→scanner half of the index batch loop and shares nothing with the
/// capture evidence contract. Plain models, paths passed in, no EF — the
/// Core wall stands.
/// </summary>
public sealed record IndexPackage(
    string PackageDirectory,
    string PackageId,
    int FormatVersion,
    int VocabularyVersion,
    string PackageChecksum,
    string AppVersionAtOpen,
    IReadOnlyList<SeedDocument> Documents,
    IReadOnlyList<PackagePerson> People,
    IReadOnlyList<PackageSubject> Subjects,
    IReadOnlyList<PackageDocType> DocTypes);

public sealed record SeedDocument(
    string AnchorPageId,
    long? DocumentId,
    string? Title,
    IReadOnlyList<SeedPage> Pages,
    IReadOnlyList<SeedDecision> Decisions,
    IReadOnlyList<SeedSuggestion> Suggestions);

public sealed record SeedPage(string PageId, string Image);

public sealed record SeedDecision(string Field, string? Qualifier, string Value);

public sealed record SeedSuggestion(
    long SuggestionId, string Field, string? Qualifier, string Value, string? ReasonQuote);

public sealed record PackagePerson(
    string Id,
    string DisplayName,
    string Kind,
    IReadOnlyList<string> Roles,
    IReadOnlyList<string> Aliases);

public sealed record PackageSubject(string Id, string Label, string? ParentId, bool Active);

public sealed record PackageDocType(string Id, string Label, string Family, bool Active);

/// <summary>
/// One doc-type verdict for one document — the phase-2 slice's shape, kept so
/// the golden tests compile unchanged. It maps 1:1 onto an
/// <see cref="IndexAnswer"/> with field "doc_type" and no qualifier.
/// </summary>
public sealed record DocTypeAnswer(
    string AnchorPageId, string DocTypeId, string DecidedBy, DateTimeOffset DecidedAt);

/// <summary>
/// One answer for one document — the full surface the results contract
/// carries (SPEC-2026-008 §07, the schema of record for this shape).
/// Field is one of <see cref="IndexAnswerVocabulary.Fields"/>. An empty
/// Value is a withdrawal (contract rule) and skips value validation, but
/// never qualifier validation: the portal's latest-wins decision slot is
/// (anchor, field, qualifier), so a withdrawal must name the same slot as
/// the answer it withdraws. Seq is assigned by the writer, positionally,
/// at export. DecidedAt is written as UTC seconds.
/// </summary>
public sealed record IndexAnswer(
    string AnchorPageId, string Field, string? Qualifier, string Value,
    string DecidedBy, DateTimeOffset DecidedAt);

/// <summary>
/// The answer fields and qualifier sets the portal accepts. The source of
/// truth is the portal's seeded vocabularies (JimsStuff app/vocab.py:
/// person_role_qualifiers, and the date qualifiers its pipelines emit);
/// the contract schemas deliberately leave qualifier as a free string, so
/// this list is pinned by the cross-repo round-trip test, not by a schema.
/// </summary>
public static class IndexAnswerVocabulary
{
    public const string DocType = "doc_type";
    public const string Date = "date";
    public const string Person = "person";
    public const string Subject = "subject";
    public const string KeyFlag = "key_flag";

    /// <summary>The one non-withdrawal value key_flag may carry.</summary>
    public const string KeyFlagTrue = "true";

    public static readonly IReadOnlySet<string> Fields = new HashSet<string>(
        StringComparer.Ordinal) { DocType, Date, Person, Subject, KeyFlag };

    public static readonly IReadOnlySet<string> PersonQualifiers = new HashSet<string>(
        StringComparer.Ordinal) { "from", "to", "cc", "mentioned" };

    /// <summary>"No date on the page" (ADR-0016). The answer's value is this
    /// same word: an empty value is the contract's withdrawal, so it cannot
    /// also mean "undated".</summary>
    public const string Undated = "undated";

    public static readonly IReadOnlySet<string> DateQualifiers = new HashSet<string>(
        StringComparer.Ordinal) { "exact", "about", Undated };

    /// <summary>The longest name Jim may type for a person not on the list.</summary>
    public const int TypedNameMaxLength = 120;

    /// <summary>
    /// True when every character survives the contract's byte rule. .NET's
    /// encoder and Python's json.dumps agree byte-for-byte on ordinary BMP
    /// letters and punctuation but not on controls, line separators, exotic
    /// spaces (NBSP pasted from Word, the French narrow NBSP), BOMs,
    /// surrogate pairs, or private-use/unassigned code points. Applies to
    /// the two free-text values: the decider's name and a typed person name.
    /// </summary>
    public static bool IsPlainText(string text)
    {
        foreach (var ch in text)
        {
            var category = char.GetUnicodeCategory(ch);
            if (char.IsControl(ch) || char.IsSurrogate(ch)
                || (char.IsWhiteSpace(ch) && ch != ' ')
                || category is System.Globalization.UnicodeCategory.Format
                    or System.Globalization.UnicodeCategory.PrivateUse
                    or System.Globalization.UnicodeCategory.OtherNotAssigned)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Why a typed person name cannot travel, or null when it can. It must
    /// be trimmed (two spellings of one name are two proposals), plain text,
    /// within the length limit, hold at least one letter or digit, and never
    /// be shaped like a register id — the portal reads "P0042" as an id and
    /// refuses the whole file when the register has none.
    /// </summary>
    public static string? TypedNameProblem(string name)
    {
        if (name.Length == 0 || name != name.Trim())
        {
            return "a typed name must not be empty or start or end with spaces";
        }

        if (name.Length > TypedNameMaxLength)
        {
            return $"a typed name is at most {TypedNameMaxLength} characters";
        }

        // Punctuation alone normalises to nothing on the portal, so it could
        // never be held as a proposal there.
        if (!name.Any(char.IsLetterOrDigit))
        {
            return $"\"{name}\" has no letter or digit — type the name as the page spells it";
        }

        if (System.Text.RegularExpressions.Regex.IsMatch(name, "^P[0-9]+$"))
        {
            return $"\"{name}\" looks like a person's id, and no one on the list has it — " +
                "type the name instead";
        }

        return IsPlainText(name)
            ? null
            : $"\"{name}\" contains a character that cannot travel to the portal " +
              "(a tab or a special space pasted from a document) — type it plainly";
    }
}
