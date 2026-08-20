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

    public static async Task<bool> IsEnabledAsync(
        AppSettingsService settings, string flag, CancellationToken cancellationToken = default)
    {
        var fallback = flag == Search ? "true" : "false";
        return await settings.GetAsync(flag, fallback, cancellationToken).ConfigureAwait(false) == "true";
    }
}
