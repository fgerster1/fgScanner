using FgScanner.Core.Evidence;
using FgScanner.Core.Index;
using Xunit;

namespace FgScanner.Core.Tests;

/// <summary>
/// The evidence field set is a stable external contract: the JimsStuff importer parses these
/// names, so a rename or a flag flip here silently breaks a legal pipeline.
/// </summary>
public class EvidenceProfileTests
{
    [Fact]
    public void NoteState_is_never_sticky()
    {
        var noteState = EvidenceProfile.Field("NoteState");

        Assert.False(noteState.Sticky);
    }

    [Fact]
    public void Field_names_are_the_importer_contract()
    {
        var names = EvidenceProfile.Fields.Select(f => f.Name);

        Assert.Equal(
            [
                "DocNo", "DocDate", "DocType", "Title", "Parties", "Operator", "Redact",
                "Box", "Notes", "NoteState", "NoteAuthor", "NoteBasis", "NoteWhen",
            ],
            names);
    }

    [Fact]
    public void DocDate_is_text_because_the_portal_permits_approximate_dates()
    {
        // The portal's `~2021` / `~YYYY-08-18` notation is real knowledge the strict Date
        // type would reject outright.
        Assert.Equal(IndexFieldType.Text, EvidenceProfile.Field("DocDate").Type);
    }

    [Fact]
    public void DocNo_and_Box_are_the_only_required_fields()
    {
        var required = EvidenceProfile.Fields.Where(f => f.Required).Select(f => f.Name);

        Assert.Equal(["DocNo", "Box"], required);
    }

    [Fact]
    public void NoteState_offers_exactly_the_three_captures()
    {
        Assert.Equal(
            ["as-found", "note-face", "clean"],
            EvidenceProfile.Field("NoteState").ListChoices);
    }

    [Fact]
    public void Authorship_fields_are_sticky_because_a_box_is_one_answer_repeated()
    {
        var sticky = EvidenceProfile.Fields.Where(f => f.Sticky).Select(f => f.Name);

        Assert.Equal(
            ["DocNo", "NoteAuthor", "NoteBasis", "NoteWhen"],
            sticky);
    }

    /// <summary>
    /// Box and Operator are constant for a whole box. They were sticky, which still made the
    /// operator type the first page and retype a correction onto every row it had reached.
    /// </summary>
    [Fact]
    public void Box_and_operator_are_the_batch_fields()
    {
        var batch = EvidenceProfile.Fields
            .Where(f => f.Scope == FieldScope.Batch)
            .Select(f => f.Name);

        Assert.Equal(["Operator", "Box"], batch);
    }

    [Fact]
    public void NoteBasis_records_how_authorship_is_known()
    {
        Assert.Equal(
            ["stated", "handwriting", "signed", "none"],
            EvidenceProfile.Field("NoteBasis").ListChoices);
    }

    [Fact]
    public void DocType_carries_the_portals_thirteen_value_vocabulary()
    {
        Assert.Equal(
            [
                "trust_document", "deposition_transcript", "financial_record", "property_record",
                "personal_letter", "court_filing", "attorney_correspondence", "billing_statement",
                "photograph_map", "handwritten_note", "word_index", "cover_page", "other",
            ],
            EvidenceProfile.Field("DocType").ListChoices);
    }

    [Fact]
    public void Redact_carries_the_two_local_rule_57_2_findings()
    {
        Assert.Equal(["identifier", "phi"], EvidenceProfile.Field("Redact").ListChoices);
    }

    [Fact]
    public void Operator_defaults_to_the_signed_in_user()
    {
        Assert.Equal("$(user)", EvidenceProfile.Field("Operator").DefaultValue);
    }

    [Fact]
    public void Only_list_fields_carry_choices()
    {
        var withChoices = EvidenceProfile.Fields
            .Where(f => f.ListChoices is { Count: > 0 })
            .Select(f => f.Type);

        Assert.All(withChoices, t => Assert.Equal(IndexFieldType.List, t));
    }
}
