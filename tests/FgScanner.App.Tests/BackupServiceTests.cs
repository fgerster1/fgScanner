using System.IO;
using FgScanner.App.Services;
using FgScanner.Core.Station;
using FgScanner.Data;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FgScanner.App.Tests;

/// <summary>
/// The Back up button's work (SPEC-SCANNER-2026-011 AC-4): it runs inside the open app, so it
/// checks for itself that nothing is in hand, and refuses in words Jim can read out.
/// </summary>
public sealed class BackupServiceTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("fg-backup-app").FullName;

    private StationPaths Station => StationPaths.Resolve(Path.Combine(_root, "station-jim"));

    private string Evidence => Path.Combine(_root, "Evidence-Scans");

    private string Stick => Path.Combine(_root, "stick");

    public BackupServiceTests()
    {
        Directory.CreateDirectory(Station.Root);
        using (var db = new FgScannerDbContext(DbBootstrapper.BuildOptions(Station.Database)))
        {
            db.Database.Migrate();
            db.Profiles.Add(new Profile { Name = "Evidence", BaseDirectory = Evidence });
            db.SaveChanges();
        }

        Directory.CreateDirectory(Path.Combine(Evidence, "Dental"));
        File.WriteAllText(Path.Combine(Evidence, "Dental", "scan_00001.jpg"), "jpeg one");
        Directory.CreateDirectory(Stick);
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private BackupService Service(WorkGate? gate = null) => new(Station, gate ?? new WorkGate());

    [Fact]
    public async Task A_backup_writes_a_checked_copy_and_records_it()
    {
        var gate = new WorkGate();

        var outcome = await Service(gate).BackupAsync(Stick, whyNotNow: null, Token);

        Assert.Null(outcome.Problem);
        Assert.Matches("^[0-9a-f]{4}-[0-9a-f]{4}$", outcome.Code);
        Assert.Equal(StationVerifyStatus.Ok, StationManifestVerifier.Verify(outcome.Folder!).Status);
        Assert.Equal(outcome.Code, LastBackupRecord.Read(Station.Root)!.Code);
        Assert.False(File.Exists(Path.Combine(Station.Root, StationFreezeRecord.FileName)));
        (await gate.EnterAsync(Token).WaitAsync(TimeSpan.FromSeconds(5), Token)).Dispose();
    }

    [Fact]
    public async Task A_backup_refuses_while_paper_is_in_hand()
    {
        var outcome = await Service().BackupAsync(Stick, "a sheet is being scanned — finish it first", Token);

        Assert.Equal("a sheet is being scanned — finish it first", outcome.Problem);
        Assert.Empty(Directory.GetDirectories(Stick));
    }

    [Fact]
    public async Task A_backup_refuses_a_folder_that_is_not_there()
    {
        var outcome = await Service().BackupAsync(Path.Combine(_root, "unplugged"), whyNotNow: null, Token);

        Assert.Contains("does not exist", outcome.Problem);
    }

    [Fact]
    public async Task A_backup_refuses_while_recovery_holds_unsaved_scans()
    {
        Directory.CreateDirectory(Path.Combine(Station.Recovery, "session-old"));
        File.WriteAllText(Path.Combine(Station.Recovery, "session-old", "page-00001.png"), "unsaved");

        var outcome = await Service().BackupAsync(Stick, whyNotNow: null, Token);

        Assert.Contains("unsaved", outcome.Problem);
        Assert.Empty(Directory.GetDirectories(Stick));
    }

    /// <summary>The live session keeps its lock and index while the app runs; neither is a scan.</summary>
    [Fact]
    public async Task The_live_sessions_lock_and_index_do_not_refuse_a_backup()
    {
        Directory.CreateDirectory(Path.Combine(Station.Recovery, "session-live"));
        File.WriteAllText(Path.Combine(Station.Recovery, "session-live", ".lock"), "");
        File.WriteAllText(Path.Combine(Station.Recovery, "session-live", "index.json"), "{\"pages\":[]}");

        var outcome = await Service().BackupAsync(Stick, whyNotNow: null, Token);

        Assert.Null(outcome.Problem);
    }

    [Fact]
    public async Task A_backup_refuses_a_group_outside_the_evidence_folder()
    {
        using (var db = new FgScannerDbContext(DbBootstrapper.BuildOptions(Station.Database)))
        {
            var profile = db.Profiles.Single();
            db.Groups.Add(new Group { Name = "Stray", DirectoryPath = Path.Combine(_root, "Elsewhere", "Stray"), ProfileId = profile.Id });
            db.SaveChanges();
        }

        var outcome = await Service().BackupAsync(Stick, whyNotNow: null, Token);

        Assert.Contains("outside the evidence folder", outcome.Problem);
    }
}
