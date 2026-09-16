using FgScanner.App.Views;
using FgScanner.Data;
using Xunit;

namespace FgScanner.App.Tests;

/// <summary>The Settings field editor's row, which is how an operator sets a field's length and memo (AC-17).</summary>
public sealed class FieldRowTests
{
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
        var row = new FieldRow { Name = "Due", Type = FieldType.Text, Length = 10, Memo = true };

        row.Type = FieldType.Date;

        Assert.Null(row.Length);
        Assert.False(row.Memo);
    }

    [Fact]
    public void A_blank_length_means_no_limit() =>
        Assert.Null(new FieldRow { Name = "Title", Type = FieldType.Text }.ToDefinition().MaxLength);
}
