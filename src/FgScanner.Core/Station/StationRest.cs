namespace FgScanner.Core.Station;

/// <summary>
/// Is the station at rest? Pack and apply both refuse a station that is mid-anything: the app
/// open, a transaction half-written, or scans not yet saved into a group — a pack taken then is a
/// backup of a state that never existed, and an apply then swaps a database under live work.
/// </summary>
internal static class StationRest
{
    /// <param name="verb">"pack" or "apply" — the step the operator retries.</param>
    public static string? Problem(Func<bool> isAppOpen, string database, string stationRoot, string verb)
    {
        if (isAppOpen())
        {
            return $"FG Scanner is open — close it, then {verb} again";
        }

        if (File.Exists(database + "-journal")
            || (File.Exists(database + "-wal") && new FileInfo(database + "-wal").Length > 0))
        {
            return $"the database was not closed cleanly — start FG Scanner once, close it, then {verb} again";
        }

        var recovery = Path.Combine(stationRoot, "recovery");
        return Directory.Exists(recovery) && Directory.EnumerateFiles(recovery, "*", SearchOption.AllDirectories)
                .Any(f => !Path.GetFileName(f).Equals(".lock", StringComparison.OrdinalIgnoreCase))
            ? $"FG Scanner holds unsaved scans in its recovery folder — start it, save or discard them, then {verb} again"
            : null;
    }
}
