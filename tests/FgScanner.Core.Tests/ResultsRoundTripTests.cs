using FgScanner.Core.IndexPackages;
using Xunit;

namespace FgScanner.Core.Tests;

/// <summary>
/// SPEC-2026-008 AC-6, the scanner leg. One widened results file — one
/// answer of each field, a person and a date qualifier, a withdrawal —
/// lives in the PORTAL repo (tests/fixtures/index/contract/
/// widened_results.json) and is pinned from both sides: the portal's
/// test_import_package.py imports it expecting zero refusals, and this
/// test proves the real writer produces exactly those bytes. Drift in
/// either writer or importer reds one of the two suites wherever the
/// repos sit side by side (the sync-test pattern).
/// </summary>
public sealed class ResultsRoundTripTests
{
    private static string SiblingFixturePath()
    {
        var jimsStuff = Environment.GetEnvironmentVariable("FG_CONTRACT_SIBLING")
            ?? Path.Combine(Path.GetDirectoryName(TestPaths.RepoRoot())!, "JimsStuff");
        Assert.SkipWhen(!Directory.Exists(jimsStuff),
            $"JimsStuff repo not found at {jimsStuff} (set FG_CONTRACT_SIBLING to point at it)");
        return Path.Combine(
            jimsStuff, "tests", "fixtures", "index", "contract", "widened_results.json");
    }

    /// <summary>The canonical widened answer set, against the golden
    /// package's own vocabulary and anchors.</summary>
    private static IndexAnswer[] CanonicalAnswers()
    {
        var when = new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);
        return
        [
            new("TOM99001", "doc_type", null, "letter", "jim", when),
            new("TOM99001", "date", "about", "2021-07-18", "jim", when),
            new("TOM99001", "person", "from", "P0002", "jim", when),
            new("TOM99001", "subject", null, "accounting-distributions", "jim", when),
            new("TOM99005", "key_flag", null, "true", "jim", when),
            new("TOM99005", "doc_type", null, "", "jim", when),
        ];
    }

    private static byte[] WriteCanonical()
    {
        var work = Path.Combine(
            Path.GetTempPath(), "fgscanner-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        try
        {
            foreach (var file in Directory.EnumerateFiles(
                PackageReaderTests.GoldenPackageDir(), "*", SearchOption.AllDirectories))
            {
                var dest = Path.Combine(work, Path.GetRelativePath(
                    PackageReaderTests.GoldenPackageDir(), file));
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                File.Copy(file, dest, overwrite: true);
            }

            var package = PackageReader.Open(work, appVersion: "0.0.0-test");
            var output = Path.Combine(work, "widened_results.json");
            PackageWriter.WriteResults(package, CanonicalAnswers(), output);
            return File.ReadAllBytes(output);
        }
        finally
        {
            try
            {
                Directory.Delete(work, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }
    }

    [Fact]
    public void TheWriterReproducesTheSharedFixtureByteForByte()
    {
        var fixture = SiblingFixturePath();
        // A sibling repo WITHOUT the fixture is a broken shared state and
        // fails loudly instead of hiding drift (the sync-test stance).
        Assert.True(File.Exists(fixture),
            $"shared fixture missing at {fixture} — regolden it with " +
            "FG_REGOLDEN_WIDENED_RESULTS=yes");
        Assert.Equal(File.ReadAllBytes(fixture), WriteCanonical());
    }

    [Fact]
    public void RegoldenWidenedResults()
    {
        Assert.SkipWhen(
            Environment.GetEnvironmentVariable("FG_REGOLDEN_WIDENED_RESULTS") != "yes",
            "set FG_REGOLDEN_WIDENED_RESULTS=yes to rewrite the shared fixture " +
            "in the sibling repo");
        var fixture = SiblingFixturePath();
        // Target-validated: the fixture directory must already exist in the
        // sibling — this never creates trees in a repo it only guessed at.
        Assert.True(Directory.Exists(Path.GetDirectoryName(fixture)),
            $"fixture directory missing: {Path.GetDirectoryName(fixture)}");
        File.WriteAllBytes(fixture, WriteCanonical());
    }
}
