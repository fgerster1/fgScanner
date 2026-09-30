using FgScanner.Core.IndexPackages;
using Xunit;

namespace FgScanner.Core.Tests;

/// <summary>
/// SPEC-2026-005 AC-3 (behaviour half). The reader refuses before it opens:
/// a damaged file or an unknown formatVersion never gets as far as showing
/// Jim a document. The writer echoes provenance pinned at open time and
/// refuses answers the package cannot support. Golden byte-fidelity is
/// ContractGoldenTests; this file is the refusals and the rules.
/// </summary>
public sealed class PackageReaderTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "fgscanner-tests", Guid.NewGuid().ToString("N"));

    public PackageReaderTests()
    {
        Directory.CreateDirectory(_root);
        CopyGolden(_root);
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

    internal static string GoldenPackageDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "FgScanner.slnx")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        return Path.Combine(dir.FullName, "docs", "contract-vendored", "golden", "package", "PKG-0001");
    }

    private static void CopyGolden(string target)
    {
        var source = GoldenPackageDir();
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var dest = Path.Combine(target, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            File.Copy(file, dest);
        }
    }

    private IndexPackage Open() => PackageReader.Open(_root, appVersion: "0.6.0-test");

    [Fact]
    public void OpensTheGoldenCopyAndPinsProvenance()
    {
        var package = Open();
        Assert.Equal("PKG-0001", package.PackageId);
        Assert.Equal(1, package.FormatVersion);
        Assert.Equal(6, package.Documents.Count);
        Assert.Equal("0.6.0-test", package.AppVersionAtOpen);
        Assert.Equal(2, package.People.Count);
        Assert.Contains(package.Subjects, s => !s.Active);
        Assert.Matches("^[0-9a-f]{64}$", package.PackageChecksum);
    }

    [Fact]
    public void SeedDecisionsAndSuggestionsComeThrough()
    {
        var package = Open();
        var doc1 = package.Documents.Single(d => d.AnchorPageId == "TOM99001");
        Assert.Contains(doc1.Decisions, d => d is { Field: "doc_type", Value: "letter" });
        Assert.Equal(2, doc1.Pages.Count);
        var doc3 = package.Documents.Single(d => d.AnchorPageId == "TOM99005");
        Assert.Contains(doc3.Suggestions, s => s is { Field: "doc_type", Value: "card-note" });
    }

    [Fact]
    public void ACorruptedFileRefusesBeforeOpening()
    {
        var seed = Path.Combine(_root, "seed.json");
        File.WriteAllText(seed, File.ReadAllText(seed) + " ");
        var ex = Assert.Throws<PackageRefusedException>(Open);
        Assert.Contains("seed.json", ex.Message);
        Assert.Contains("damaged", ex.Message);
    }

    [Fact]
    public void AMissingImageRefusesBeforeOpening()
    {
        File.Delete(Path.Combine(_root, "images", "TOM99007.jpg"));
        var ex = Assert.Throws<PackageRefusedException>(Open);
        Assert.Contains("TOM99007", ex.Message);
    }

    [Fact]
    public void AnUnknownFormatVersionRefusesWithTheUpdateMessage()
    {
        var manifest = Path.Combine(_root, "manifest.json");
        File.WriteAllText(manifest,
            File.ReadAllText(manifest).Replace("\"formatVersion\": 1", "\"formatVersion\": 99"));
        var ex = Assert.Throws<PackageRefusedException>(Open);
        Assert.Equal(
            "This package needs a newer FG Scanner — close and reopen the app to update.",
            ex.Message);
    }

    [Fact]
    public void SomethingThatIsNotAnIndexPackageRefuses()
    {
        var stray = Path.Combine(_root, "stray");
        Directory.CreateDirectory(stray);
        Assert.Throws<PackageRefusedException>(
            () => PackageReader.Open(stray, appVersion: "0.6.0-test"));

        // The capture evidence contract's manifest is NOT an index package —
        // the wall between the two contracts (CLAUDE.md).
        File.WriteAllText(Path.Combine(stray, "manifest.json"),
            """{"evidenceExport": 1}""");
        Assert.Throws<PackageRefusedException>(
            () => PackageReader.Open(stray, appVersion: "0.6.0-test"));
    }

    [Fact]
    public void WriterEchoesProvenanceAndAssignsSeqInOrder()
    {
        var package = Open();
        var output = Path.Combine(_root, "results.json");
        PackageWriter.WriteResults(package,
        [
            new DocTypeAnswer("TOM99005", "card-note", "jim",
                new DateTimeOffset(2026, 9, 30, 12, 0, 0, TimeSpan.Zero)),
            new DocTypeAnswer("TOM99001", "letter", "jim",
                new DateTimeOffset(2026, 9, 30, 12, 5, 0, TimeSpan.Zero)),
        ], output);

        using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(output));
        var rootEl = doc.RootElement;
        Assert.Equal("PKG-0001", rootEl.GetProperty("packageId").GetString());
        Assert.Equal(package.PackageChecksum, rootEl.GetProperty("packageChecksum").GetString());
        Assert.Equal("0.6.0-test", rootEl.GetProperty("appVersion").GetString());
        var answers = rootEl.GetProperty("answers").EnumerateArray().ToList();
        Assert.Equal([1, 2], answers.Select(a => a.GetProperty("seq").GetInt32()));
        Assert.Equal("doc_type", answers[0].GetProperty("field").GetString());
        Assert.Equal("2026-09-30T12:00:00Z", answers[0].GetProperty("decidedAt").GetString());
    }

    [Fact]
    public void WriterRefusesAnswersThePackageCannotSupport()
    {
        var package = Open();
        var output = Path.Combine(_root, "results.json");

        // An anchor that is not in the package.
        Assert.Throws<ArgumentException>(() => PackageWriter.WriteResults(package,
            [new DocTypeAnswer("TOM00042", "letter", "jim", DateTimeOffset.UnixEpoch)], output));

        // A doc type the vocabulary does not know.
        Assert.Throws<ArgumentException>(() => PackageWriter.WriteResults(package,
            [new DocTypeAnswer("TOM99001", "no-such-type", "jim", DateTimeOffset.UnixEpoch)], output));

        // Nothing was written by a refused call.
        Assert.False(File.Exists(output));
    }
}
