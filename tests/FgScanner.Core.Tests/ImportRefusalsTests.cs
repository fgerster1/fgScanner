using FgScanner.Core.Capture;
using Xunit;

namespace FgScanner.Core.Tests;

/// <summary>
/// SPEC-2026-009 AC-1g. The portal import refuses a whole group over one bad sticky-note sheet
/// (JimsStuff pipeline/fgscanner_group._annotation_problems), and by then the box has been
/// re-shelved. These cases are that function's rules, worded the way it words them.
/// </summary>
public class ImportRefusalsTests
{
    private static AnnotationRow Row(string image, string? docNo, string? state = null, string? author = "Jim") =>
        new(image, docNo, state, author);

    public static TheoryData<AnnotationRow[], string[]> Cases => new()
    {
        // A pair inside one DocNo run, with a plain sheet between, is importable.
        {
            [Row("a", "1"), Row("b", "1", "as-found"), Row("c", "1"), Row("d", "1", "clean")],
            []
        },
        // The clean sheet is in the next document: the note has nothing to be a note ON.
        {
            [Row("a", "1", "as-found"), Row("b", "2", "clean")],
            ["a: as-found capture with no clean sheet after it in DocNo 1"]
        },
        // A blank DocNo makes every sheet its own document, so no pair survives it.
        {
            [Row("a", null, "note-face"), Row("b", null, "clean")],
            ["a: note-face capture with no clean sheet after it in DocNo (blank)"]
        },
        {
            [Row("a", "1", "as-found", author: " "), Row("b", "1", "clean")],
            ["a: as-found capture names no NoteAuthor"]
        },
        {
            [Row("a", "1", "asfound")],
            ["a: NoteState 'asfound' is not one of as-found, note-face, clean"]
        },
        // A clean sheet alone is only an abandoned sequence: a warning there, never a refusal.
        {
            [Row("a", "1", "clean")],
            []
        },
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void Matches_the_importers_refusals(AnnotationRow[] rows, string[] expectedStarts)
    {
        var problems = ImportRefusals.Annotations(rows);

        Assert.Equal(expectedStarts.Length, problems.Count);
        for (var i = 0; i < expectedStarts.Length; i++)
        {
            Assert.StartsWith(expectedStarts[i], problems[i], StringComparison.Ordinal);
        }
    }
}
