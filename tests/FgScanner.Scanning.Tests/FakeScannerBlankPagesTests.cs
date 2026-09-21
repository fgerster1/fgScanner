using System.Security.Cryptography;
using FgScanner.Scanning.Recovery;
using Xunit;

namespace FgScanner.Scanning.Tests;

/// <summary>
/// The fake scanner stamps a run number and a page number into every bitmap so its pages never
/// collide on checksum — which is why no test in this solution has ever produced the case that
/// breaks a duplex save. This mode produces it deliberately: a stack of blank backs, identical to
/// the byte.
/// </summary>
public sealed class FakeScannerBlankPagesTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "fgscanner-tests", Guid.NewGuid().ToString("N"));

    public FakeScannerBlankPagesTests() => Directory.CreateDirectory(_root);

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

    private static string Checksum(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private async Task<List<string>> ScanAsync(FakeScanService service)
    {
        using var session = RecoverySession.Create(_root);
        var paths = new List<string>();
        await foreach (var page in service.ScanAsync(
            new ScanProfileOptions { Source = ScanSource.Feeder }, session, TestContext.Current.CancellationToken))
        {
            paths.Add(page.FilePath);
        }

        return paths;
    }

    [Fact]
    public async Task Blank_pages_are_identical_to_the_byte()
    {
        var paths = await ScanAsync(new FakeScanService { PageCount = 4, BlankIdenticalPages = true });

        var checksums = paths.Select(Checksum).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        Assert.Equal(4, paths.Count);
        Assert.Single(checksums);
    }

    /// <summary>Off by default, so nothing that scans with the fake starts colliding.</summary>
    [Fact]
    public async Task An_ordinary_fake_scan_still_produces_pages_that_differ()
    {
        var paths = await ScanAsync(new FakeScanService { PageCount = 4 });

        Assert.Equal(4, paths.Select(Checksum).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }
}
