using FgScanner.Core.Evidence;
using Xunit;

namespace FgScanner.Core.Tests;

/// <summary>
/// One sheet in four carries a sticky note, so the as-found → clean pair is captured
/// 800-odd times. Typing the NoteState by hand at that volume is a value mistyped; leaving
/// one set is worse, because pending field values persist across scans and would stamp
/// `as-found` onto every plain sheet after it.
/// </summary>
public class AnnotatedCaptureSequenceTests
{
    [Fact]
    public void A_plain_sheet_is_stamped_with_nothing()
    {
        var sequence = new AnnotatedCaptureSequence();

        Assert.False(sequence.IsActive);
        Assert.Null(sequence.NoteStateForNextCapture);
    }

    [Fact]
    public void The_first_capture_of_a_started_sequence_is_the_as_found_state()
    {
        var sequence = new AnnotatedCaptureSequence();

        sequence.Start();

        Assert.True(sequence.IsActive);
        Assert.Equal("as-found", sequence.NoteStateForNextCapture);
    }

    [Fact]
    public void The_capture_after_as_found_is_the_clean_sheet()
    {
        var sequence = new AnnotatedCaptureSequence();
        sequence.Start();

        sequence.RecordCapture(Guid.NewGuid());

        Assert.Equal("clean", sequence.NoteStateForNextCapture);
    }

    [Fact]
    public void The_optional_note_face_capture_does_not_satisfy_the_clean_capture()
    {
        var sequence = new AnnotatedCaptureSequence();
        sequence.Start();
        sequence.RecordCapture(Guid.NewGuid());

        sequence.TakeNoteFace();
        Assert.Equal("note-face", sequence.NoteStateForNextCapture);
        sequence.RecordCapture(Guid.NewGuid());

        Assert.Equal("clean", sequence.NoteStateForNextCapture);
        Assert.True(sequence.IsActive);
    }

    [Fact]
    public void The_clean_capture_ends_the_sequence_and_clears_the_state()
    {
        var sequence = new AnnotatedCaptureSequence();
        sequence.Start();
        sequence.RecordCapture(Guid.NewGuid());

        sequence.RecordCapture(Guid.NewGuid());

        Assert.False(sequence.IsActive);
        Assert.Null(sequence.NoteStateForNextCapture);
    }

    /// <summary>
    /// An as-found capture with no clean partner is a whole-group refusal at import, by which
    /// time the box has been re-shelved — so the abandoned pair is discarded at the scanner.
    /// </summary>
    [Fact]
    public void Cancelling_names_every_capture_the_sequence_made()
    {
        var asFound = Guid.NewGuid();
        var noteFace = Guid.NewGuid();
        var sequence = new AnnotatedCaptureSequence();
        sequence.Start();
        sequence.RecordCapture(asFound);
        sequence.TakeNoteFace();
        sequence.RecordCapture(noteFace);

        var discarded = sequence.Cancel();

        Assert.Equal([asFound, noteFace], discarded);
        Assert.False(sequence.IsActive);
        Assert.Null(sequence.NoteStateForNextCapture);
    }

    [Fact]
    public void A_completed_sequence_leaves_nothing_to_discard()
    {
        var sequence = new AnnotatedCaptureSequence();
        sequence.Start();
        sequence.RecordCapture(Guid.NewGuid());
        sequence.RecordCapture(Guid.NewGuid());

        Assert.Empty(sequence.Cancel());
    }

    /// <summary>
    /// Starting a second sheet mid-pair is how an as-found silently acquires the wrong clean
    /// partner — the operator must finish or cancel the one in hand.
    /// </summary>
    [Fact]
    public void A_sequence_cannot_be_started_while_one_is_in_hand()
    {
        var sequence = new AnnotatedCaptureSequence();
        sequence.Start();
        sequence.RecordCapture(Guid.NewGuid());

        Assert.Throws<InvalidOperationException>(sequence.Start);
    }

    [Fact]
    public void Recording_a_capture_outside_a_sequence_is_refused()
    {
        var sequence = new AnnotatedCaptureSequence();

        Assert.Throws<InvalidOperationException>(() => sequence.RecordCapture(Guid.NewGuid()));
    }

    [Fact]
    public void The_note_face_capture_is_only_offered_once_the_notes_are_lifted()
    {
        var sequence = new AnnotatedCaptureSequence();
        sequence.Start();

        // Before the as-found capture there is nothing lifted to photograph.
        Assert.Throws<InvalidOperationException>(sequence.TakeNoteFace);
    }
}
