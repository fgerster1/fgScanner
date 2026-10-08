using FgScanner.Core.Evidence;

namespace FgScanner.Core.Capture;

/// <summary>One sheet's sticky-note fields, in Sequence order.</summary>
public sealed record AnnotationRow(string ImageName, string? DocNo, string? NoteState, string? NoteAuthor);

/// <summary>
/// The portal import's whole-group refusals for sticky-note sheets, run on the station before
/// commit (SPEC-2026-009 AC-1g). A port of JimsStuff pipeline/fgscanner_group._annotation_problems
/// and its wording: found here the operator fixes it with the box on the desk; found at import,
/// the group has already been copied and the paper re-shelved.
/// </summary>
public static class ImportRefusals
{
    private static readonly string[] NoteStates =
        [AnnotatedCaptureSequence.AsFound, AnnotatedCaptureSequence.NoteFace, AnnotatedCaptureSequence.Clean];

    public static IReadOnlyList<string> Annotations(IReadOnlyList<AnnotationRow> rows)
    {
        var problems = new List<string>();
        foreach (var row in rows)
        {
            var state = Field(row.NoteState);
            if (state.Length > 0 && !NoteStates.Contains(state))
            {
                problems.Add($"{row.ImageName}: NoteState '{state}' is not one of {string.Join(", ", NoteStates)} "
                    + "— an unrecognised capture would be imported as an ordinary page of the document");
                continue;
            }

            if (IsAnnotation(state) && Field(row.NoteAuthor).Length == 0)
            {
                problems.Add($"{row.ImageName}: {state} capture names no NoteAuthor — who wrote a note is a "
                    + "determination a person makes, and `unknown` is a legitimate answer that has to be given, "
                    + "not assumed");
            }
        }

        for (var i = 0; i < rows.Count; i++)
        {
            var state = Field(rows[i].NoteState);
            if (IsAnnotation(state) && !HasCleanSheetAfter(rows, i))
            {
                var docNo = Field(rows[i].DocNo);
                problems.Add($"{rows[i].ImageName}: {state} capture with no clean sheet after it in DocNo "
                    + $"{(docNo.Length == 0 ? "(blank)" : docNo)} — the notes hide text this import would have "
                    + "no readable copy of");
            }
        }

        return problems;
    }

    // The next clean capture in the same DocNo run (fgscanner_group._subject_of). A blank DocNo
    // makes every sheet its own document, so it never has one.
    private static bool HasCleanSheetAfter(IReadOnlyList<AnnotationRow> rows, int index)
    {
        var docNo = Field(rows[index].DocNo);
        if (docNo.Length == 0)
        {
            return false;
        }

        for (var j = index + 1; j < rows.Count; j++)
        {
            if (Field(rows[j].DocNo) != docNo)
            {
                return false;
            }

            if (Field(rows[j].NoteState) == AnnotatedCaptureSequence.Clean)
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsAnnotation(string state) =>
        state is AnnotatedCaptureSequence.AsFound or AnnotatedCaptureSequence.NoteFace;

    private static string Field(string? value) => value?.Trim() ?? "";
}
