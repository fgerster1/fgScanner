using FgScanner.App.Views;
using FgScanner.Data;
using Xunit;

namespace FgScanner.App.Tests;

/// <summary>The Settings field editor's row, which is how an operator sets a field's length and memo (AC-17).</summary>
public sealed class FieldRowTests
{
    /// <summary>
    /// Memo is offered as a fifth entry in the Type list, because that is where an operator looks
    /// for it. Underneath it is still Text plus a flag: FieldType keeps its four members, because
    /// it casts positionally to IndexFieldType and the type's NAME is written into manifest.json,
    /// which the JimsStuff importer parses (ADR-0009).
    /// </summary>
    [Theory]
    [InlineData(FieldDisplayType.Text, FieldType.Text, false)]
    [InlineData(FieldDisplayType.Memo, FieldType.Text, true)]
    [InlineData(FieldDisplayType.Date, FieldType.Date, false)]
    [InlineData(FieldDisplayType.Number, FieldType.Number, false)]
    [InlineData(FieldDisplayType.List, FieldType.List, false)]
    public void The_type_shown_maps_onto_a_stored_type_and_the_memo_flag(
        FieldDisplayType shown, FieldType stored, bool memo)
    {
        var row = new FieldRow { Name = "Notes", DisplayType = shown };

        var definition = row.ToDefinition();

        Assert.Equal(stored, definition.Type);
        Assert.Equal(memo, definition.Memo);
    }

    [Fact]
    public void A_stored_memo_field_shows_as_Memo_when_Settings_reopens()
    {
        var definition = new FieldDefinition { Name = "Notes", Type = FieldType.Text, Memo = true };

        Assert.Equal(FieldDisplayType.Memo, FieldRow.From(definition).DisplayType);
    }

    [Fact]
    public void A_plain_text_field_shows_as_Text()
    {
        var definition = new FieldDefinition { Name = "Title", Type = FieldType.Text, Memo = false };

        Assert.Equal(FieldDisplayType.Text, FieldRow.From(definition).DisplayType);
    }

    /// <summary>
    /// Switching away from Memo drops the larger limit with it, as switching to a Date does. A
    /// memo may be 2000 characters and plain text only 100, so a length carried across is one
    /// ProfileService refuses — and that refusal throws out the whole Settings save with it, not
    /// just the field.
    /// </summary>
    [Fact]
    public void Switching_from_Memo_to_Text_drops_the_length_only_a_memo_could_hold()
    {
        var row = new FieldRow { Name = "Notes", DisplayType = FieldDisplayType.Memo, Length = 2000 };

        row.DisplayType = FieldDisplayType.Text;

        Assert.Null(row.Length);
        Assert.False(row.ToDefinition().Memo);
    }

    /// <summary>A length plain text can still hold is the operator's, and stays theirs.</summary>
    [Fact]
    public void Switching_from_Memo_to_Text_keeps_a_length_text_can_hold()
    {
        var row = new FieldRow { Name = "Notes", DisplayType = FieldDisplayType.Memo, Length = 80 };

        row.DisplayType = FieldDisplayType.Text;

        Assert.Equal(80, row.Length);
    }

    /// <summary>
    /// The same rule on the way in. A definition built outside ProfileService.Sizing — an older
    /// build, an imported profile — can carry a length its type cannot use; showing it in a cell
    /// the grid has greyed out puts a number on screen the operator can neither use nor clear.
    /// </summary>
    [Fact]
    public void A_stray_length_on_a_date_does_not_load_into_the_row()
    {
        var definition = new FieldDefinition { Name = "Due", Type = FieldType.Date, MaxLength = 300 };

        Assert.Null(FieldRow.From(definition).Length);
    }

    [Fact]
    public void Length_and_memo_round_trip_through_the_field_editor()
    {
        var definition = new FieldDefinition { Name = "Notes", Type = FieldType.Text, MaxLength = 500, Memo = true };

        var restored = FieldRow.From(definition).ToDefinition();

        Assert.Equal(500, restored.MaxLength);
        Assert.True(restored.Memo);
    }

    /// <summary>Length and memo mean nothing on a date or a choice list, so the row does not keep them.</summary>
    [Fact]
    public void Choosing_a_type_other_than_text_clears_length_and_memo()
    {
        var row = new FieldRow { Name = "Due", DisplayType = FieldDisplayType.Memo, Length = 10 };

        row.DisplayType = FieldDisplayType.Date;

        Assert.Null(row.Length);
        Assert.False(row.ToDefinition().Memo);
    }

    /// <summary>
    /// Neither mapper guesses. A stored type this screen does not know is a programming error —
    /// a fifth FieldType, or a value that reached the database past its boundary guard — and
    /// showing it as a List would rewrite the stored type to List on the next save, which is the
    /// name written into manifest.json for the JimsStuff importer (ADR-0009).
    /// </summary>
    [Fact]
    public void An_unknown_stored_type_is_not_quietly_shown_as_something_else() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => FieldDisplayTypes.From((FieldType)7, memo: false));

    [Fact]
    public void An_unknown_shown_type_is_not_quietly_stored_as_something_else() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => FieldDisplayTypes.ToStored((FieldDisplayType)7));

    [Fact]
    public void A_blank_length_means_no_limit() =>
        Assert.Null(new FieldRow { Name = "Title", DisplayType = FieldDisplayType.Text }.ToDefinition().MaxLength);
}
