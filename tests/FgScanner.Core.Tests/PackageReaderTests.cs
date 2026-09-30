using FgScanner.Core.IndexPackages;
using Xunit;

namespace FgScanner.Core.Tests;

/// <summary>
/// JimsStuff SPEC-2026-005 (contract-slice) AC-3 (behaviour half). The reader refuses before it opens:
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
            File.Copy(file, dest, overwrite: true);
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
    public void ACoreFileTheManifestDoesNotCoverRefuses()
    {
        // A manifest listing only the images is schema-valid (minProperties
        // 1); seed.json would then be read UNVERIFIED — or crash with
        // FileNotFoundException when absent. Both must be the refusal.
        EditJson("manifest.json", m => m["sha256"]!.AsObject().Remove("seed.json"));
        var ex = Assert.Throws<PackageRefusedException>(Open);
        Assert.Contains("seed.json", ex.Message);

        CopyGoldenFresh();
        EditJson("manifest.json", m => m["sha256"]!.AsObject().Remove("people.json"));
        File.Delete(Path.Combine(_root, "people.json"));
        Assert.Throws<PackageRefusedException>(Open);
    }

    [Fact]
    public void AFileAnotherProgramHoldsRefusesInsteadOfCrashing()
    {
        // AV or an open Explorer preview holding a file mid-verification is
        // an operator situation, not a crash dialog.
        using var hold = new FileStream(Path.Combine(_root, "images", "TOM99003.jpg"),
            FileMode.Open, FileAccess.Read, FileShare.None);
        var ex = Assert.Throws<PackageRefusedException>(Open);
        Assert.Contains("TOM99003", ex.Message);
    }

    [Fact]
    public void AFileThatIsNotJsonRefusesAsUnreadable()
    {
        // Checksum-valid but unparseable (the hash is fixed up to match the
        // damage): the parse refusal must fire, not a raw JsonException.
        var seed = Path.Combine(_root, "seed.json");
        File.WriteAllText(seed, File.ReadAllText(seed)[..40]);
        EditJson("manifest.json", m => m["sha256"]!["seed.json"] =
            Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(
                File.ReadAllBytes(seed))));
        var ex = Assert.Throws<PackageRefusedException>(Open);
        Assert.Contains("seed.json", ex.Message);
        Assert.Contains("damaged", ex.Message);

        // And a manifest that is not JSON at all — the file no checksum
        // protects — refuses the same way.
        CopyGoldenFresh();
        File.WriteAllText(Path.Combine(_root, "manifest.json"), "{ \"indexPackage\": ");
        Assert.Throws<PackageRefusedException>(Open);
    }

    [Fact]
    public void EveryParseableButMalformedManifestVariantRefuses()
    {
        // Second-review repros: manifest.json is the one file no checksum
        // protects, so every parseable-but-wrong shape must end in the
        // refusal, never a raw exception.
        foreach (Action<System.Text.Json.Nodes.JsonNode> damage in new Action<System.Text.Json.Nodes.JsonNode>[]
        {
            m => m["formatVersion"] = "1",
            m => m["formatVersion"] = null,
            m => m["vocabularyVersion"] = "two",
            m => m["packageId"] = 7,
            m => m["sha256"]!["seed.json"] = null,
            m => m["sha256"]!.AsObject().Add("bad\u0000name", new string('0', 64)),
        })
        {
            CopyGoldenFresh();
            EditJson("manifest.json", damage);
            Assert.Throws<PackageRefusedException>(Open);
        }

        // A manifest whose root is not even an object.
        CopyGoldenFresh();
        File.WriteAllText(Path.Combine(_root, "manifest.json"), "[]");
        Assert.Throws<PackageRefusedException>(Open);
    }

    [Fact]
    public void ChecksumValidButStructurallyMalformedFilesRefuse()
    {
        // The same refusal contract covers the post-checksum parse phase:
        // checksum-valid-but-wrong-shape (hash fixed up by EditJson) is
        // damage, not a crash.
        foreach (var (file, damage) in new (string, Action<System.Text.Json.Nodes.JsonNode>)[]
        {
            ("seed.json", s => s.AsObject().Remove("documents")),
            ("seed.json", s => s["documents"]![0]!["anchorPageId"] = 7),
            ("seed.json", s => s["documents"]![0]!["pages"] =
                new System.Text.Json.Nodes.JsonArray()),
            ("seed.json", s => s["documents"]![0]!["suggestions"] =
                new System.Text.Json.Nodes.JsonArray(
                    new System.Text.Json.Nodes.JsonObject
                    {
                        ["suggestionId"] = 1, ["field"] = "doc_type",
                        ["value"] = null,
                    })),
            ("people.json", p => p["people"]![0]!.AsObject().Remove("id")),
            ("subjects.json", s => s["subjects"]![0]!["active"] = "yes"),
            ("doctypes.json", d => d["docTypes"] = "nope"),
        })
        {
            CopyGoldenFresh();
            EditJson(file, damage);
            var ex = Assert.Throws<PackageRefusedException>(Open);
            Assert.Contains(file, ex.Message);
        }
    }

    [Fact]
    public void IncoherentSeedsRefuse()
    {
        // A repeated anchor, a page claimed by two documents, an anchor
        // that is not its document's first page, or a docCount that does
        // not match the seed — all exportable by a careless CLI call, all
        // end as a package stuck partial or two conflicting registers.
        EditJson("seed.json", s =>
        {
            var docs = s["documents"]!.AsArray();
            docs[1]!["anchorPageId"] = docs[0]!["anchorPageId"]!.GetValue<string>();
        });
        Assert.Throws<PackageRefusedException>(Open);

        CopyGoldenFresh();
        EditJson("seed.json", s =>
        {
            var docs = s["documents"]!.AsArray();
            docs[1]!["pages"]![0]!["pageId"] =
                docs[0]!["pages"]![0]!["pageId"]!.GetValue<string>();
        });
        Assert.Throws<PackageRefusedException>(Open);

        CopyGoldenFresh();
        EditJson("seed.json", s =>
        {
            var pages = s["documents"]![0]!["pages"]!.AsArray();
            var first = pages[0];
            pages.RemoveAt(0);
            pages.Add(first);
        });
        Assert.Throws<PackageRefusedException>(Open);

        CopyGoldenFresh();
        EditJson("manifest.json", m => m["docCount"] = 5);
        Assert.Throws<PackageRefusedException>(Open);
    }

    [Fact]
    public void AFormatVersionBelowTheKnownOneIsDamageNotAnUpdatePrompt()
    {
        // "Close and reopen to update" on formatVersion 0 is false advice
        // with no exit — the updater has nothing newer.
        var manifest = Path.Combine(_root, "manifest.json");
        File.WriteAllText(manifest,
            File.ReadAllText(manifest).Replace("\"formatVersion\": 1", "\"formatVersion\": 0"));
        var ex = Assert.Throws<PackageRefusedException>(Open);
        Assert.NotEqual(PackageReader.UpdateMessage, ex.Message);
        Assert.Contains("damaged", ex.Message);
    }

    [Fact]
    public void OpenRefusesAnEmptyAppVersion()
    {
        // What Open pins is what results.json echoes; an empty pin would
        // fail the portal's schema only after Jim answered the batch.
        Assert.Throws<ArgumentException>(
            () => PackageReader.Open(_root, appVersion: ""));
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
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // AV briefly holding a temp JPEG mid-test is the same
            // non-failure the Dispose comment describes; the fresh copy
            // below overwrites whatever survived.
        }

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
    public void WriterRefusesADecidedByThatWouldBreakByteFidelity()
    {
        // .NET's relaxed encoder and Python's json.dumps agree on BMP text
        // but not on non-BMP characters (emoji), U+2028/U+2029 or controls
        // — the one free-text field must not be able to violate the
        // contract's byte rule. Accented names stay fine.
        var package = Open();
        var output = Path.Combine(_root, "results.json");
        foreach (var decider in new[] { "jim \U0001F600", "jim\u2028", "jim\u001b" })
        {
            Assert.Throws<ArgumentException>(() => PackageWriter.WriteResults(package,
                [new DocTypeAnswer("TOM99001", "letter", decider,
                    DateTimeOffset.UnixEpoch)], output));
        }

        PackageWriter.WriteResults(package,
            [new DocTypeAnswer("TOM99001", "letter", "Jürgen Müller",
                new DateTimeOffset(2026, 9, 30, 12, 0, 0, TimeSpan.Zero))], output);
        Assert.Contains("Jürgen Müller", File.ReadAllText(output));
    }

    [Fact]
    public void WriterAcceptsAnInactiveDocTypeId()
    {
        // The vocabulary ships inactive rows precisely so old values stay
        // valid ("any older vocabulary stays importable forever") — the
        // portal importer accepts them, so refusing here would strand a
        // whole answered batch. Hiding inactive ids from a picker is the
        // phase-4 UI's job, not this library's.
        EditJson("doctypes.json", d =>
        {
            foreach (var row in d["docTypes"]!.AsArray())
            {
                if (row!["id"]!.GetValue<string>() == "letter")
                {
                    row["active"] = false;
                }
            }
        });
        var package = Open();
        var output = Path.Combine(_root, "results.json");
        PackageWriter.WriteResults(package,
            [new DocTypeAnswer("TOM99001", "letter", "jim",
                new DateTimeOffset(2026, 9, 30, 12, 0, 0, TimeSpan.Zero))], output);
        Assert.Contains("letter", File.ReadAllText(output));
    }

    [Fact]
    public void WriterRefusesTheSpacesTheEncodersDisagreeOn()
    {
        // NBSP pasted from Word, the French narrow no-break space, a BOM:
        // .NET escapes them, Python writes them raw — the byte rule breaks.
        var package = Open();
        var output = Path.Combine(_root, "results.json");
        foreach (var decider in new[] { "jim\u00A0tomaiko", "jim\u202Ftomaiko", "\uFEFFjim" })
        {
            Assert.Throws<ArgumentException>(() => PackageWriter.WriteResults(package,
                [new DocTypeAnswer("TOM99001", "letter", decider,
                    DateTimeOffset.UnixEpoch)], output));
        }
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
