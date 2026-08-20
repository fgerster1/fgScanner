using FgScanner.Scanning;
using FgScanner.Scanning.Recovery;
using Xunit;

namespace FgScanner.Scanning.Tests;

public sealed class FakeScanServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "fgscanner-tests", Guid.NewGuid().ToString("N"));

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

    [Fact]
    public async Task Feeder_scan_streams_all_pages_into_storage()
    {
        var service = new FakeScanService { PageCount = 4 };
        using var session = RecoverySession.Create(_root);

        var pages = new List<ScannedPage>();
        await foreach (var page in service.ScanAsync(
            new ScanProfileOptions { Source = ScanSource.Feeder }, session, TestContext.Current.CancellationToken))
        {
            pages.Add(page);
        }

        Assert.Equal(4, pages.Count);
        Assert.Equal([1, 2, 3, 4], pages.Select(p => p.SequenceNumber));
        Assert.All(pages, p => Assert.True(new FileInfo(p.FilePath).Length > 0));
        Assert.Equal(4, session.Pages.Count);
    }

    [Fact]
    public async Task Flatbed_scan_produces_exactly_one_page()
    {
        var service = new FakeScanService { PageCount = 5 };
        using var session = RecoverySession.Create(_root);

        var count = 0;
        await foreach (var _ in service.ScanAsync(
            new ScanProfileOptions { Source = ScanSource.Flatbed }, session, TestContext.Current.CancellationToken))
        {
            count++;
        }

        Assert.Equal(1, count);
    }

    [Fact]
    public async Task Driver_failure_mid_feed_keeps_already_scanned_pages()
    {
        var service = new FakeScanService
        {
            PageCount = 5,
            Error = new IOException("paper jam"),
            ErrorAfterPages = 2,
        };
        using var session = RecoverySession.Create(_root);

        var received = new List<ScannedPage>();
        await Assert.ThrowsAsync<IOException>(async () =>
        {
            await foreach (var page in service.ScanAsync(
                new ScanProfileOptions { Source = ScanSource.Feeder }, session, TestContext.Current.CancellationToken))
            {
                received.Add(page);
            }
        });

        Assert.Equal(2, received.Count);
        Assert.Equal(2, session.Pages.Count); // committed pages survive the failure
    }

    [Fact]
    public async Task Device_listing_filters_by_driver()
    {
        var service = new FakeScanService();

        var wia = await service.ListDevicesAsync(ScanDriver.Wia, TestContext.Current.CancellationToken);
        var escl = await service.ListDevicesAsync(ScanDriver.Escl, TestContext.Current.CancellationToken);

        Assert.All(wia, d => Assert.Equal(ScanDriver.Wia, d.Driver));
        Assert.All(escl, d => Assert.Equal(ScanDriver.Escl, d.Driver));
        Assert.NotEmpty(wia);
    }

    [Fact]
    public async Task Cancellation_stops_the_feed()
    {
        var service = new FakeScanService { PageCount = 100, PageDelay = TimeSpan.FromMilliseconds(10) };
        using var session = RecoverySession.Create(_root);
        using var cts = new CancellationTokenSource();

        var received = 0;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var _ in service.ScanAsync(
                new ScanProfileOptions { Source = ScanSource.Feeder }, session, cts.Token))
            {
                if (++received == 2)
                {
                    cts.Cancel();
                }
            }
        });

        Assert.True(received < 100);
    }
}
