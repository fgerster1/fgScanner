namespace FgScanner.Core.Capture;

public enum DuplexPassState
{
    Inactive,

    /// <summary>Started; the fronts are going through the feeder.</summary>
    Fronts,

    /// <summary>The fronts are in. The operator is turning the stack over.</summary>
    AwaitingFlip,

    /// <summary>Both passes are in; the stack can be ordered and saved.</summary>
    Backs,
}

/// <summary>
/// What the two passes came to: either an order, or a refusal saying why. A refusal carries no
/// partial order on purpose — half an answer would be adopted as a whole one.
/// </summary>
public sealed record DuplexOrder(IReadOnlyList<string> Order, string? Refusal)
{
    public bool Refused => Refusal is not null;
}

/// <summary>
/// A stack scanned in two passes — fronts, then backs — on a scanner with no one-pass duplex.
/// Holds no database, no images and no WPF, so the ordering can be proved without a scanner, which
/// is the only way this feature could be tested at all.
///
/// <para><b>Why the order is decided here, before the pages are saved.</b> GroupService numbers
/// pages strictly in the order it receives them, so handing it the interleaved list makes the
/// scan_NNNNN filenames, the document sequence values and the index rows agree from the start.
/// Adopting fronts-then-backs and reordering afterwards renumbers rows that are already written,
/// leaves the filenames in capture order, and on groups that preserve originals leaves the
/// originals\ archive ordered differently from the pages it is supposed to mirror. The reorder
/// buttons on the Groups page stay, for repairing stacks captured before this existed.</para>
///
/// <para><b>Why a mismatch refuses instead of doing its best.</b> Two passes of different lengths
/// is the ordinary failure of a flipped stack: a double feed, a jam, a sheet left in the tray — or
/// a last sheet that genuinely has nothing on the back. The counts cannot tell those apart, and
/// with the backs reversed it is not even knowable which front lost its partner. Interleaving as
/// far as the shorter pass would pair real backs with the wrong fronts and look perfectly tidy
/// doing it. On evidence a confident wrong pairing is worse than an obvious mess: the mess gets
/// rescanned, the wrong pairing gets read out in a deposition (§05 Q1).</para>
/// </summary>
public sealed class DuplexPassSequence
{
    private readonly List<string> _fronts = [];
    private readonly List<string> _backs = [];

    public DuplexPassState State { get; private set; } = DuplexPassState.Inactive;

    public int FrontCount => _fronts.Count;

    public int BackCount => _backs.Count;

    /// <summary>
    /// Whether the second pass arrives last-sheet-first. True by default because turning the whole
    /// stack over end-for-end is the usual gesture at a feeder; flipping sheet by sheet keeps the
    /// order, and the operator says so with a checkbox. Getting this wrong reverses every pairing
    /// while leaving the page count correct, which is exactly the kind of error nobody spots.
    /// </summary>
    public bool BacksReversed { get; set; } = true;

    public bool IsActive => State != DuplexPassState.Inactive;

    public void Start()
    {
        if (IsActive)
        {
            throw new InvalidOperationException(
                "Finish or cancel the stack in hand before starting another.");
        }

        _fronts.Clear();
        _backs.Clear();
        State = DuplexPassState.Fronts;
    }

    /// <summary>Records what a pass captured: the fronts first, then the backs.</summary>
    public void RecordPass(IReadOnlyList<string> pagePaths)
    {
        switch (State)
        {
            case DuplexPassState.Fronts:
                _fronts.AddRange(pagePaths);
                State = DuplexPassState.AwaitingFlip;
                break;

            case DuplexPassState.AwaitingFlip:
                _backs.AddRange(pagePaths);
                State = DuplexPassState.Backs;
                break;

            case DuplexPassState.Backs:
                // One stack per sequence (§05 N2a). A third pass has no sheet to belong to, and
                // Batch scan already exists for repeated runs.
                throw new InvalidOperationException(
                    "Both passes are already in. Save this stack before scanning another.");

            default:
                throw new InvalidOperationException("No two-pass stack is in hand.");
        }
    }

    /// <summary>
    /// Abandons the stack and names every page both passes captured, for the caller to discard.
    /// Nothing may survive: a front whose back was never captured is not half a record on disk —
    /// it is adopted as a whole document and read as one.
    /// </summary>
    public IReadOnlyList<string> Cancel()
    {
        var discarded = new List<string>(_fronts.Count + _backs.Count);
        discarded.AddRange(_fronts);
        discarded.AddRange(_backs);
        _fronts.Clear();
        _backs.Clear();
        State = DuplexPassState.Inactive;
        return discarded;
    }

    /// <summary>The captured stack in sheet order, or a refusal. See the type doc for both whys.</summary>
    public DuplexOrder Interleave()
    {
        if (State != DuplexPassState.Backs)
        {
            throw new InvalidOperationException("Both passes must be scanned before the stack can be ordered.");
        }

        return Interleave(_fronts, _backs, BacksReversed);
    }

    /// <summary>
    /// The ordering itself, with no state attached, so it can be reasoned about on its own.
    /// </summary>
    public static DuplexOrder Interleave(
        IReadOnlyList<string> fronts, IReadOnlyList<string> backs, bool backsReversed)
    {
        if (fronts.Count != backs.Count)
        {
            return new DuplexOrder([],
                $"The two passes do not match: {fronts.Count} front(s) and {backs.Count} back(s). "
                + "Nothing has been paired. Scan the backs again, or save the pages as they are and "
                + "put them in order in Groups.");
        }

        var ordered = new List<string>(fronts.Count * 2);
        for (var i = 0; i < fronts.Count; i++)
        {
            ordered.Add(fronts[i]);
            ordered.Add(backsReversed ? backs[backs.Count - 1 - i] : backs[i]);
        }

        return new DuplexOrder(ordered, null);
    }
}
