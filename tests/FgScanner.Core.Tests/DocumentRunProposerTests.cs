using FgScanner.Core.Capture;
using FgScanner.Core.Evidence;
using Xunit;

namespace FgScanner.Core.Tests;

/// <summary>
/// SPEC-2026-009 AC-1/AC-1b/AC-1d. DocNo decides which sheets the portal import makes one
/// document, and the import mints permanent page ids from that grouping — so the proposal is
/// tested as pure logic before any screen applies it. Jim's 883 sheets carry no DocNo at all,
/// but his Titles repeat across a document's sheets, which is the signal this reads.
/// </summary>
public class DocumentRunProposerTests
{
    private const string AsFound = AnnotatedCaptureSequence.AsFound;
    private const string NoteFace = AnnotatedCaptureSequence.NoteFace;
    private const string Clean = AnnotatedCaptureSequence.Clean;

    private static DocumentRunRow Row(
        string? title, string? docNo = null, string? noteState = null,
        string? docType = "letter", string? docDate = "2021-10-12") =>
        new(title, docType, docDate, docNo, noteState);

    private static DocumentRunRow Empty(string? docNo = null) => new(null, null, null, docNo, null);

    private static IReadOnlyList<string> Numbers(params DocumentRunRow[] rows)
    {
        var proposal = DocumentRunProposer.Propose(rows);
        Assert.Empty(proposal.Malformed);
        EveryAnnotationHasItsCleanSheet(rows, proposal.DocNos);
        return proposal.DocNos;
    }

    // The importer's own pairing (fgscanner_group._subject_of): an annotation capture is a note
    // ON the next clean sheet in the same DocNo run, and a group where one has none is refused
    // whole. Every proposal these tests accept is held to that rule.
    private static void EveryAnnotationHasItsCleanSheet(
        DocumentRunRow[] rows, IReadOnlyList<string> docNos)
    {
        for (var i = 0; i < rows.Length; i++)
        {
            if (rows[i].NoteState is not (AsFound or NoteFace))
            {
                continue;
            }

            var paired = false;
            for (var j = i + 1; j < rows.Length && docNos[j] == docNos[i]; j++)
            {
                if (rows[j].NoteState == Clean)
                {
                    paired = true;
                    break;
                }
            }

            Assert.True(paired, $"row {i} ({rows[i].NoteState}) has no clean sheet after it in DocNo {docNos[i]}");
        }
    }

    [Fact]
    public void Consecutive_sheets_with_one_title_share_a_document() =>
        Assert.Equal(["1", "1", "2"], Numbers(Row("A"), Row("A"), Row("B")));

    [Fact]
    public void A_different_type_or_date_starts_a_new_document_even_under_one_title() =>
        Assert.Equal(["1", "2", "3"], Numbers(
            Row("A"), Row("A", docType: "deed"), Row("A", docType: "deed", docDate: "1996")));

    [Fact]
    public void A_blank_title_continues_the_document_only_when_type_and_date_match() =>
        Assert.Equal(["1", "1", "1", "2"], Numbers(
            Row("A"), Row(null), Row("  "), Row(null, docDate: "1990-09-24")));

    [Fact]
    public void A_typed_docno_is_kept_and_numbering_continues_after_the_highest() =>
        Assert.Equal(["7", "8", "8", "3", "9"], Numbers(
            Row("A", docNo: "7"), Row("B"), Row("B"), Row("C", docNo: "3"), Row("D")));

    /// <summary>R-D5: a number typed on one sheet of a run is the operator naming that
    /// document, so the run's blank sheets join it — before or after the typed one.</summary>
    [Fact]
    public void Blank_sheets_of_a_typed_run_take_its_number()
    {
        Assert.Equal(["7", "7", "7"], Numbers(Row("A", docNo: "7"), Row("A"), Row("A")));
        Assert.Equal(["7", "7", "7"], Numbers(Row("A"), Row("A", docNo: "7"), Row("A")));
        Assert.Equal(["1", "1", "1", "2", "2"], Numbers(
            Row("A", docNo: "1"), Row("A"), Row("A"), Row("B", docNo: "2"), Row("B")));
    }

