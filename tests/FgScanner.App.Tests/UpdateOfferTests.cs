using FgScanner.App.Services;
using FgScanner.Core.Station;
using Xunit;

namespace FgScanner.App.Tests;

/// <summary>
/// SPEC-SCANNER-2026-011 AC-8, Q3/Q4: the update offer says when Jim last backed up and offers a
/// backup first; he may skip it, and a backup that does not finish installs nothing.
/// </summary>
public sealed class UpdateOfferTests
{
    private static readonly DateTimeOffset Then = new(2026, 10, 15, 18, 30, 0, TimeSpan.FromHours(-4));

    [Fact]
    public void The_offer_names_the_last_backup()
    {
        var text = UpdateOffer.Text("0.8.2", "0.8.1", new LastBackupRecord(@"E:\FGScanner-Backup\jim-2026-10-15-1830-backup", "ab12-cd34", Then));

        Assert.Contains("FG Scanner 0.8.2 is available (you have 0.8.1)", text);
        Assert.Contains($"Your last backup was {Then.ToLocalTime():yyyy-MM-dd HH:mm} (code ab12-cd34)", text);
        Assert.Contains("Yes", text);
        Assert.Contains("No", text);
        Assert.Contains("Cancel", text);
    }

    [Fact]
    public void The_offer_says_when_there_has_never_been_a_backup()
    {
        Assert.Contains("You have never backed up this station", UpdateOffer.Text("0.8.2", "0.8.1", null));
    }

    [Fact]
    public void Back_up_first_installs_only_after_a_backup_that_finished()
    {
        var backups = 0;

        Assert.True(UpdateOffer.InstallAfter(UpdateChoice.BackUpFirst, () => { backups++; return true; }));
        Assert.False(UpdateOffer.InstallAfter(UpdateChoice.BackUpFirst, () => { backups++; return false; }));
        Assert.Equal(2, backups);
    }

    [Fact]
    public void Install_now_skips_the_backup_and_later_does_nothing()
    {
        var backups = 0;

        Assert.True(UpdateOffer.InstallAfter(UpdateChoice.InstallNow, () => { backups++; return true; }));
        Assert.False(UpdateOffer.InstallAfter(UpdateChoice.Later, () => { backups++; return true; }));
        Assert.Equal(0, backups);
    }
}
