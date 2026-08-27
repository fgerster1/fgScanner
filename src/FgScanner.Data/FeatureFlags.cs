namespace FgScanner.Data;

/// <summary>
/// Phase-10 differentiators ship behind individual flags (PLAN prompt 10): each feature is
/// demonstrable and individually disableable from Settings. Search defaults on (purely local);
/// the rest default off until the user opts in.
/// </summary>
public static class FeatureFlags
{
    public const string PatchT = "Feature.PatchT";
    public const string BlankPolicy = "Feature.BlankPolicy";
    public const string Search = "Feature.Search";
    public const string CommitHook = "Feature.CommitHook";

    /// <summary>
    /// Turn a misfed page upright before OCR. On by default, unlike the phase-10 flags: a page fed
    /// the wrong way round reads as confident gibberish that nothing downstream can detect, so the
    /// safe state is correcting it. Off trades that for the OSD pass, roughly 0.8s per page.
    /// </summary>
    public const string AutoOrient = "Feature.AutoOrient";

    /// <summary>
    /// Copy the untouched bytes of a page image to originals\ before its first pixel edit, so the
    /// capture survives for evidence work (Ohio Evid.R. 1003: a duplicate is admissible unless
    /// authenticity is questioned — the pristine capture answers that question). Off by default:
    /// non-evidence users would double their disk use for nothing.
    /// </summary>
    public const string PreserveOriginals = "Feature.PreserveOriginals";

    /// <summary>Flags that are on unless the user turns them off.</summary>
    private static readonly string[] DefaultOn = [Search, AutoOrient];

    public static async Task<bool> IsEnabledAsync(
        AppSettingsService settings, string flag, CancellationToken cancellationToken = default)
    {
        var fallback = DefaultOn.Contains(flag) ? "true" : "false";
        return await settings.GetAsync(flag, fallback, cancellationToken).ConfigureAwait(false) == "true";
    }
}
