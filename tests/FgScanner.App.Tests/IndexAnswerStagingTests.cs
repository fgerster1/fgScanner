using FgScanner.App.Views;
using Xunit;

namespace FgScanner.App.Tests;

/// <summary>
/// SPEC-2026-008 AC-3. Staging is the truth between Jim's click and the
/// export: one entry per decision slot, where a slot is the field alone
/// for single-value fields (doc_type, date, key_flag) and includes the
/// value for the multi-value fields (person also its qualifier) — the
/// SPEC-2026-003 §07 slot rule as amended 2026-09-30. Nothing stages
/// itself: an untouched suggestion is not an answer.
/// </summary>
public sealed class IndexAnswerStagingTests
{
    private const string Anchor = "TOM99001";

    private readonly AnswerStaging _staging = new();

    private IReadOnlyList<StagedAnswer> Staged() => _staging.ForDocument(Anchor);

    [Fact]
    public void Nothing_is_staged_until_someone_acts()
    {
        Assert.Empty(Staged());
    }

    [Fact]
    public void A_doc_type_stages_and_a_later_edit_replaces_it()
    {
        _staging.Stage(Anchor, "doc_type", null, "letter");
        _staging.Stage(Anchor, "doc_type", null, "card-note");

        var entry = Assert.Single(Staged());
        Assert.Equal(("doc_type", null, "card-note"),
            (entry.Field, entry.Qualifier, entry.Value));
    }

    [Fact]
    public void A_date_is_one_slot_even_when_the_qualifier_changes()
    {
        _staging.Stage(Anchor, "date", "exact", "2021-07-18");
        _staging.Stage(Anchor, "date", "about", "2021-07-01");

        var entry = Assert.Single(Staged());
        Assert.Equal(("date", "about", "2021-07-01"),
            (entry.Field, entry.Qualifier, entry.Value));
    }

    [Fact]
    public void A_malformed_date_is_refused_at_entry_never_reformatted()
    {
        Assert.Throws<ArgumentException>(
            () => _staging.Stage(Anchor, "date", "exact", "07/18/2021"));
        Assert.Throws<ArgumentException>(
            () => _staging.Stage(Anchor, "date", "exact", "2021-13-40"));
        Assert.Empty(Staged());
    }

    [Fact]
    public void People_stage_per_person_and_per_qualifier()
    {
        _staging.Stage(Anchor, "person", "mentioned", "P0001");
        _staging.Stage(Anchor, "person", "mentioned", "P0002");
        _staging.Stage(Anchor, "person", "from", "P0001");
        // The same person in the same role twice is one entry, not two.
        _staging.Stage(Anchor, "person", "mentioned", "P0001");

        Assert.Equal(3, Staged().Count);
    }

    [Fact]
    public void Subjects_stage_per_subject_and_unstage_individually()
    {
        _staging.Stage(Anchor, "subject", null, "tractor");
        _staging.Stage(Anchor, "subject", null, "lawyer-fees");
        _staging.Stage(Anchor, "subject", null, "tractor");
        Assert.Equal(2, Staged().Count);

        _staging.Unstage(Anchor, "subject", null, "tractor");
        var entry = Assert.Single(Staged());
        Assert.Equal("lawyer-fees", entry.Value);
    }

    [Fact]
    public void A_single_value_withdrawal_stages_an_empty_value()
    {
        // Withdrawing the portal's earlier doc_type decision is a real
        // answer: an empty value in the slot.
        _staging.Stage(Anchor, "doc_type", null, "");
        var entry = Assert.Single(Staged());
        Assert.Equal("", entry.Value);
    }

    [Fact]
    public void A_multi_value_withdrawal_is_refused()
    {
        // The contract's empty value cannot NAME which person or subject
        // it withdraws (§03 non-goal); phase 5's web UI owns removal.
        Assert.Throws<ArgumentException>(
            () => _staging.Stage(Anchor, "person", "mentioned", ""));
        Assert.Throws<ArgumentException>(
            () => _staging.Stage(Anchor, "subject", null, ""));
    }

    [Fact]
    public void An_unknown_field_is_refused_at_entry()
    {
        Assert.Throws<ArgumentException>(
            () => _staging.Stage(Anchor, "amount", null, "12.00"));
    }

    [Fact]
    public void Documents_stage_independently_and_changes_are_announced()
    {
        var announced = 0;
        _staging.Changed += () => announced++;

        _staging.Stage(Anchor, "subject", null, "tractor");
        _staging.Stage("TOM99005", "key_flag", null, "true");

        Assert.Single(Staged());
        Assert.Single(_staging.ForDocument("TOM99005"));
        Assert.Equal(2, announced);
        Assert.Equal(2, _staging.AnsweredDocumentCount);
    }
}