    [Fact]
    public void Two_typed_numbers_in_one_run_stay_two_documents() =>
        Assert.Equal(["7", "7", "8", "8"], Numbers(
            Row("A"), Row("A", docNo: "7"), Row("A", docNo: "8"), Row("A")));

    /// <summary>R-D5, finding 10: a sheet nobody described — a kept duplex back — is a page of
    /// the document before it, and must not cut the next front off from it either.</summary>
    [Fact]
    public void An_undescribed_sheet_joins_the_document_before_it() =>
        Assert.Equal(["1", "1", "1", "1", "2", "2"], Numbers(
            Row("A"), Empty(), Row("A"), Empty(), Row("B"), Empty()));

    [Fact]
    public void Annotated_sequence_is_never_split() =>
        Assert.Equal(["1", "1", "1", "1", "2"], Numbers(
            Row("A"),
            Row("Sticky on page 2", noteState: AsFound),
            Row("Note", noteState: NoteFace),
            Row("A", noteState: Clean),
            Row("B")));

    [Fact]
    public void An_annotated_sequence_can_start_a_document() =>
        Assert.Equal(["1", "2", "2", "2"], Numbers(
            Row("A"), Row("x", noteState: AsFound), Row("B", noteState: Clean), Row("B")));

    /// <summary>Finding 1: a page added between an as-found and its clean partner ("Add missed
    /// page", Split) is still inside the pair's document, so the pair stays together.</summary>
    [Fact]
    public void A_plain_sheet_between_as_found_and_clean_stays_inside_the_pair() =>
        Assert.Equal(["1", "2", "2", "2", "3"], Numbers(
            Row("A"), Row("x", noteState: AsFound), Row("missed"), Row("B", noteState: Clean), Row("C")));

    /// <summary>Finding 2: the DocNo may be typed on the clean sheet, not the as-found.</summary>
    [Fact]
    public void Typed_on_clean_numbers_the_whole_pair()
    {
        Assert.Equal(["5", "5"], Numbers(Row("x", noteState: AsFound), Row("A", docNo: "5", noteState: Clean)));
        Assert.Equal(["6", "5", "5", "5"], Numbers(
            Row("A"),
            Row("x", noteState: AsFound),
            Row("n", docNo: "5", noteState: NoteFace),
            Row("B", docNo: "5", noteState: Clean)));
    }

    /// <summary>R-D1: Jim's 9/20 copy has stacks of as-found rows with no clean between them.
    /// Numbering them would glue a stack into one document or orphan a note, so nothing is
    /// proposed until NoteState is fixed — and the rows to fix are named.</summary>
    [Fact]
    public void Malformed_annotation_two_as_found_before_a_clean_is_refused()
    {
        var proposal = DocumentRunProposer.Propose(
        [
            Row("A"),
            Row("Farm sale", noteState: AsFound),
            Row("Trust Efforts", noteState: AsFound),
            Row("B", noteState: Clean),
        ]);

        Assert.Equal([1], proposal.Malformed);
        Assert.Empty(proposal.DocNos);
    }

    [Fact]
    public void Malformed_annotation_as_found_with_no_clean_after_it_is_refused()
    {
        var proposal = DocumentRunProposer.Propose(
            [Row("A"), Row("x", noteState: AsFound), Row("n", noteState: NoteFace)]);

        Assert.Equal([1, 2], proposal.Malformed);
    }

    [Fact]
    public void Malformed_annotation_two_typed_numbers_in_one_pair_is_refused()
    {
        var proposal = DocumentRunProposer.Propose(
            [Row("x", docNo: "4", noteState: AsFound), Row("A", docNo: "5", noteState: Clean)]);

        Assert.Equal([0, 1], proposal.Malformed);
    }

    [Fact]
    public void Malformed_annotation_unknown_note_state_is_refused()
    {
        var proposal = DocumentRunProposer.Propose([Row("A"), Row("A", noteState: "asfound")]);

        Assert.Equal([1], proposal.Malformed);
    }

    [Fact]
    public void One_sheet_is_document_one_and_an_empty_group_proposes_nothing()
    {
        Assert.Equal(["1"], Numbers(Row("A")));
        Assert.Empty(Numbers());
    }
}
