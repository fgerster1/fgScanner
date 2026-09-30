using FgScanner.Core.IndexPackages;
using Xunit;

namespace FgScanner.Core.Tests;

/// <summary>
/// JimsStuff SPEC-2026-005 (contract-slice) AC-3 (golden half). The reader opens the COMMITTED golden
/// package byte-for-byte as JimsStuff's exporter wrote it, and the writer
/// reproduces the COMMITTED golden results.json — each side's suite consumes
/// the other side's committed output, which is how the round trip is proven
/// without ever running the two apps together.
/// </summary>
public sealed class ContractGoldenTests
{
    /// <summary>
    /// The canonical golden answer set. Fixed values on purpose: the golden
    /// results.json is regenerated from exactly this list (RegoldenResults
    /// below), and the compare test writes exactly this list again.
    /// </summary>
    private static readonly DocTypeAnswer[] GoldenAnswers =
    [
        // One answer per golden document: a complete batch, so the portal's
        // golden import (AC-4) flips the package to imported, not partial.
        new("TOM99001", "letter", "jim",
            new DateTimeOffset(2026, 9, 30, 12, 0, 0, TimeSpan.Zero)),
        new("TOM99003", "attorney-invoice", "jim",
            new DateTimeOffset(2026, 9, 30, 12, 5, 0, TimeSpan.Zero)),
        new("TOM99005", "card-note", "jim",
            new DateTimeOffset(2026, 9, 30, 12, 10, 0, TimeSpan.Zero)),
        new("TOM99007", "trust-amendment", "jim",
            new DateTimeOffset(2026, 9, 30, 12, 15, 0, TimeSpan.Zero)),
        new("TOM99009", "invoice-bill", "jim",
            new DateTimeOffset(2026, 9, 30, 12, 20, 0, TimeSpan.Zero)),
        new("TOM99011", "unidentified", "jim",
            new DateTimeOffset(2026, 9, 30, 12, 25, 0, TimeSpan.Zero)),
    ];

    private const string GoldenAppVersion = "0.0.0-golden";

    private static string GoldenResultsPath()
    {
        var package = PackageReaderTests.GoldenPackageDir();
        return Path.GetFullPath(Path.Combine(package, "..", "..", "results.json"));
    }

    [Fact]
    public void ReaderOpensTheCommittedGoldenPackage()
    {
        var package = PackageReader.Open(PackageReaderTests.GoldenPackageDir(), GoldenAppVersion);
        Assert.Equal("PKG-0001", package.PackageId);
        Assert.Equal(6, package.Documents.Count);
        Assert.Equal(12, package.Documents.Sum(d => d.Pages.Count));
    }

    [Fact]
    public void WriterReproducesTheCommittedGoldenResults()
    {
        var goldenPath = GoldenResultsPath();
        Assert.True(File.Exists(goldenPath),
            $"no committed golden results at {goldenPath} — regenerate with the real " +
            "writer (FG_REGOLDEN_RESULTS) and re-run build/sync-contract.ps1, never by hand");

        var package = PackageReader.Open(PackageReaderTests.GoldenPackageDir(), GoldenAppVersion);
        var output = Path.Combine(Path.GetTempPath(), "fgscanner-tests",
            Guid.NewGuid().ToString("N") + "-results.json");
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        try
        {
            PackageWriter.WriteResults(package, GoldenAnswers, output);
            Assert.Equal(File.ReadAllBytes(goldenPath), File.ReadAllBytes(output));
        }
        finally
        {
            File.Delete(output);
        }
    }

    [Fact]
    public void RegoldenResults()
    {
        // The deliberate act: set FG_REGOLDEN_RESULTS to the JimsStuff
        // docs/contract/golden directory, run this one test, review the
        // diff there, commit, then re-run build/sync-contract.ps1 so the
        // vendored copy follows. Unset, it is an ordinary skip.
        var target = Environment.GetEnvironmentVariable("FG_REGOLDEN_RESULTS");
        Assert.SkipWhen(string.IsNullOrEmpty(target),
            "regolden only runs when FG_REGOLDEN_RESULTS points at the contract's golden dir");

        // The var overwrites contract law, so the target must actually BE a
        // golden dir — pointed one level up it would mint a stray
        // results.json that the next sync hashes into both manifests.
        Assert.True(Directory.Exists(target), $"no directory at {target}");
        Assert.True(string.Equals(Path.GetFileName(Path.TrimEndingDirectorySeparator(target!)),
                "golden", StringComparison.OrdinalIgnoreCase)
            && File.Exists(Path.Combine(target!, "package", "PKG-0001", "manifest.json")),
            $"{target} is not a contract golden directory (expected .../golden with package/PKG-0001 inside)");
        var package = PackageReader.Open(PackageReaderTests.GoldenPackageDir(), GoldenAppVersion);
        PackageWriter.WriteResults(package, GoldenAnswers, Path.Combine(target!, "results.json"));
    }
}
