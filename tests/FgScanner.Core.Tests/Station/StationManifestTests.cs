using System.Text;
using FgScanner.Core.Station;
using Xunit;

namespace FgScanner.Core.Tests.Station;

/// <summary>
/// A station travels between Jim and Franz as a pack whose manifest names every file by hash
/// (SPEC-SCANNER-2026-010 §07, AC-2). Verify is the only thing standing between a damaged
/// upload and evidence that silently differs from what Jim scanned, so every way a copy can be
/// wrong must be named — and a pack still uploading must not read as a broken one.
/// </summary>
public sealed class StationManifestTests : IDisposable
{
    private static readonly DateTimeOffset SentAt = new(2026, 10, 14, 18, 30, 0, TimeSpan.Zero);

    private readonly string _root = Directory.CreateTempSubdirectory("fg-manifest").FullName;

    private string Station => Path.Combine(_root, "station");

    private string Evidence => Path.Combine(_root, "Evidence-Scans");

    private string Pack => Path.Combine(_root, "pack");

    public StationManifestTests()
    {
        Write(Path.Combine(Station, "fgscanner.db"), "database bytes");
        Write(Path.Combine(Evidence, "Dental", "scan_00001.jpg"), "jpeg one");
        Write(Path.Combine(Evidence, "Dental", "scan_00001.md"), "# page one");
        Write(Path.Combine(Evidence, "Dental", "originals", "scan_00001.jpg"), "jpeg one before rotation");
        Write(Path.Combine(Evidence, "Dental", "index.json"), "{}");
        Write(Path.Combine(Evidence, "Dental", "manifest.json"), "{}");
        Write(Path.Combine(Station, "trash", "0001", "scan_00007.jpg"), "trashed jpeg");
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

    private StationManifest Build() => StationManifestBuilder.Build(new StationSource(
        Station: "jim",
        Database: Path.Combine(Station, "fgscanner.db"),
        EvidenceRoot: Evidence,
        Trash: Path.Combine(Station, "trash"),
        AppVersion: "0.8.0",
        LastMigration: "20261001000000_Example",
        BasedOn: null,
        SentAt: SentAt));

    /// <summary>Lays the station out as a pack, manifest last, as <c>station pack</c> will.</summary>
    private StationManifest CopyToPack()
    {
        var manifest = Build();
        Directory.CreateDirectory(Pack);
        File.Copy(Path.Combine(Station, "fgscanner.db"), Path.Combine(Pack, StationManifest.DatabaseFileName));
        foreach (var file in manifest.Files)
        {
            var source = file.Rel.StartsWith("trash/", StringComparison.Ordinal)
                ? Path.Combine(Station, file.Rel)
                : Path.Combine(Evidence, file.Rel["evidence/".Length..]);
            var target = Path.Combine(Pack, file.Rel);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(source, target);
        }

        File.WriteAllBytes(Path.Combine(Pack, StationManifest.FileName), manifest.ToJsonBytes());
        return manifest;
    }

    [Fact]
    public void The_manifest_names_every_evidence_and_trash_file_by_kind()
    {
        var manifest = Build();

        Assert.Equal(
            [
                ("evidence/Dental/index.json", "index"),
                ("evidence/Dental/manifest.json", "index"),
                ("evidence/Dental/originals/scan_00001.jpg", "original"),
                ("evidence/Dental/scan_00001.jpg", "image"),
                ("evidence/Dental/scan_00001.md", "sidecar"),
                ("trash/0001/scan_00007.jpg", "trash"),
            ],
            manifest.Files.Select(f => (f.Rel, f.Kind)));
        Assert.Equal(Convert.ToHexStringLower(
            System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes("jpeg one"))),
            manifest.Files.Single(f => f.Rel == "evidence/Dental/scan_00001.jpg").Sha256);
        Assert.Equal(8, manifest.Files.Single(f => f.Rel == "evidence/Dental/scan_00001.jpg").Size);
        Assert.Equal(Convert.ToHexStringLower(
            System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes("database bytes"))),
            manifest.DbSha256);
        Assert.Equal("jim", manifest.Station);
        Assert.Equal(Evidence, manifest.EvidenceRoot);
    }

    [Fact]
    public void An_exact_copy_verifies_with_no_mismatches()
    {
        var manifest = CopyToPack();

        var result = StationManifestVerifier.Verify(Pack);

        Assert.Equal(StationVerifyStatus.Ok, result.Status);
        Assert.Empty(result.Problems);
        Assert.Equal(manifest.Files.Count + 1, result.FileCount);
        Assert.Equal(StationManifest.CodeOf(manifest.ToJsonBytes()), result.Code);
        Assert.Matches("^[0-9a-f]{4}$", result.Code);
    }

    [Fact]
    public void A_flipped_byte_is_named()
    {
        CopyToPack();
        var image = Path.Combine(Pack, "evidence", "Dental", "scan_00001.jpg");
        var bytes = File.ReadAllBytes(image);
        bytes[0] ^= 0x01;
        File.WriteAllBytes(image, bytes);

        var result = StationManifestVerifier.Verify(Pack);

        Assert.Equal(StationVerifyStatus.NotOk, result.Status);
        Assert.Equal(["changed: evidence/Dental/scan_00001.jpg"], result.Problems);
    }

    [Fact]
    public void A_changed_database_is_named()
    {
        CopyToPack();
        File.AppendAllText(Path.Combine(Pack, StationManifest.DatabaseFileName), "!");

        var result = StationManifestVerifier.Verify(Pack);

        Assert.Equal(["changed: fgscanner.db"], result.Problems);
    }

    [Fact]
    public void A_missing_file_is_named()
    {
        CopyToPack();
        File.Delete(Path.Combine(Pack, "trash", "0001", "scan_00007.jpg"));

        var result = StationManifestVerifier.Verify(Pack);

        Assert.Equal(StationVerifyStatus.NotOk, result.Status);
        Assert.Equal(["missing: trash/0001/scan_00007.jpg"], result.Problems);
    }

    [Fact]
    public void An_extra_file_is_named()
    {
        CopyToPack();
        Write(Path.Combine(Pack, "evidence", "Dental", "scan_00002.jpg"), "not in the manifest");

        var result = StationManifestVerifier.Verify(Pack);

        Assert.Equal(StationVerifyStatus.NotOk, result.Status);
        Assert.Equal(["extra: evidence/Dental/scan_00002.jpg"], result.Problems);
    }

    [Fact]
    public void A_pack_with_no_manifest_yet_is_still_arriving_not_broken()
    {
        CopyToPack();
        File.Delete(Path.Combine(Pack, StationManifest.FileName));

        var result = StationManifestVerifier.Verify(Pack);

        Assert.Equal(StationVerifyStatus.StillArriving, result.Status);
        Assert.Empty(result.Problems);
    }

    [Fact]
    public void Verifying_twice_does_not_trip_over_its_own_log()
    {
        CopyToPack();
        File.WriteAllText(Path.Combine(Pack, StationManifest.VerifyLogFileName), "earlier run");

        Assert.Equal(StationVerifyStatus.Ok, StationManifestVerifier.Verify(Pack).Status);
    }

    [Fact]
    public void The_manifest_round_trips_through_json_byte_for_byte()
    {
        var manifest = Build() with { BasedOn = new string('a', 64) };
        var bytes = manifest.ToJsonBytes();

        var reread = StationManifest.Parse(bytes);

        Assert.Equal(bytes, reread.ToJsonBytes());
        Assert.Equal(manifest.Files, reread.Files);
        Assert.Equal("2026-10-14T18:30:00Z", reread.SentAt);
    }
}
