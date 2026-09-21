namespace FgScanner.Scanning;

/// <summary>
/// A scan failure this app has words for. NAPS2's own exceptions are the driver's vocabulary —
/// "NoDuplexSupportException" told the operator what the library thinks, not what to do next — and
/// they belong to the scanning layer, which is the only place allowed to know about the driver
/// (CLAUDE.md: hardware access only through IScanService). Naps2ScanService translates; the Scan
/// page catches these and shows <see cref="Exception.Message"/> as written.
/// </summary>
public abstract class ScanException(string message, Exception? inner = null)
    : Exception(message, inner);

/// <summary>The scanner cannot scan from the source that was asked for.</summary>
public sealed class ScanSourceUnavailableException(ScanSource source, Exception? inner = null)
    : ScanException(MessageFor(source), inner)
{
    /// <summary>Not "Source": Exception already has one, and it means the application name.</summary>
    public ScanSource RequestedSource { get; } = source;

    private static string MessageFor(ScanSource source) => source switch
    {
        // A scanner without one-pass duplex can still capture both sides, in two passes — and this
        // app has a flow for exactly that. Saying so turns a dead end into the next step.
        ScanSource.Duplex =>
            "This scanner cannot scan both sides in one pass. Choose \"Feeder (one side)\" and "
            + "scan the fronts, then the backs.",
        ScanSource.Feeder => "This scanner has no document feeder. Choose \"Flatbed\".",
        _ => "This scanner has no flatbed. Choose one of the feeder options.",
    };
}

/// <summary>The feeder was asked for paper and had none.</summary>
public sealed class FeederEmptyException(Exception? inner = null)
    : ScanException("The feeder is empty. Load the pages and scan again.", inner);
