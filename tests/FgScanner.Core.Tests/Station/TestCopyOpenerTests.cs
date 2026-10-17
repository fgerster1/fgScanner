using FgScanner.Core.Station;
using Xunit;

namespace FgScanner.Core.Tests.Station;

/// <summary>
/// The test copy's own guarantees (SPEC-SCANNER-2026-011 Revision C, CR-4/SR-2, SR-3): a copy that
/// fails part-way leaves nothing that looks like a station, the marker lands before anything else
/// does, and the scans' folder can never sit inside another station's evidence on this PC.
/// </summary>
public sealed class TestCopyOpenerTests : IDisposable
{
    private static readonly DateTimeOffset At = new(2026, 10, 17, 9, 0, 0, TimeSpan.FromHours(-4));

    private readonly string _root = Directory.CreateTempSubdirectory("fg-testcopy").FullName;

    private string Jim => Path.Combine(_root, "jim");

    private string Evidence => Path.Combine(_root, "Evidence-Scans");

    private string Workspace => Path.Combine(_root, "ws");

    private string TestEvidence => Path.Combine(_root, "Evidence-Scans-test");

    public TestCopyOpenerTests()
    {
        Write(Path.Combine(Jim, "fgscanner.db"), "database");
        Write(Path.Combine(Evidence, "Dental", "scan_00001.jpg"), "one");
        Write(Path.Combine(Evidence, "Dental", "scan_00002.jpg"), "two");
        Write(Path.Combine(Evidence, "Dental", "scan_00003.jpg"), "three");
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

    private string Backup()
    {
        var stick = Directory.CreateDirectory(Path.Combine(_root, "stick")).FullName;
        var result = new StationPacker(() => false, File.Copy, _ => long.MaxValue).Pack(new StationPackRequest(
            new StationSource("jim", Path.Combine(Jim, "fgscanner.db"), Evidence, Path.Combine(Jim, "trash"), "0.8.2", "M2", null, At),
            Jim, stick, null, StationPacker.BackupKind, 0));
        Assert.Null(result.Problem);
        return result.PackFolder!;
    }

    private sealed class PassingDatabase : IStationDatabase
    {
        public StationContent Content(string database) => new([], "");

        public string? SchemaProblem(string incoming, string? receiver, string? lastMigration) => null;

        public string? PathOutside(string database, string evidenceRoot) => null;

        public string ContentSha256(string database, IReadOnlyList<StationContentTable> tables) => "";

        public void BindToStation(string incoming, string? receiver, string receiverTrash)
        {
        }

        public bool IntegrityOk(string database) => true;

        public void RerootEvidence(string database, string fromRoot, string toRoot)
        {
        }
    }

    [Fact]
    public void A_copy_that_fails_part_way_leaves_nothing_behind()
    {
        var pack = Backup();
        var placed = 0;
        var opener = new TestCopyOpener(new PassingDatabase(), onPlaced: _ =>
        {
            if (++placed == 2)
            {
                throw new IOException("the disk was pulled");
            }
        });

        Assert.Throws<IOException>(() => opener.Open(pack, Workspace, TestEvidence, At));

        Assert.False(File.Exists(Path.Combine(Workspace, "fgscanner.db")));
        Assert.False(File.Exists(Path.Combine(Workspace, TestCopyMarker.FileName)));
        Assert.Empty(Directory.Exists(TestEvidence) ? Directory.GetFiles(TestEvidence, "*", SearchOption.AllDirectories) : []);
    }

    [Fact]
    public void The_marker_lands_before_any_file()
    {
        var pack = Backup();
        var markedFirst = true;
        var opener = new TestCopyOpener(new PassingDatabase(), onPlaced: _ => markedFirst &= TestCopyMarker.IsTestCopy(Workspace));

        Assert.Null(opener.Open(pack, Workspace, TestEvidence, At).Problem);

        Assert.True(markedFirst);
    }

    /// <summary>SR-3: the pack's own evidence path is only text it chose; the scans' folder must also
    /// stay out of every evidence folder this PC's stations really use.</summary>
    [Fact]
    public void The_scans_folder_may_not_sit_inside_another_stations_evidence()
    {
        var pack = Backup();
        var roundTrip = Path.Combine(_root, "C-Evidence-Scans");

        var result = new TestCopyOpener(new PassingDatabase())
            .Open(pack, Workspace, Path.Combine(roundTrip, "test"), At, otherEvidenceRoots: [roundTrip]);

        Assert.Contains("another station", result.Problem);
    }
}
