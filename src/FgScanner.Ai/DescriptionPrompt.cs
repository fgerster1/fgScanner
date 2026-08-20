namespace FgScanner.Ai;

/// <summary>The per-page prompt (PLAN §5.6). The 700-char target is an aim — the hard ≤1000 limit
/// is enforced in code, because models cannot count characters.</summary>
public static class DescriptionPrompt
{
    public const string BlankPageSentinel = "BLANK PAGE";

    public const string Text =
        "Describe this scanned document page in one paragraph of at most 700 characters. " +
        "State the document type first, then any legible names or letterhead, then dates and " +
        "reference numbers, then the subject matter, then notable physical characteristics " +
        "(stamps, signatures, handwriting, damage). Do not guess at illegible text. Do not " +
        "transcribe the document. Reply with the paragraph only, no preamble. If the page is " +
        "blank or contains no meaningful content, reply with exactly: BLANK PAGE";
}
