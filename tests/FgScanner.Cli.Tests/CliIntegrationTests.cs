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

    /// <summary>
    /// SPEC-SCANNER-2026-010 AC-7: a second station's CLI run writes its database inside its own
    /// folder. The overrides carry no database here on purpose — the station must supply it.
    /// </summary>
    [Fact]
    public async Task A_data_dir_puts_the_database_in_that_station()
    {
        var station = Path.Combine(_root, "station-jim");
        var exit = await CliRunner.RunAsync(
            ["scan", "--data-dir", station, "--group", GroupDir, "--source", "feeder"],
            new CliOverrides(ScanService: new FakeScanService(), TessdataDir: Path.Combine(_root, "tessdata")));

        Assert.Equal(0, exit);
        Assert.True(File.Exists(Path.Combine(station, "fgscanner.db")));
    }

    [Fact]
    public async Task A_data_dir_with_no_folder_is_refused()
    {
        var exit = await CliRunner.RunAsync(["scan", "--data-dir"], _overrides);

        Assert.NotEqual(0, exit);
    }

    [Fact]
    public async Task Scan_lands_pages_as_files_and_rows()
    {
        var exit = await RunAsync("scan", "--group", GroupDir, "--source", "feeder");

        Assert.Equal(0, exit);
        Assert.Equal(3, Directory.GetFiles(GroupDir, "scan_*.png").Length);
        var groups = await Groups.ListGroupsAsync(cancellationToken: TestContext.Current.CancellationToken);
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
        var group = Assert.Single(await Groups.ListGroupsAsync(cancellationToken: TestContext.Current.CancellationToken));
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

    /// <summary>
    /// SPEC-SCANNER-2026-010 AC-2: Jim reads the line out on Teams, so it must carry the code and
    /// name the first problem; the full list goes to a log beside the manifest.
    /// </summary>
    [Fact]
    public async Task Station_verify_prints_the_code_and_names_a_flipped_byte()
    {
        var pack = Path.Combine(_root, "pack");
        var evidence = Path.Combine(pack, "evidence", "Dental");
        Directory.CreateDirectory(evidence);
        await File.WriteAllTextAsync(Path.Combine(evidence, "scan_00001.jpg"), "jpeg", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(pack, "fgscanner.db"), "db", TestContext.Current.CancellationToken);
        var manifest = Core.Station.StationManifestBuilder.Build(new Core.Station.StationSource(
            "jim", Path.Combine(pack, "fgscanner.db"), Path.Combine(pack, "evidence"), Path.Combine(pack, "trash"),
            "0.8.0", null, null, DateTimeOffset.UnixEpoch));
        var bytes = manifest.ToJsonBytes();
        await File.WriteAllBytesAsync(Path.Combine(pack, "station-manifest.json"), bytes, TestContext.Current.CancellationToken);
        var code = Core.Station.StationManifest.CodeOf(bytes);

        var (okExit, okOut) = await RunCapturedAsync("station", "verify", pack);
        await File.AppendAllTextAsync(Path.Combine(evidence, "scan_00001.jpg"), "!", TestContext.Current.CancellationToken);
        var (badExit, badOut) = await RunCapturedAsync("station", "verify", pack);
        File.Delete(Path.Combine(pack, "station-manifest.json"));
        var (waitExit, waitOut) = await RunCapturedAsync("station", "verify", pack);

        Assert.Equal(0, okExit);
        Assert.StartsWith($"OK {code} · 2 files · ", okOut);
        Assert.Equal(1, badExit);
        Assert.StartsWith("NOT OK — changed: evidence/Dental/scan_00001.jpg", badOut);
        Assert.Contains("changed: evidence/Dental/scan_00001.jpg",
            await File.ReadAllTextAsync(Path.Combine(pack, "station-verify.log"), TestContext.Current.CancellationToken));
        Assert.Equal(3, waitExit);
        Assert.StartsWith("NOT YET", waitOut);
    }

    private async Task<(int Exit, string Output)> RunCapturedAsync(params string[] args)
    {
        var original = Console.Out;
        using var output = new StringWriter();
        Console.SetOut(output);
        try
        {
            return (await RunAsync(args), output.ToString());
        }
        finally
        {
            Console.SetOut(original);
        }
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
