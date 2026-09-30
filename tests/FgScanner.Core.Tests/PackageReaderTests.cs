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
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // AV or the search indexer briefly holding a temp JPEG is not a
            // failure of the code under test.
        }
    }

    internal static string GoldenPackageDir() => Path.Combine(
        TestPaths.RepoRoot(), "docs", "contract-vendored", "golden", "package", "PKG-0001");

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

    /// <summary>Mutate a JSON file in the copy, then fix up the manifest's
    /// hash for it so only the mutation under test differs.</summary>
    private void EditJson(string relative, Action<System.Text.Json.Nodes.JsonNode> edit)
    {
        var path = Path.Combine(_root, relative);
        var node = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(path))!;
        edit(node);
        File.WriteAllText(path, node.ToJsonString());
        if (relative != "manifest.json")
        {
            EditJson("manifest.json", manifest => manifest["sha256"]![relative] =
                Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(
                    File.ReadAllBytes(path))));
        }
    }

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
    public void AnOmittedQualifierKeyIsSchemaLegalAndStillOpens()
    {
        // seed.schema.json requires only [field, value] on a decision and
        // [suggestionId, field, value] on a suggestion; additive-change
        // packages may stop emitting null qualifiers entirely.
        EditJson("seed.json", seed =>
        {
            foreach (var doc in seed["documents"]!.AsArray())
            {
                foreach (var decision in doc!["decisions"]!.AsArray())
                {
                    decision!.AsObject().Remove("qualifier");
                }

                foreach (var suggestion in doc["suggestions"]!.AsArray())
                {
                    suggestion!.AsObject().Remove("qualifier");
                }
            }
        });
        var package = Open();
        var doc1 = package.Documents.Single(d => d.AnchorPageId == "TOM99001");
        Assert.Contains(doc1.Decisions, d => d is { Field: "doc_type", Qualifier: null });
    }

    [Fact]
    public void AMalformedManifestRefusesInsteadOfCrashing()
    {
        // manifest.json is the one file no checksum protects, so damage that
        // stays parseable must still end in the operator-facing refusal —
        // never a raw KeyNotFoundException/FormatException crash dialog.
        EditJson("manifest.json", m => m.AsObject().Remove("packageId"));
        var ex = Assert.Throws<PackageRefusedException>(Open);
        Assert.Contains("damaged", ex.Message);

        CopyGoldenFresh();
        EditJson("manifest.json", m => m["formatVersion"] = 1.5);
        Assert.Throws<PackageRefusedException>(Open);

        CopyGoldenFresh();
        EditJson("manifest.json", m => m["sha256"]!["seed.json"] = 7);
        Assert.Throws<PackageRefusedException>(Open);
    }

    [Fact]
    public void AManifestPathThatEscapesThePackageRefuses()
    {
        // The manifest is untrusted input: a listed path must never make the
        // reader hash (or report on) anything outside the package folder.
        EditJson("manifest.json", m => m["sha256"]!["../escape.txt"] = new string('0', 64));
        var ex = Assert.Throws<PackageRefusedException>(Open);
        Assert.Contains("outside the package", ex.Message);

        // An absolute path must refuse for ESCAPING, not by reading the
        // outside file and reporting on its hash — that report is an oracle.
        CopyGoldenFresh();
        EditJson("manifest.json", m => m["sha256"]![@"C:\Windows\win.ini"] = new string('0', 64));
        ex = Assert.Throws<PackageRefusedException>(Open);
        Assert.Contains("outside the package", ex.Message);
    }

    [Fact]
    public void ASeedImageTheManifestDoesNotCoverRefuses()
    {
        // "Passed all three checks" must mean every page Jim will be shown
        // is present and verified — not just the files the manifest happened
        // to list.
        EditJson("manifest.json", m => m["sha256"]!.AsObject().Remove("images/TOM99008.jpg"));
        var ex = Assert.Throws<PackageRefusedException>(Open);
        Assert.Contains("TOM99008", ex.Message);
    }

    private void CopyGoldenFresh()
    {
        Directory.Delete(_root, recursive: true);
        Directory.CreateDirectory(_root);
        CopyGolden(_root);
    }

    [Fact]
    public void WriterReplacesAnExistingResultsFileAndLeavesNoTemp()
    {
        // CLAUDE.md hard rule: all index/file writes are atomic (temp file
        // beside the target, then replace) — a crash mid-write must never
        // destroy the previous complete results.json.
        var package = Open();
        var output = Path.Combine(_root, "results.json");
        File.WriteAllText(output, "previous complete answers");
        PackageWriter.WriteResults(package,
            [new DocTypeAnswer("TOM99001", "letter", "jim",
                new DateTimeOffset(2026, 9, 30, 12, 0, 0, TimeSpan.Zero))], output);
        Assert.Contains("TOM99001", File.ReadAllText(output));
        Assert.Empty(Directory.GetFiles(_root, "*.tmp"));
    }

    [Fact]
    public void WriterRefusesAnEmptyDecidedBy()
    {
        // results.schema.json requires decidedBy minLength 1; a blank
        // operator name must fail here, not at the portal after Jim
        // answered the whole batch.
        var package = Open();
        var output = Path.Combine(_root, "results.json");
        Assert.Throws<ArgumentException>(() => PackageWriter.WriteResults(package,
            [new DocTypeAnswer("TOM99001", "letter", "",
                DateTimeOffset.UnixEpoch)], output));
        Assert.False(File.Exists(output));
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
