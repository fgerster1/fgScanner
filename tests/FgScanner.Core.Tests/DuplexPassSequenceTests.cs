using FgScanner.Core.Capture;
using Xunit;

namespace FgScanner.Core.Tests;

/// <summary>
/// Two-pass duplex: the fronts of a stack, then the backs. The ordering decision is pure domain
/// logic — no scanner, no database, no window — so it is tested directly rather than through a
/// capture. Pairing is a counting problem, and the counting is the whole feature: a stack whose
/// backs are one sheet out is 40 exhibits attributed to the wrong documents, and nothing later in
/// the pipeline can tell.
/// </summary>
public class DuplexPassSequenceTests
{
    private static string[] Pages(string prefix, int count) =>
        [.. Enumerable.Range(1, count).Select(i => $"{prefix}{i}.jpg")];

    private static DuplexPassSequence Captured(int fronts, int backs, bool reversed = true)
    {
        var sequence = new DuplexPassSequence { BacksReversed = reversed };
        sequence.Start();
        sequence.RecordPass(Pages("F", fronts));
        var backPages = Pages("B", backs);
        sequence.RecordPass(reversed ? [.. backPages.Reverse()] : backPages);
        return sequence;
    }

    /// <summary>AC-3, with the backs fed in the order a stack flipped end-for-end produces them.</summary>
    [Fact]
    public void Fronts_and_backs_interleave_into_sheet_order()
    {
        var result = Captured(fronts: 5, backs: 5).Interleave();

        Assert.False(result.Refused);
        Assert.Equal(
            ["F1.jpg", "B1.jpg", "F2.jpg", "B2.jpg", "F3.jpg", "B3.jpg", "F4.jpg", "B4.jpg", "F5.jpg", "B5.jpg"],
            result.Order);
    }

    /// <summary>
    /// AC-4. Turning the whole stack over hands the backs back last-sheet-first, so the last page
    /// of the second pass belongs to the first sheet. Getting this backwards reverses every
    /// pairing in the stack while leaving the page count right, which is what makes it dangerous.
    /// </summary>
    [Fact]
    public void Reversed_backs_are_paired_from_the_end()
    {
        var sequence = new DuplexPassSequence { BacksReversed = true };
        sequence.Start();
        sequence.RecordPass(["F1.jpg", "F2.jpg", "F3.jpg"]);
        sequence.RecordPass(["B3.jpg", "B2.jpg", "B1.jpg"]);

        Assert.Equal(
            ["F1.jpg", "B1.jpg", "F2.jpg", "B2.jpg", "F3.jpg", "B3.jpg"],
            sequence.Interleave().Order);
    }

    /// <summary>Flipping sheet by sheet keeps them in order, and the checkbox says so.</summary>
    [Fact]
    public void Backs_fed_in_the_same_order_are_paired_from_the_start()
    {
        var sequence = new DuplexPassSequence { BacksReversed = false };
        sequence.Start();
        sequence.RecordPass(["F1.jpg", "F2.jpg", "F3.jpg"]);
        sequence.RecordPass(["B1.jpg", "B2.jpg", "B3.jpg"]);

        Assert.Equal(
            ["F1.jpg", "B1.jpg", "F2.jpg", "B2.jpg", "F3.jpg", "B3.jpg"],
            sequence.Interleave().Order);
    }

    /// <summary>
    /// AC-5, decided by §05 Q1(a). Five fronts and four backs may be a genuinely single-sided last
    /// sheet — or a double feed, a jam, or a sheet left in the tray, and the counts cannot tell
    /// them apart. With the backs reversed it is not even knowable which front lost its partner.
    /// So nothing is paired: on evidence a confident wrong pairing is worse than an obvious mess.
    /// </summary>
    [Fact]
    public void An_odd_stack_is_refused_rather_than_guessed()
    {
        var result = Captured(fronts: 5, backs: 4).Interleave();

        Assert.True(result.Refused);
        Assert.Empty(result.Order);
    }

    [Fact]
    public void A_count_mismatch_reports_both_counts()
    {
        var result = Captured(fronts: 5, backs: 4).Interleave();

        Assert.Contains("5", result.Refusal, StringComparison.Ordinal);
        Assert.Contains("4", result.Refusal, StringComparison.Ordinal);
    }

    /// <summary>
    /// Every path from both passes comes back, from whichever state the operator gave up in. A
    /// front with no back is not half a record — it is a page that will be adopted as a whole
    /// document and read as one, so an abandoned stack must leave nothing behind.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void Cancelling_returns_every_captured_path(int passesRecorded)
    {
        var sequence = new DuplexPassSequence();
        sequence.Start();
        var expected = new List<string>();
        if (passesRecorded >= 1)
        {
            sequence.RecordPass(["F1.jpg", "F2.jpg"]);
            expected.AddRange(["F1.jpg", "F2.jpg"]);
        }

        if (passesRecorded >= 2)
        {
            sequence.RecordPass(["B2.jpg", "B1.jpg"]);
            expected.AddRange(["B2.jpg", "B1.jpg"]);
        }

        var discarded = sequence.Cancel();

        Assert.Equal(expected, discarded);
        Assert.Equal(DuplexPassState.Inactive, sequence.State);
    }

    [Fact]
    public void Starting_twice_is_refused()
    {
        var sequence = new DuplexPassSequence();
        sequence.Start();

        var ex = Assert.Throws<InvalidOperationException>(sequence.Start);
        Assert.Contains("cancel", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_pass_cannot_be_recorded_before_the_stack_is_started()
    {
        var sequence = new DuplexPassSequence();

        Assert.Throws<InvalidOperationException>(() => sequence.RecordPass(["F1.jpg"]));
    }

    /// <summary>One stack per sequence (§05 N2a): a third pass has no sheet to belong to.</summary>
    [Fact]
    public void A_third_pass_is_refused()
    {
        var sequence = Captured(fronts: 2, backs: 2);

        Assert.Throws<InvalidOperationException>(() => sequence.RecordPass(["X1.jpg"]));
    }

    [Fact]
    public void The_state_follows_the_operator_through_both_passes()
    {
        var sequence = new DuplexPassSequence();
        Assert.Equal(DuplexPassState.Inactive, sequence.State);

        sequence.Start();
        Assert.Equal(DuplexPassState.Fronts, sequence.State);

        sequence.RecordPass(["F1.jpg"]);
        Assert.Equal(DuplexPassState.AwaitingFlip, sequence.State);

        sequence.RecordPass(["B1.jpg"]);
        Assert.Equal(DuplexPassState.Backs, sequence.State);
        Assert.Equal(1, sequence.FrontCount);
        Assert.Equal(1, sequence.BackCount);
    }

    /// <summary>A stack of one sheet is still a stack; nothing about it is a special case.</summary>
    [Fact]
    public void One_sheet_interleaves()
    {
        var result = Captured(fronts: 1, backs: 1).Interleave();

        Assert.Equal(["F1.jpg", "B1.jpg"], result.Order);
    }

    /// <summary>
    /// Interleaving before adoption is the point (§08): AdoptPagesAsync numbers pages in the order
    /// it receives them, so the order decided here becomes the scan_NNNNN filenames, the sequence
    /// values and the index rows at once. Ordering afterwards renumbers rows that are already
    /// written and leaves the originals\ archive in capture order.
    /// </summary>
    [Fact]
    public void Interleaving_before_a_second_pass_is_refused()
    {
        var sequence = new DuplexPassSequence();
        sequence.Start();
        sequence.RecordPass(["F1.jpg"]);

        Assert.Throws<InvalidOperationException>(() => sequence.Interleave());
    }
}
