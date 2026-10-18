using System.Globalization;
using FgScanner.Core.Station;

namespace FgScanner.App.Services;

public enum UpdateChoice
{
    BackUpFirst,
    InstallNow,
    Later,
}

/// <summary>
/// What the update offer says and does (SPEC-SCANNER-2026-011 Q3, Q4): an update is the moment a
/// backup matters most, so the offer names the last one and puts "back up first" on the default
/// button. Jim may skip it — a missing stick must never block an update — but a backup that does
/// not finish installs nothing, so he is never updated believing he is covered when he is not.
/// </summary>
public static class UpdateOffer
{
    public static string Text(string available, string current, LastBackupRecord? last) =>
        $"FG Scanner {available} is available (you have {current}).\n\n" +
        (last is null
            ? "You have never backed up this station."
            : $"Your last backup was {last.At.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)} (code {last.Code}).") +
        "\n\nYes — back up to your USB stick first, then install.\n" +
        "No — install now without a backup.\n" +
        "Cancel — not now.\n\nThe app closes briefly during the update.";

    /// <param name="backUp">Runs a backup and says whether it finished with OK.</param>
    public static bool InstallAfter(UpdateChoice choice, Func<bool> backUp) => choice switch
    {
        UpdateChoice.BackUpFirst => backUp(),
        UpdateChoice.InstallNow => true,
        _ => false,
    };
}
