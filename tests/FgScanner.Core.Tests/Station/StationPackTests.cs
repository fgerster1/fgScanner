using FgScanner.Core.Station;
using Xunit;

namespace FgScanner.Core.Tests.Station;

/// <summary>
/// <c>station pack</c> (SPEC-SCANNER-2026-010 §08, AC-3, AC-4): only what the receiver lacks
/// travels, a rename travels as a rename, and a station that is not at rest is refused — a pack
/// taken mid-scan or mid-read is a backup of a state that never existed.
/// </summary>
public sealed class StationPackTests : IDisposable
{
    private static readonly DateTimeOffset SentAt = new(2026, 10, 16, 9, 5, 0, TimeSpan.FromHours(-4));

    private readonly string _root = Directory.CreateTempSubdirectory("fg-pack").FullName;

    private bool _appOpen;

    private long _freeBytes = long.MaxValue;

    private string Station => Path.Combine(_root, "station");

    private string Evidence => Path.Combine(_root, "Evidence-Scans");

    private string Transport => Path.Combine(_root, "to-franz");

    public StationPackTests()
    {
        Write(Path.Combine(Station, "fgscanner.db"), "database bytes");
        Write(Path.Combine(Evidence, "Dental", "scan_00001.jpg"), "jpeg one");
        Write(Path.Combine(Evidence, "Dental", "scan_00002.jpg"), "jpeg two");
        Write(Path.Combine(Station, "trash", "0001", "scan_00007.jpg"), "trashed jpeg");
        Directory.CreateDirectory(Transport);
    }

