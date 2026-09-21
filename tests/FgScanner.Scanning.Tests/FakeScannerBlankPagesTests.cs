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

    /// <summary>
    /// A stack that comes up short on the second pass is the ordinary failure of a two-pass run —
    /// a double feed, or a sheet left in the tray — and the last sheet of an odd stack has no back
    /// at all. Neither can be rehearsed without paper unless the runs can differ, which is what
    /// the manual protocol in docs/manual-tests.md needs before it reaches a real scanner.
    /// </summary>
    [Fact]
    public async Task Each_run_can_be_given_its_own_page_count()
    {
        var service = new FakeScanService { PagesPerRun = [3, 2] };

        Assert.Equal(3, (await ScanAsync(service)).Count);
        Assert.Equal(2, (await ScanAsync(service)).Count);

        // The last count repeats, so a long session does not fall off the end of the list.
        Assert.Equal(2, (await ScanAsync(service)).Count);
    }

    /// <summary>AC-10's neighbour: unset, it changes nothing about an ordinary fake scan.</summary>
    [Fact]
    public async Task Without_per_run_counts_every_run_uses_the_page_count()
    {
        var service = new FakeScanService { PageCount = 4 };

        Assert.Equal(4, (await ScanAsync(service)).Count);
        Assert.Equal(4, (await ScanAsync(service)).Count);
    }

    /// <summary>Off by default, so nothing that scans with the fake starts colliding.</summary>
    [Fact]
    public async Task An_ordinary_fake_scan_still_produces_pages_that_differ()
    {
        var paths = await ScanAsync(new FakeScanService { PageCount = 4 });

        Assert.Equal(4, paths.Select(Checksum).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }
}
