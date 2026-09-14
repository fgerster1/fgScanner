using FgScanner.App.Views;
using Xunit;

namespace FgScanner.App.Tests;

/// <summary>
/// A field's length limit, as entry enforces it (SPEC-2026-002 §05 Q7, AC-7). WPF's own
/// TextBox.MaxLength cuts a long paste silently, and on evidence a quietly truncated title is data
/// loss nobody sees; so typing stops at the limit and an over-long paste is refused, never cut.
/// </summary>
public sealed class TextLengthGuardTests
{
    [Fact]
    public void A_typed_character_past_the_limit_is_refused()
    {
        var decision = TextLengthGuard.Decide(new string('a', 10), selectionLength: 0, "b", limit: 10, pasted: false);

        Assert.False(decision.Allowed);
    }

    [Fact]
    public void A_typed_character_within_the_limit_goes_in()
    {
        var decision = TextLengthGuard.Decide(new string('a', 9), selectionLength: 0, "b", limit: 10, pasted: false);

        Assert.True(decision.Allowed);
    }

    [Fact]
    public void An_over_long_paste_is_refused_with_both_lengths_named()
    {
        var decision = TextLengthGuard.Decide(new string('a', 50), selectionLength: 0, new string('b', 100), limit: 100, pasted: true);

        Assert.False(decision.Allowed);
        Assert.Contains("150", decision.Message, StringComparison.Ordinal);
        Assert.Contains("100", decision.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_paste_that_fits_goes_in()
    {
        var decision = TextLengthGuard.Decide(new string('a', 50), selectionLength: 0, new string('b', 50), limit: 100, pasted: true);

        Assert.True(decision.Allowed);
    }

    [Fact]
    public void Replacing_a_selection_counts_the_selected_characters_out()
    {
        var decision = TextLengthGuard.Decide(new string('a', 100), selectionLength: 20, new string('b', 20), limit: 100, pasted: true);

        Assert.True(decision.Allowed);
    }

    [Fact]
    public void With_no_limit_everything_goes_in()
    {
        var decision = TextLengthGuard.Decide("", selectionLength: 0, new string('b', 5000), limit: null, pasted: true);

        Assert.True(decision.Allowed);
    }
}
