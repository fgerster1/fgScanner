using System.IO.Compression;
using FgScanner.Core.IndexPackages;
using Xunit;

namespace FgScanner.Core.Tests;

/// <summary>
/// JimsStuff SPEC-2026-007 Prompt 8 (AC-12, §16 R11). The portal serves a batch as ONE
/// zip (stored, entries at the root, built from the finished folder). Jim
/// opens that file; FG Scanner extracts it into a folder of its own and
/// opens the folder through the unchanged PackageReader — so every check
/// the reader makes still runs on what was extracted. Extraction refuses
/// any entry that would land outside that folder (zip-slip) and leaves
/// nothing behind when it refuses.
/// </summary>
public sealed class ZipPackageOpenerTests : IDisposable
{
    private const string AppVersion = "0.6.0-test";

    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "fgscanner-tests", Guid.NewGuid().ToString("N"));

    private string Downloads => Path.Combine(_root, "Downloads");

    private string ExtractRoot => Path.Combine(_root, "LocalAppData", "index-packages");

    public ZipPackageOpenerTests()
    {
        Directory.CreateDirectory(Downloads);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // AV or the indexer briefly holding a temp file is not a failure
            // of the code under test.
        }
    }

    /// <summary>A zip shaped like the portal's: stored, entries at the root.</summary>
    private string PortalZip(Action<ZipArchive>? extra = null, string name = "PKG-0001.zip")
    {
        var path = Path.Combine(Downloads, name);
        var source = PackageReaderTests.GoldenPackageDir();
        using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            {
                var entry = Path.GetRelativePath(source, file).Replace('\\', '/');
                zip.CreateEntryFromFile(file, entry, CompressionLevel.NoCompression);
            }

            extra?.Invoke(zip);
        }

        return path;
    }

    [Fact]
    public void APortalZipOpensThroughTheReader()
    {
        var opened = ZipPackageOpener.Open(PortalZip(), ExtractRoot, AppVersion);

        Assert.Equal("PKG-0001", opened.Package.PackageId);
        Assert.Equal(Path.Combine(ExtractRoot, "PKG-0001"), opened.PackageDirectory);
        Assert.True(File.Exists(Path.Combine(opened.PackageDirectory, "manifest.json")));
    }

    [Fact]
    public void NoTemporaryFolderIsLeftBehind()
    {
        ZipPackageOpener.Open(PortalZip(), ExtractRoot, AppVersion);

        Assert.Equal(["PKG-0001"],
            Directory.GetDirectories(ExtractRoot).Select(Path.GetFileName).ToArray());
    }

    [Fact]
    public void OpeningTheSameZipAgainReusesTheFolder()
    {
        var zip = PortalZip();
        var first = ZipPackageOpener.Open(zip, ExtractRoot, AppVersion);
        var marker = Path.Combine(first.PackageDirectory, "unlisted-file.txt");
        File.WriteAllText(marker, "the reader ignores files the manifest does not list");

        var second = ZipPackageOpener.Open(zip, ExtractRoot, AppVersion);

        Assert.Equal(first.PackageDirectory, second.PackageDirectory);
        Assert.True(File.Exists(marker), "a verifying folder must be reused, not re-extracted");
    }

    [Fact]
    public void ADamagedExtractedFolderIsReplacedFromTheZip()
    {
        var zip = PortalZip();
        var first = ZipPackageOpener.Open(zip, ExtractRoot, AppVersion);
        File.WriteAllText(Path.Combine(first.PackageDirectory, "seed.json"), "{}");

        var second = ZipPackageOpener.Open(zip, ExtractRoot, AppVersion);

        Assert.Equal("PKG-0001", second.Package.PackageId);
    }

    [Theory]
    [InlineData("../escaped.txt")]
    [InlineData("images/../../escaped.txt")]
    [InlineData("C:/escaped.txt")]
    [InlineData("/escaped.txt")]
    public void AnEntryEscapingThePackageIsRefusedAndNothingIsLeft(string evil)
    {
        var zip = PortalZip(z =>
        {
            using var writer = new StreamWriter(z.CreateEntry(evil).Open());
            writer.Write("should never be written");
        });

        var refusal = Assert.Throws<PackageRefusedException>(
            () => ZipPackageOpener.Open(zip, ExtractRoot, AppVersion));

        Assert.Contains("outside the package", refusal.Message, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(_root, "LocalAppData", "escaped.txt")));
        Assert.False(File.Exists(Path.Combine(ExtractRoot, "escaped.txt")));
        Assert.True(!Directory.Exists(ExtractRoot) || Directory.GetFileSystemEntries(ExtractRoot).Length == 0);
    }

    [Fact]
    public void ADamagedZipSaysDownloadItAgain()
    {
        var zip = PortalZip();
        var bytes = File.ReadAllBytes(zip);
        File.WriteAllBytes(zip, bytes[..(bytes.Length / 2)]);

        var refusal = Assert.Throws<PackageRefusedException>(
            () => ZipPackageOpener.Open(zip, ExtractRoot, AppVersion));

        Assert.Contains("download it again", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AZipWithoutAManifestIsNotAPackage()
    {
        var path = Path.Combine(Downloads, "holiday-photos.zip");
        using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            using var writer = new StreamWriter(zip.CreateEntry("beach.jpg").Open());
            writer.Write("not a package");
        }

        var refusal = Assert.Throws<PackageRefusedException>(
            () => ZipPackageOpener.Open(path, ExtractRoot, AppVersion));

        Assert.Contains("not an index package", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AFileWithAChecksumMismatchIsRefusedByTheReaderAndRemoved()
    {
        var zip = PortalZip();
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Update))
        {
            archive.GetEntry("seed.json")!.Delete();
            using var writer = new StreamWriter(archive.CreateEntry("seed.json").Open());
            writer.Write("{\"documents\": []}");
        }

        var refusal = Assert.Throws<PackageRefusedException>(
            () => ZipPackageOpener.Open(zip, ExtractRoot, AppVersion));

        Assert.Contains("download it again", refusal.Message, StringComparison.Ordinal);
        Assert.False(Directory.Exists(Path.Combine(ExtractRoot, "PKG-0001")),
            "a folder the reader refused must not be left to be reused next time");
    }

    // --- SPEC-2026-007 Prompt 10 review, F4: every failure is a refusal in words.
    // The view model catches refusals only, from an async void handler, so any
    // other exception escaping here ends the app.

    private void AssertNothingLeft() =>
        Assert.True(!Directory.Exists(ExtractRoot) || Directory.GetFileSystemEntries(ExtractRoot).Length == 0,
            "nothing may be left behind: " + string.Join(", ",
                Directory.Exists(ExtractRoot) ? Directory.GetFileSystemEntries(ExtractRoot) : []));

    private static void ReplaceEntry(string zip, string name, string content)
    {
        using var archive = ZipFile.Open(zip, ZipArchiveMode.Update);
        archive.GetEntry(name)!.Delete();
        using var writer = new StreamWriter(archive.CreateEntry(name).Open());
        writer.Write(content);
    }

    [Fact]
    public void APackageIdWithATrailingLineFeedIsRefused()
    {
        var zip = PortalZip();
        var manifest = File.ReadAllText(Path.Combine(PackageReaderTests.GoldenPackageDir(), "manifest.json"));
        ReplaceEntry(zip, "manifest.json",
            manifest.Replace("\"PKG-0001\"", "\"PKG-0001\\n\"", StringComparison.Ordinal));

        var refusal = Assert.Throws<PackageRefusedException>(
            () => ZipPackageOpener.Open(zip, ExtractRoot, AppVersion));

        Assert.Contains("malformed", refusal.Message, StringComparison.Ordinal);
        AssertNothingLeft();
    }

    [Theory]
    [InlineData("seed.json")]          // listed twice
    [InlineData("SEED.json")]          // one file to Windows, two to the zip
    [InlineData("images/what?.jpg")]   // a name Windows cannot hold
    [InlineData("images/nul\0.jpg")]
    public void AnEntryWindowsCannotHoldIsRefusedInWordsAndNothingIsLeft(string entry)
    {
        var zip = PortalZip(z =>
        {
            using var writer = new StreamWriter(z.CreateEntry(entry).Open());
            writer.Write("{}");
        });

        var refusal = Assert.Throws<PackageRefusedException>(
            () => ZipPackageOpener.Open(zip, ExtractRoot, AppVersion));

        Assert.Contains("download it again", refusal.Message, StringComparison.Ordinal);
        AssertNothingLeft();
    }

    // --- F5: a different build under the same id replaces the extracted folder.

    [Fact]
    public void ARebuiltBatchWithTheSameIdIsReExtractedNotServedStale()
    {
        ZipPackageOpener.Open(PortalZip(), ExtractRoot, AppVersion);
        var rebuiltZip = PortalZip(name: "PKG-0001 (1).zip");
        var golden = PackageReaderTests.GoldenPackageDir();
        var seed = File.ReadAllText(Path.Combine(golden, "seed.json"))
            .Replace("Letter about the tractor", "Letter about the tractor, rebuilt", StringComparison.Ordinal);
        var manifest = File.ReadAllText(Path.Combine(golden, "manifest.json")).Replace(
            "2f8795357974e87734d6af81806f3660c993856a4f00b3e2d9b8a1ed29b93ec5",
            Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(seed))),
            StringComparison.Ordinal);
        ReplaceEntry(rebuiltZip, "seed.json", seed);
        ReplaceEntry(rebuiltZip, "manifest.json", manifest);

        var opened = ZipPackageOpener.Open(rebuiltZip, ExtractRoot, AppVersion);

        Assert.Equal(
            Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(manifest))),
            opened.Package.PackageChecksum);
        Assert.Equal("Letter about the tractor, rebuilt", opened.Package.Documents[0].Title);
    }

    [Fact]
    public void AnExtractedFolderThatCannotBeReplacedIsARefusalNotACrash()
    {
        var zip = PortalZip();
        var first = ZipPackageOpener.Open(zip, ExtractRoot, AppVersion);
        File.WriteAllText(Path.Combine(first.PackageDirectory, "seed.json"), "{}");
        // A viewer holding a page open: Windows will not delete the folder.
        using var held = new FileStream(
            Path.Combine(first.PackageDirectory, "images", "TOM99001.jpg"),
            FileMode.Open, FileAccess.Read, FileShare.None);

        var refusal = Assert.Throws<PackageRefusedException>(
            () => ZipPackageOpener.Open(zip, ExtractRoot, AppVersion));

        Assert.Contains("close", refusal.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(Directory.GetDirectories(ExtractRoot, "*.extracting-*"));
    }
}
