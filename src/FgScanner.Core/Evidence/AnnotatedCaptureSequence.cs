namespace FgScanner.Core.Evidence;

/// <summary>
/// Drives the three-capture protocol for a sheet with notes attached: as-found, optionally
/// the note's own face, then the sheet clean. It owns the NoteState value so the operator
/// never types one — at one annotated sheet in four, a value typed is a value mistyped, and
/// a value left set is worse, because pending field values persist across scans and would
/// stamp `as-found` onto every plain sheet after it.
/// </summary>
public sealed class AnnotatedCaptureSequence
{
    public const string AsFound = "as-found";
    public const string NoteFace = "note-face";
    public const string Clean = "clean";

    private readonly List<Guid> _captures = [];
    private string? _next;

    /// <summary>Whether a sheet is in hand — captured, but not yet through its clean capture.</summary>
    public bool IsActive => _next is not null;

    /// <summary>The NoteState to stamp on the next capture; null for an ordinary sheet.</summary>
    public string? NoteStateForNextCapture => _next;

    public void Start()
    {
        if (IsActive)
        {
            throw new InvalidOperationException(
                "Finish or cancel the annotated sheet in hand before starting another.");
        }

        _captures.Clear();
        _next = AsFound;
    }

    /// <summary>Records that the pending capture was taken and adopted as <paramref name="documentId"/>.</summary>
    public void RecordCapture(Guid documentId)
    {
        if (_next is null)
        {
            throw new InvalidOperationException("No annotated sheet is in hand.");
        }

        _captures.Add(documentId);

        // Only the clean capture ends the sheet; the note's face is an extra image of a
        // sheet still owing one.
        _next = _next is AsFound or NoteFace ? Clean : null;
        if (_next is null)
        {
            _captures.Clear();
        }
    }

    /// <summary>Diverts the next capture to the lifted note itself.</summary>
    public void TakeNoteFace()
    {
        if (_next != Clean)
        {
            throw new InvalidOperationException("The notes are not lifted yet.");
        }

        _next = NoteFace;
    }

    /// <summary>
    /// Abandons the sheet and names what it captured, for the caller to discard. An as-found
    /// with no clean partner is a whole-group refusal at import — by which time the box has
    /// been re-shelved — so a half-pair must not survive the scanner.
    /// </summary>
    public IReadOnlyList<Guid> Cancel()
    {
        var discarded = _captures.ToArray();
        _captures.Clear();
        _next = null;
        return discarded;
    }
}
