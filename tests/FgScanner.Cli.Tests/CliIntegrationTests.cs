using FgScanner.Cli;
using FgScanner.Data;
using FgScanner.Scanning;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FgScanner.Cli.Tests;

/// <summary>Whole-pipeline CLI runs with the fake scanner and a throwaway database — no UI, no hardware.</summary>
public sealed class CliIntegrationTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("fgs-cli").FullName;
    private readonly string _dbPath;
    private readonly CliOverrides _overrides;

    public CliIntegrationTests()
    {
        _dbPath = Path.Combine(_root, "cli.db");
        _overrides = new CliOverrides(
            ScanService: new FakeScanService(),
            DbPath: _dbPath,
            TessdataDir: Path.Combine(_root, "tessdata"));
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private sealed class Factory(string dbPath) : IDbContextFactory<FgScannerDbContext>
    {
        public FgScannerDbContext CreateDbContext() => new(DbBootstrapper.BuildOptions(dbPath));
    }

    private GroupService Groups => new(new Factory(_dbPath));

    private string GroupDir => Path.Combine(_root, "CliGroup");

    private Task<int> RunAsync(params string[] args) => CliRunner.RunAsync(args, _overrides);

    [Fact]
    public async Task Scan_lands_pages_as_files_and_rows()
    {
        var exit = await RunAsync("scan", "--group", GroupDir, "--source", "feeder");

        Assert.Equal(0, exit);
        Assert.Equal(3, Directory.GetFiles(GroupDir, "scan_*.png").Length);
        var groups = await Groups.ListGroupsAsync(TestContext.Current.CancellationToken);
        var group = Assert.Single(groups);
        Assert.Equal("CliGroup", group.Name);
        Assert.Equal(3, (await Groups.GetPagesAsync(group.Id, TestContext.Current.CancellationToken)).Count);
    }

    [Fact]
    public async Task Scheduled_task_pipeline_scan_then_process_writes_the_index_with_no_ui()
    {
        Assert.Equal(0, await RunAsync("scan", "--group", GroupDir, "--source", "feeder"));

        var exit = await RunAsync("process", GroupDir, "--ocr", "--write-index");

        Assert.Equal(0, exit);
        Assert.True(File.Exists(Path.Combine(GroupDir, "index.csv")));
        Assert.True(File.Exists(Path.Combine(GroupDir, "manifest.json")));
        var group = Assert.Single(await Groups.ListGroupsAsync(TestContext.Current.CancellationToken));
        Assert.All(
            await Groups.GetPagesAsync(group.Id, TestContext.Current.CancellationToken),
            p => Assert.Equal(OcrStatus.Yes, p.OcrStatus)); // real Tesseract ran per page
        Assert.All(
            Directory.GetFiles(GroupDir, "scan_*.png"),
            image => Assert.True(File.Exists(Path.ChangeExtension(image, ".md")), "md sidecar written"));
    }

    [Fact]
    public async Task Export_produces_a_pdf_from_the_group()
    {
        Assert.Equal(0, await RunAsync("scan", "--group", GroupDir, "--source", "feeder"));
        var output = Path.Combine(_root, "out.pdf");

        var exit = await RunAsync("export", "--group", GroupDir, "-o", output, "--pdfcompat", "A2-b");

        Assert.Equal(0, exit);
        var bytes = await File.ReadAllBytesAsync(output, TestContext.Current.CancellationToken);
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(bytes[..4]));
    }

    [Fact]
    public async Task List_devices_reports_the_fake_scanners()
    {
        Assert.Equal(0, await RunAsync("list-devices"));
        Assert.Equal(0, await RunAsync("list-devices", "--driver", "escl"));
        Assert.Equal(1, await RunAsync("list-devices", "--driver", "parallel-port"));
    }

    [Fact]
    public async Task Missing_required_option_is_a_usage_error()
    {
        var exit = await RunAsync("scan"); // --group is required

        Assert.NotEqual(0, exit);
    }

    [Fact]
    public async Task Unknown_profile_fails_with_exit_1()
    {
        var exit = await RunAsync("scan", "--group", GroupDir, "--profile", "DoesNotExist");

        Assert.Equal(1, exit);
    }
}
