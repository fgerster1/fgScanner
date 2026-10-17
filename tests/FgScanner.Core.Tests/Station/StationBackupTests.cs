using FgScanner.Core.Station;
using Xunit;

namespace FgScanner.Core.Tests.Station;

/// <summary>
/// Backups to Jim's USB stick (SPEC-SCANNER-2026-011 AC-3, AC-10): the newest few stay, an old
/// one goes only once a new one is safely written, and a copy cut short by a pulled stick is
/// never counted as a backup.
/// </summary>
public sealed class StationBackupTests : IDisposable
{
    private static readonly DateTimeOffset At = new(2026, 10, 17, 9, 0, 0, TimeSpan.FromHours(-4));

    private readonly string _root = Directory.CreateTempSubdirectory("fg-backup").FullName;

    private long _freeBytes = long.MaxValue;

    private string? _driveFormat = "exFAT";

    private string Station => Path.Combine(_root, "station");

    private string Evidence => Path.Combine(_root, "Evidence-Scans");

    private string Stick => Path.Combine(_root, "stick");

    public StationBackupTests()
    {
        Write(Path.Combine(Station, "fgscanner.db"), "database bytes");
        Write(Path.Combine(Evidence, "Dental", "scan_00001.jpg"), "jpeg one");
        Write(Path.Combine(Station, "trash", "0001", "scan_00007.jpg"), "trashed jpeg");
        Directory.CreateDirectory(Stick);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private static void Write(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private StationBackup Backup(long fat32Limit = StationBackup.Fat32FileLimit) => new(
        new StationPacker(() => false, File.Copy, _ => _freeBytes), _ => _driveFormat, fat32Limit);

    private StationPackRequest Request(int minutes, string station = "jim") => new(
        new StationSource(station, Path.Combine(Station, "fgscanner.db"), Evidence, Path.Combine(Station, "trash"),
            "0.8.2", "M2", null, At.AddMinutes(minutes)),
        Station, Stick, null, StationPacker.BackupKind, 0);

    private string[] Backups() => [.. Directory.GetDirectories(Stick).Select(Path.GetFileName).Order(StringComparer.Ordinal)!];

    [Fact]
    public void The_fourth_backup_removes_the_oldest_once_it_is_written()
    {
        for (var i = 0; i < 3; i++)
        {
            Assert.Null(Backup().Run(Request(i)).Pack.Problem);
        }

        var fourth = Backup().Run(Request(3));

        Assert.Null(fourth.Pack.Problem);
        Assert.Equal(["jim-2026-10-17-0900-backup"], fourth.Removed.Select(Path.GetFileName));
        Assert.Equal(["jim-2026-10-17-0901-backup", "jim-2026-10-17-0902-backup", "jim-2026-10-17-0903-backup"], Backups());
    }

    [Fact]
    public void A_backup_that_fails_removes_nothing()
    {
        for (var i = 0; i < 3; i++)
        {
            Backup().Run(Request(i));
        }

        _freeBytes = 10;
        var fourth = Backup().Run(Request(3));

        Assert.NotNull(fourth.Pack.Problem);
        Assert.Empty(fourth.Removed);
        Assert.Equal(3, Backups().Length);
    }

    /// <summary>AC-10: the stick was pulled before the manifest landed. The folder is not a backup:
    /// it neither counts toward the three nor survives the next good one.</summary>
    [Fact]
    public void A_copy_cut_short_is_never_counted_and_is_cleared_by_the_next_backup()
    {
        Backup().Run(Request(0));
        Backup().Run(Request(1));
        Write(Path.Combine(Stick, "jim-2026-10-17-0902-backup", "fgscanner.db"), "half");

        var next = Backup().Run(Request(3));

        Assert.Null(next.Pack.Problem);
        Assert.Equal(["jim-2026-10-17-0900-backup", "jim-2026-10-17-0901-backup", "jim-2026-10-17-0903-backup"], Backups());
    }

    /// <summary>A backup whose manifest is there but whose files are not is kept for Franz to look at,
    /// and not counted as one of the three.</summary>
    /// <summary>Revision C CR-7: only a folder cut short before this backup began is cleared.</summary>
    [Fact]
    public void A_stranded_folder_newer_than_the_backup_is_left_alone()
    {
        var made = Backup().Run(Request(0)).Pack.PackFolder!;
        var stranded = Path.Combine(Stick, "jim-2026-10-17-0905-backup");
        Write(Path.Combine(stranded, "fgscanner.db"), "half");
        Directory.SetCreationTimeUtc(stranded, Directory.GetCreationTimeUtc(made).AddHours(1));

        Backup().Run(Request(1));

        Assert.True(Directory.Exists(stranded));
    }

    /// <summary>Revision C CR-7: damaged backups are kept for Franz, and the result says they are there.</summary>
    [Fact]
    public void Damaged_backups_are_named_in_the_result()
    {
        var damaged = Backup().Run(Request(0)).Pack.PackFolder!;
        File.Delete(Path.Combine(damaged, "evidence", "Dental", "scan_00001.jpg"));

        var next = Backup().Run(Request(1));

        Assert.Equal([damaged], next.Damaged);
    }

    /// <summary>Revision C CR-5: a page saved while the backup copied is in the database but not
    /// in the pack. The check runs before old backups are cleared, so a refused one costs none.</summary>
    [Fact]
    public void A_backup_the_check_refuses_is_removed_and_costs_no_old_backup()
    {
        for (var i = 0; i < 3; i++)
        {
            Backup().Run(Request(i));
        }

        var refused = new StationBackup(new StationPacker(() => false, File.Copy, _ => _freeBytes), _ => "exFAT",
            check: _ => "the station changed while it was copied — back up again").Run(Request(3));

        Assert.Equal("the station changed while it was copied — back up again", refused.Pack.Problem);
        Assert.Equal(["jim-2026-10-17-0900-backup", "jim-2026-10-17-0901-backup", "jim-2026-10-17-0902-backup"], Backups());
        Assert.Equal(At.AddMinutes(2), LastBackupRecord.Read(Station)!.At);
    }

    /// <summary>Revision D, RR-2: a file written while the station was copied (a commit's index, an
    /// archived original) is not in the backup; the backup is refused before any old one goes.</summary>
    [Fact]
    public void A_file_written_while_copying_refuses_the_backup()
    {
        for (var i = 0; i < 3; i++)
        {
            Backup().Run(Request(i));
        }

        var written = false;
        var packer = new StationPacker(() => false, File.Copy, _ => _freeBytes, onFileWritten: _ =>
        {
            if (!written)
            {
                written = true;
                Write(Path.Combine(Evidence, "Dental", "index.json"), "{}");
            }
        });

        var result = new StationBackup(packer, _ => "exFAT").Run(Request(3));

        Assert.Contains("changed while it was copied", result.Pack.Problem);
        Assert.Contains("index.json", result.Pack.Problem);
        Assert.Equal(["jim-2026-10-17-0900-backup", "jim-2026-10-17-0901-backup", "jim-2026-10-17-0902-backup"], Backups());
    }

    /// <summary>Revision D, RR-6: a check that could not run is no check; the pack goes.</summary>
    [Fact]
    public void A_check_that_throws_removes_the_backup()
    {
        var result = new StationBackup(new StationPacker(() => false, File.Copy, _ => _freeBytes), _ => "exFAT",
            check: _ => throw new IOException("the temp copy was locked")).Run(Request(0));

        Assert.Contains("could not be checked", result.Pack.Problem);
        Assert.Empty(Backups());
        Assert.Null(LastBackupRecord.Read(Station));
    }

    [Fact]
    public void A_damaged_backup_is_left_alone_and_not_counted()
    {
        var damaged = Backup().Run(Request(0)).Pack.PackFolder!;
        File.Delete(Path.Combine(damaged, "evidence", "Dental", "scan_00001.jpg"));
        for (var i = 1; i < 4; i++)
        {
            Backup().Run(Request(i));
        }

        Assert.Equal(4, Backups().Length);
        Assert.Contains("jim-2026-10-17-0900-backup", Backups());
    }

    [Fact]
    public void Another_stations_backups_on_the_same_stick_are_never_touched()
    {
        Backup().Run(Request(0, station: "franz"));
        for (var i = 1; i < 5; i++)
        {
            Backup().Run(Request(i));
        }

        Assert.Contains("franz-2026-10-17-0900-backup", Backups());
        Assert.Equal(3, Backups().Count(b => b.StartsWith("jim-", StringComparison.Ordinal)));
    }

    [Fact]
    public void A_FAT32_stick_refuses_a_file_it_cannot_hold_before_copying_anything()
    {
        _driveFormat = "FAT32";

        var result = Backup(fat32Limit: 10).Run(Request(0));

        Assert.Contains("exFAT", result.Pack.Problem);
        Assert.Empty(Backups());
    }

    [Fact]
    public void A_backup_records_itself_as_the_last_backup()
    {
        var result = Backup().Run(Request(0));

        var last = LastBackupRecord.Read(Station)!;
        Assert.Equal(result.Pack.PackFolder, last.Folder);
        Assert.Equal(result.Pack.Code, last.Code);
        Assert.Equal(At.AddMinutes(0), last.At);
    }

    [Fact]
    public void A_failed_backup_leaves_the_last_backup_as_it_was()
    {
        Backup().Run(Request(0));
        var before = LastBackupRecord.Read(Station);
        _freeBytes = 10;

        Backup().Run(Request(1));

        Assert.Equal(before, LastBackupRecord.Read(Station));
    }

    [Fact]
    public void No_backup_yet_reads_as_none()
    {
        Assert.Null(LastBackupRecord.Read(Station));
    }
}
