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

    /// <summary>
    /// SPEC-SCANNER-2026-010 AC-3: the CLI packs a real station (a database the app wrote, a
    /// scanned group), the pack verifies with the same code, and a second pack based on the first
    /// carries only the database.
    /// </summary>
    [Fact]
    public async Task Station_pack_makes_a_pack_that_verifies_and_a_second_that_ships_only_the_database()
    {
        var station = Path.Combine(_root, "station-jim");
        var evidence = Path.Combine(_root, "Evidence-Scans");
        var transport = Directory.CreateDirectory(Path.Combine(_root, "to-franz")).FullName;
        Assert.Equal(0, await CliRunner.RunAsync(
            ["scan", "--data-dir", station, "--group", Path.Combine(evidence, "Dental"), "--source", "feeder"],
            new CliOverrides(ScanService: new FakeScanService(), TessdataDir: Path.Combine(_root, "tessdata"))));
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        string[] pack = ["station", "pack", "--data-dir", station, "--to", transport, "--evidence", evidence, "--station", "jim"];

        var (firstExit, firstOut) = await RunStationCapturedAsync(pack);
        var firstPack = Directory.GetDirectories(transport).Single();
        var (verifyExit, verifyOut) = await RunCapturedAsync("station", "verify", firstPack);
        var (secondExit, secondOut) = await RunStationCapturedAsync(
            [.. pack, "--based-on", Path.Combine(firstPack, "station-manifest.json")]);

        Assert.Equal(0, firstExit);
        Assert.StartsWith("OK ", firstOut);
        Assert.Matches("^jim-\\d{4}-\\d{2}-\\d{2}-\\d{4}-send$", Path.GetFileName(firstPack));
        Assert.Equal(0, verifyExit);
        Assert.Equal(firstOut.Split(" · ")[0], verifyOut.Split(" · ")[0]);
        Assert.Equal(0, secondExit);
        Assert.StartsWith("OK ", secondOut);
        Assert.Contains(" · 1 files · ", secondOut);
        Assert.True(File.Exists(Path.Combine(station, "station-freeze.json")));
    }

    /// <summary>
    /// SPEC-SCANNER-2026-011: <c>station backup</c> writes a whole, checked copy that
    /// <c>station verify</c> accepts with the same code, keeps the newest few, and leaves the
    /// round trip's baseline alone.
    /// </summary>
    [Fact]
    public async Task Station_backup_writes_a_checked_copy_keeps_the_newest_and_leaves_no_baseline()
    {
        var station = Path.Combine(_root, "station-jim");
        var evidence = Path.Combine(_root, "Evidence-Scans");
        var stick = Directory.CreateDirectory(Path.Combine(_root, "stick")).FullName;
        Assert.Equal(0, await CliRunner.RunAsync(
            ["scan", "--data-dir", station, "--group", Path.Combine(evidence, "Dental"), "--source", "feeder"],
            new CliOverrides(ScanService: new FakeScanService(), TessdataDir: Path.Combine(_root, "tessdata"))));
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        var (exit, output) = await RunStationCapturedAsync(
            "station", "backup", "--data-dir", station, "--to", stick, "--evidence", evidence, "--station", "jim", "--keep", "1");
        var first = Directory.GetDirectories(stick).Single();
        var (verifyExit, verifyOut) = await RunCapturedAsync("station", "verify", first);
        await Task.Delay(TimeSpan.FromSeconds(61), TestContext.Current.CancellationToken);
        var (againExit, _) = await RunStationCapturedAsync(
            "station", "backup", "--data-dir", station, "--to", stick, "--evidence", evidence, "--station", "jim", "--keep", "1");

        Assert.Equal(0, exit);
        Assert.StartsWith("OK ", output);
        Assert.Matches("^jim-\\d{4}-\\d{2}-\\d{2}-\\d{4}-backup$", Path.GetFileName(first));
        Assert.Equal(0, verifyExit);
        Assert.Equal(output.Split(" · ")[0], verifyOut.Split(" · ")[0]);
        Assert.False(File.Exists(Path.Combine(station, "station-freeze.json")));
        Assert.True(File.Exists(Path.Combine(station, "last-backup.json")));
        Assert.Equal(0, againExit);
        Assert.NotEqual(first, Assert.Single(Directory.GetDirectories(stick)));
    }

    [Fact]
    public async Task Station_pack_does_not_take_the_backup_kind()
    {
        var (exit, _) = await RunStationCapturedAsync(
            "station", "pack", "--to", _root, "--kind", "backup", "--data-dir", Path.Combine(_root, "s"));

        Assert.Equal(2, exit);
    }

    /// <summary>
    /// SPEC-SCANNER-2026-010 AC-6 through the CLI: a first pack opens into an empty station (as
    /// Franz's workspace receives Jim's first send), and <c>--undo</c> takes it back out.
    /// </summary>
    [Fact]
    public async Task Station_apply_opens_a_first_pack_into_an_empty_station_and_undo_takes_it_back()
    {
        var jim = Path.Combine(_root, "station-jim");
        var franz = Path.Combine(_root, "station-franz");
        var evidence = Path.Combine(_root, "Evidence-Scans");
        var transport = Directory.CreateDirectory(Path.Combine(_root, "to-franz")).FullName;
        Assert.Equal(0, await CliRunner.RunAsync(
            ["scan", "--data-dir", jim, "--group", Path.Combine(evidence, "Dental"), "--source", "feeder"],
            new CliOverrides(ScanService: new FakeScanService(), TessdataDir: Path.Combine(_root, "tessdata"))));
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        Assert.Equal(0, (await RunStationCapturedAsync(
            "station", "pack", "--data-dir", jim, "--to", transport, "--evidence", evidence, "--station", "jim")).Exit);
        var pack = Directory.GetDirectories(transport).Single();
        var jimsImages = Directory.GetFiles(evidence, "*", SearchOption.AllDirectories).Length;
        Directory.Move(evidence, evidence + ".jim");

        var (untypedExit, untypedOut) = await RunStationCapturedAsync("station", "apply", pack, "--data-dir", franz);
        var (applyExit, applyOut) = await RunStationCapturedAsync(
            "station", "apply", pack, "--data-dir", franz, "--evidence", evidence);
        var images = Directory.GetFiles(evidence, "*", SearchOption.AllDirectories).Length;
        var (undoExit, undoOut) = await RunStationCapturedAsync("station", "apply", "--undo", "--data-dir", franz);
        var dbAfterUndo = File.Exists(Path.Combine(franz, "fgscanner.db"));
        var imagesAfterUndo = Directory.GetFiles(evidence, "*", SearchOption.AllDirectories).Length;

        // Opened again, Franz packs a return; the station has then moved on, so undo has nothing to take back.
        Assert.Equal(0, (await RunStationCapturedAsync("station", "apply", pack, "--data-dir", franz, "--evidence", evidence)).Exit);
        var toJim = Directory.CreateDirectory(Path.Combine(_root, "to-jim")).FullName;
        var (unansweredExit, unansweredOut) = await RunStationCapturedAsync(
            "station", "pack", "--data-dir", franz, "--to", toJim, "--kind", "return", "--evidence", evidence);
        var (returnExit, returnOut) = await RunStationCapturedAsync(
            "station", "pack", "--data-dir", franz, "--to", toJim, "--kind", "return", "--evidence", evidence,
            "--based-on", Path.Combine(pack, "station-manifest.json"), "--by", "Franz Gerster");
        var returned = Directory.GetDirectories(toJim).Single();
        var (lateUndoExit, lateUndoOut) = await RunStationCapturedAsync("station", "apply", "--undo", "--data-dir", franz);

        // The pack never chooses where an empty station's files land; the operator types it.
        Assert.Equal(1, untypedExit);
        Assert.StartsWith("NOT OK — say which folder holds the evidence", untypedOut);
        Assert.True(applyExit == 0, applyOut);
        Assert.StartsWith("OK ", applyOut);
        Assert.Equal(jimsImages, images);

        // A return answers one send: its change log is a diff against the database that send carried.
        Assert.Equal(1, unansweredExit);
        Assert.StartsWith("NOT OK — a return must be based on the pack it answers", unansweredOut);
        Assert.True(returnExit == 0, returnOut);
        Assert.Equal("", await File.ReadAllTextAsync(Path.Combine(returned, "changes.jsonl"), TestContext.Current.CancellationToken));
        Assert.Contains("Which box did these pages come from?",
            await File.ReadAllTextAsync(Path.Combine(returned, "needs-jim.txt"), TestContext.Current.CancellationToken));
        Assert.True(undoExit == 0, undoOut);
        Assert.StartsWith("OK — put back", undoOut);
        Assert.False(dbAfterUndo);
        Assert.Equal(0, imagesAfterUndo);
        Assert.Equal(1, lateUndoExit);
        Assert.Contains("nothing to undo", lateUndoOut);
    }

    /// <summary>Revision D, D5: the verbs hold FG Scanner's own single-instance lock, so a running
    /// app (here: this test holding the lock) refuses them, and an app launched mid-way cannot open.</summary>
    [Fact]
    public async Task Station_pack_refuses_while_the_app_holds_its_lock()
    {
        var transport = Directory.CreateDirectory(Path.Combine(_root, "to-franz")).FullName;
        using var app = new Mutex(initiallyOwned: true, "FGScanner.SingleInstance", out var ours);
        Assert.True(ours, "another FG Scanner is running on this machine; close it to run this test");
        try
        {
            var (exit, output) = await RunStationCapturedAsync(
                "station", "pack", "--data-dir", Path.Combine(_root, "station-jim"), "--to", transport);

            Assert.Equal(1, exit);
            Assert.StartsWith("NOT OK — FG Scanner is open", output);
        }
        finally
        {
            app.ReleaseMutex();
        }
    }

    [Fact]
    public async Task Station_pack_refuses_a_group_outside_the_evidence_folder()
    {
        var station = Path.Combine(_root, "station-jim");
        var transport = Directory.CreateDirectory(Path.Combine(_root, "to-franz")).FullName;
        Assert.Equal(0, await CliRunner.RunAsync(
            ["scan", "--data-dir", station, "--group", GroupDir, "--source", "feeder"],
            new CliOverrides(ScanService: new FakeScanService(), TessdataDir: Path.Combine(_root, "tessdata"))));
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        var (exit, output) = await RunStationCapturedAsync(
            "station", "pack", "--data-dir", station, "--to", transport,
            "--evidence", Directory.CreateDirectory(Path.Combine(_root, "Evidence-Scans")).FullName, "--station", "jim");

        Assert.Equal(1, exit);
        Assert.StartsWith("NOT OK — a group lives outside the evidence folder", output);
        Assert.Empty(Directory.EnumerateFileSystemEntries(transport));
    }

    private Task<(int Exit, string Output)> RunCapturedAsync(params string[] args) =>
        RunCapturedAsync(_overrides, args);

    /// <summary>A station's own database: the shared test database would win over <c>--data-dir</c>.</summary>
    private Task<(int Exit, string Output)> RunStationCapturedAsync(params string[] args) =>
        RunCapturedAsync(new CliOverrides(TessdataDir: Path.Combine(_root, "tessdata")), args);

    private static async Task<(int Exit, string Output)> RunCapturedAsync(CliOverrides overrides, string[] args)
    {
        var original = Console.Out;
        using var output = new StringWriter();
        Console.SetOut(output);
        try
        {
            return (await CliRunner.RunAsync(args, overrides), output.ToString());
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
