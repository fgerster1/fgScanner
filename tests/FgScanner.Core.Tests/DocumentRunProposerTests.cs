using FgScanner.Core.Capture;
using FgScanner.Core.Evidence;
using Xunit;

namespace FgScanner.Core.Tests;

/// <summary>
/// SPEC-2026-009 AC-1/AC-1b. DocNo decides which sheets the portal import makes one document,
/// and the import mints permanent page ids from that grouping — so the proposal is tested as
/// pure logic before any screen applies it. Jim's 883 sheets carry no DocNo at all, but his
/// Titles repeat across a document's sheets, which is the signal this reads.
/// </summary>
public class DocumentRunProposerTests
{
    private static DocumentRunRow Row(
        string? title, string? docNo = null, string? noteState = null,
        string? docType = "letter", string? docDate = "2021-10-12") =>
        new(title, docType, docDate, docNo, noteState);

    [Fact]
    public void Consecutive_sheets_with_one_title_share_a_document()
    {
        var proposed = DocumentRunProposer.Propose([Row("A"), Row("A"), Row("B")]);

        Assert.Equal(["1", "1", "2"], proposed);
    }

    [Fact]
    public void A_different_type_or_date_starts_a_new_document_even_under_one_title()
    {
        var proposed = DocumentRunProposer.Propose(
            [Row("A"), Row("A", docType: "deed"), Row("A", docType: "deed", docDate: "1996")]);

        Assert.Equal(["1", "2", "3"], proposed);
    }

    [Fact]
    public void A_blank_title_continues_the_document_only_when_type_and_date_match()
    {
        var proposed = DocumentRunProposer.Propose(
            [Row("A"), Row(null), Row("  "), Row(null, docDate: "1990-09-24")]);

        Assert.Equal(["1", "1", "1", "2"], proposed);
    }

    [Fact]
    public void A_typed_docno_is_kept_and_numbering_continues_after_the_highest()
    {
        var proposed = DocumentRunProposer.Propose(
            [Row("A", docNo: "7"), Row("B"), Row("B"), Row("C", docNo: "3"), Row("D")]);

        Assert.Equal(["7", "8", "8", "3", "9"], proposed);
    }

    /// <summary>AC-1b: the import refuses a whole group when an as-found capture has no clean
    /// sheet after it in the same DocNo run (fgscanner_group._annotation_problems).</summary>
    [Fact]
    public void Annotated_sequence_is_never_split()
    {
        var proposed = DocumentRunProposer.Propose(
        [
            Row("A"),
            Row("Sticky on page 2", noteState: AnnotatedCaptureSequence.AsFound),
            Row("Note", noteState: AnnotatedCaptureSequence.NoteFace),
            Row("A", noteState: AnnotatedCaptureSequence.Clean),
            Row("B"),
        ]);

        Assert.Equal(["1", "1", "1", "1", "2"], proposed);
    }

    [Fact]
    public void An_annotated_sequence_can_start_a_document()
    {
        var proposed = DocumentRunProposer.Propose(
        [
            Row("A"),
            Row("x", noteState: AnnotatedCaptureSequence.AsFound),
            Row("B", noteState: AnnotatedCaptureSequence.Clean),
            Row("B"),
        ]);

        Assert.Equal(["1", "2", "2", "2"], proposed);
    }

    [Fact]
    public void One_sheet_is_document_one_and_an_empty_group_proposes_nothing()
    {
        Assert.Equal(["1"], DocumentRunProposer.Propose([Row("A")]));
        Assert.Empty(DocumentRunProposer.Propose([]));
    }
}
