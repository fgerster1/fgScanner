using FgScanner.Core.Index;

namespace FgScanner.Core.Evidence;

/// <summary>One field of the evidence capture profile.</summary>
public sealed record EvidenceFieldSpec(
    string Name,
    IndexFieldType Type,
    bool Required,
    bool Sticky,
    string? DefaultValue = null,
    IReadOnlyList<string>? ListChoices = null);

/// <summary>
/// The evidence capture profile, as code rather than as thirteen fields typed by hand.
/// The JimsStuff importer parses these names, so the operator hand-entering them made a
/// typo a silent break in a legal pipeline.
/// </summary>
public static class EvidenceProfile
{
    /// <summary>The portal's document-type vocabulary (JimsStuff `app/vision_ocr.py`).</summary>
    private static readonly string[] DocTypes =
    [
        "trust_document", "deposition_transcript", "financial_record", "property_record",
        "personal_letter", "court_filing", "attorney_correspondence", "billing_statement",
        "photograph_map", "handwritten_note", "word_index", "cover_page", "other",
    ];

    public static IReadOnlyList<EvidenceFieldSpec> Fields { get; } =
    [
        new("DocNo", IndexFieldType.Number, Required: true, Sticky: true),

        // Text, not Date: the portal's `~2021` / `~YYYY-08-18` notation is real knowledge
        // about a page, and the Date type would reject it outright.
        new("DocDate", IndexFieldType.Text, Required: false, Sticky: false),

        new("DocType", IndexFieldType.List, Required: false, Sticky: false, ListChoices: DocTypes),
        new("Title", IndexFieldType.Text, Required: false, Sticky: false),
        new("Parties", IndexFieldType.Text, Required: false, Sticky: false),
        new("Operator", IndexFieldType.Text, Required: false, Sticky: true, DefaultValue: "$(user)"),

        // Portage County Local Rule 57.2(C) personal identifiers and 57.2(D) protected
        // health information. Absence of a value is the unreviewed state, not a finding.
        new("Redact", IndexFieldType.List, Required: false, Sticky: false,
            ListChoices: ["identifier", "phi"]),

        new("Box", IndexFieldType.Text, Required: true, Sticky: true),
        new("Notes", IndexFieldType.Text, Required: false, Sticky: false),

        // Never sticky. Pending field values persist across scans until the group changes,
        // so a NoteState left set would stamp `as-found` onto every plain sheet after it.
        new("NoteState", IndexFieldType.List, Required: false, Sticky: false,
            ListChoices:
            [
                AnnotatedCaptureSequence.AsFound,
                AnnotatedCaptureSequence.NoteFace,
                AnnotatedCaptureSequence.Clean,
            ]),

        // Sticky: a box of Jim's own notes is the same three answers several hundred times.
        // `unknown` stays a legitimate answer and nothing may pressure it toward a guess.
        new("NoteAuthor", IndexFieldType.Text, Required: false, Sticky: true),
        new("NoteBasis", IndexFieldType.List, Required: false, Sticky: true,
            ListChoices: ["stated", "handwriting", "signed", "none"]),

        // Text, not Date: `unknown` and `case-prep` are answers a date type cannot hold.
        new("NoteWhen", IndexFieldType.Text, Required: false, Sticky: true),
    ];

    public static EvidenceFieldSpec Field(string name) =>
        Fields.FirstOrDefault(f => f.Name == name)
        ?? throw new KeyNotFoundException($"No evidence field named '{name}'.");
}