    public void Dispose()
    {
        try
        {
            StationManifestTests.RemoveLinks(_root);
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

    private StationPacker Packer() => new(() => _appOpen, File.Copy, _ => _freeBytes);

    private StationPackRequest Request(
        string? basedOn = null, string? destination = null, int pendingReads = 0, DateTimeOffset? at = null) => new(
        new StationSource(
            Station: "jim",
            Database: Path.Combine(Station, "fgscanner.db"),
            EvidenceRoot: Evidence,
            Trash: Path.Combine(Station, "trash"),
            AppVersion: "0.8.0",
            LastMigration: "20261005135716_AddPageOrientationSettled",
            BasedOn: null,
            SentAt: at ?? SentAt),
        StationRoot: Station,
        Destination: destination ?? Transport,
        BasedOnManifest: basedOn,
        Kind: "send",
        PendingReadingJobs: pendingReads);

    private static StationManifest ManifestOf(StationPackResult result) =>
        StationManifest.Parse(File.ReadAllBytes(Path.Combine(result.PackFolder!, StationManifest.FileName)));

    [Fact]
    public void A_first_pack_ships_everything_and_verifies()
    {
        var result = Packer().Pack(Request());

        Assert.Null(result.Problem);
        Assert.Equal(Path.Combine(Transport, "jim-2026-10-16-0905-send"), result.PackFolder);
        var manifest = ManifestOf(result);
        Assert.All(manifest.Files, f => Assert.Null(f.From));
        Assert.Equal(3, result.ShippedFiles);
        var verified = StationManifestVerifier.Verify(result.PackFolder!);
        Assert.Equal(StationVerifyStatus.Ok, verified.Status);
        Assert.Equal(result.Code, verified.Code);
    }

    [Fact]
    public void SecondPackWithNoChangesShipsOnlyTheDatabase()
    {
        var first = Packer().Pack(Request());

        var second = Packer().Pack(Request(
            basedOn: Path.Combine(first.PackFolder!, StationManifest.FileName), at: SentAt.AddMinutes(1)));

        Assert.Null(second.Problem);
        Assert.Equal(0, second.ShippedFiles);
        Assert.Equal(["fgscanner.db", StationManifest.FileName],
            Directory.EnumerateFiles(second.PackFolder!, "*", SearchOption.AllDirectories)
                .Select(Path.GetFileName).Order(StringComparer.Ordinal));
        var manifest = ManifestOf(second);
        Assert.All(manifest.Files, f => Assert.Equal(f.Rel, f.From));
        Assert.Equal(
            Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(
                File.ReadAllBytes(Path.Combine(first.PackFolder!, StationManifest.FileName)))),
            manifest.BasedOn);
        Assert.Equal(StationVerifyStatus.Ok, StationManifestVerifier.Verify(second.PackFolder!).Status);
    }

    [Fact]
    public void RenamesAreNotResent()
    {
        var first = Packer().Pack(Request());
        File.Move(Path.Combine(Evidence, "Dental", "scan_00002.jpg"), Path.Combine(Evidence, "Dental", "scan_00003.jpg"));

        var second = Packer().Pack(Request(
            basedOn: Path.Combine(first.PackFolder!, StationManifest.FileName), at: SentAt.AddMinutes(1)));

        Assert.Equal(0, second.ShippedFiles);
        var renamed = ManifestOf(second).Files.Single(f => f.Rel == "evidence/Dental/scan_00003.jpg");
        Assert.Equal("evidence/Dental/scan_00002.jpg", renamed.From);
        Assert.DoesNotContain(ManifestOf(second).Files, f => f.Rel == "evidence/Dental/scan_00002.jpg");
    }

    [Fact]
    public void A_changed_file_ships()
    {
        var first = Packer().Pack(Request());
        File.WriteAllText(Path.Combine(Evidence, "Dental", "scan_00001.jpg"), "jpeg one, rotated");

        var second = Packer().Pack(Request(
            basedOn: Path.Combine(first.PackFolder!, StationManifest.FileName), at: SentAt.AddMinutes(1)));

        Assert.Equal(1, second.ShippedFiles);
        Assert.True(File.Exists(Path.Combine(second.PackFolder!, "evidence", "Dental", "scan_00001.jpg")));
        Assert.Null(ManifestOf(second).Files.Single(f => f.Rel == "evidence/Dental/scan_00001.jpg").From);
    }

    [Fact]
    public void A_pack_refuses_while_the_app_is_open()
    {
        _appOpen = true;

        AssertRefused(Packer().Pack(Request()), "FG Scanner is open");
    }

    [Fact]
    public void A_pack_refuses_while_recovery_holds_a_session()
    {
        Write(Path.Combine(Station, "recovery", "session-1", "page-00001.jpg"), "unsaved scan");

        AssertRefused(Packer().Pack(Request()), "unsaved scans");
    }

    [Fact]
    public void An_empty_recovery_lock_does_not_refuse()
    {
        Write(Path.Combine(Station, "recovery", "session-1", ".lock"), "");

        Assert.Null(Packer().Pack(Request()).Problem);
    }

    [Fact]
    public void A_pack_refuses_with_a_leftover_journal()
    {
        Write(Path.Combine(Station, "fgscanner.db-journal"), "half a transaction");

        AssertRefused(Packer().Pack(Request()), "was not closed cleanly");
    }

    [Fact]
    public void A_pack_refuses_with_reading_pending()
    {
        AssertRefused(Packer().Pack(Request(pendingReads: 3)), "3 pages are still waiting to be read");
    }

    [Theory]
    [InlineData("station")]
    [InlineData("Evidence-Scans")]
    public void A_pack_refuses_a_destination_inside_the_live_folders(string live)
    {
        var inside = Path.Combine(_root, live, "outbox");

        AssertRefused(Packer().Pack(Request(destination: inside)), "inside the station's own folders");
        Assert.False(Directory.Exists(inside));
    }

    [Fact]
    public void A_pack_refuses_without_room_for_it()
    {
        _freeBytes = 1024;

        AssertRefused(Packer().Pack(Request()), "not enough free space");
    }

    [Fact]
    public void Two_packs_in_one_minute_get_their_own_folders()
    {
        var first = Packer().Pack(Request());
        var second = Packer().Pack(Request());

        Assert.Equal(Path.Combine(Transport, "jim-2026-10-16-0905-2-send"), second.PackFolder);
        Assert.NotEqual(first.PackFolder, second.PackFolder);
    }

    [Fact]
    public void The_freeze_record_keeps_the_sent_manifest_and_the_live_database_hash()
    {
        var result = Packer().Pack(Request());

        var freeze = StationFreezeRecord.Read(Station)!;
        Assert.Equal(Path.GetFileName(result.PackFolder), freeze.Pack);
        Assert.Equal(result.Code, freeze.Code);
        Assert.Equal(StationManifestBuilder.Sha256Of(Path.Combine(Station, "fgscanner.db")), freeze.LiveDbSha256);
        Assert.Equal(
            File.ReadAllBytes(Path.Combine(result.PackFolder!, StationManifest.FileName)),
            File.ReadAllBytes(Path.Combine(Station, StationFreezeRecord.SentManifestFileName)));
    }

    [Fact]
    public void The_manifest_is_written_last()
    {
        var written = new List<string>();
        var packer = new StationPacker(() => false, (from, to) =>
        {
            File.Copy(from, to);
            written.Add(Path.GetFileName(to));
        }, _ => long.MaxValue, onFileWritten: p => written.Add(Path.GetFileName(p)));

        packer.Pack(Request());

        Assert.Equal(StationManifest.FileName, written[^1]);
    }

    /// <summary>A return's change log is the chain of custody for Franz's work: it travels inside
    /// the manifest's hashes, so an edited log fails verify like an edited page.</summary>
    [Fact]
    public void Reports_travel_inside_the_manifest_and_an_edited_one_fails_verify()
    {
        var result = Packer().Pack(Request() with
        {
            Reports = [new StationPackReport("changes.jsonl", "{\"change\":\"changed\"}\n"u8.ToArray()),
                new StationPackReport("needs-jim.txt", "Nothing needs you."u8.ToArray())],
        });

        Assert.Null(result.Problem);
        Assert.Equal(["changes.jsonl", "needs-jim.txt"], ManifestOf(result).Reports!.Select(r => r.Name));
        Assert.Equal(StationVerifyStatus.Ok, StationManifestVerifier.Verify(result.PackFolder!).Status);

        File.AppendAllText(Path.Combine(result.PackFolder!, "changes.jsonl"), "{\"change\":\"forged\"}\n");

        Assert.Equal(["changed: changes.jsonl"], StationManifestVerifier.Verify(result.PackFolder!).Problems);
    }

    [Fact]
    public void A_report_name_outside_the_known_two_is_refused()
    {
        var result = Packer().Pack(Request() with
        {
            Reports = [new StationPackReport("..\\outside.txt", [1])],
        });

        Assert.Contains("not a report a pack carries", result.Problem);
        Assert.Empty(Directory.EnumerateFileSystemEntries(Transport));
    }

    [Fact]
    public void A_junction_in_the_station_refuses_the_pack_and_leaves_nothing()
    {
        var outside = Directory.CreateDirectory(Path.Combine(_root, "outside")).FullName;
        StationManifestTests.Junction(Path.Combine(Evidence, "Linked"), outside);

        var result = Packer().Pack(Request());

        Assert.Contains("link", result.Problem);
        Assert.Empty(Directory.EnumerateFileSystemEntries(Transport));
    }

    [Fact]
    public void A_refusal_leaves_nothing_behind()
    {
        _appOpen = true;

        Packer().Pack(Request());

        Assert.Empty(Directory.EnumerateFileSystemEntries(Transport));
        Assert.Null(StationFreezeRecord.Read(Station));
    }

    private static void AssertRefused(StationPackResult result, string reason)
    {
        Assert.NotNull(result.Problem);
        Assert.Contains(reason, result.Problem, StringComparison.Ordinal);
        Assert.Null(result.PackFolder);
    }
}
