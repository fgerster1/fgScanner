using System.Security.Cryptography;
using FgScanner.Cli;
using FgScanner.Scanning;
using Microsoft.Data.Sqlite;
using Xunit;

namespace FgScanner.Cli.Tests;

/// <summary>
/// SPEC-SCANNER-2026-011 AC-7 (Q5, Q6): Jim's backup opens on Franz's PC as a test copy — its
/// evidence in a folder Franz names, because Jim's own folder path is taken there by the round-trip
/// workspace — and a test copy can never be sent or returned, so nothing done in it reaches Jim.
/// </summary>
public sealed class TestCopyTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("fgs-testcopy").FullName;

    private string Jim => Path.Combine(_root, "station-jim");

    /// <summary>Jim's evidence path; on Franz's PC the round-trip workspace already uses it.</summary>
    private string Evidence => Path.Combine(_root, "Evidence-Scans");

    private string TestEvidence => Path.Combine(_root, "Evidence-Scans-test");

    private string Workspace => Path.Combine(_root, "station-jim-test");

    private string Stick => Path.Combine(_root, "stick");

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private CliOverrides Overrides => new(ScanService: new FakeScanService(), TessdataDir: Path.Combine(_root, "tessdata"));

    private async Task<(int Exit, string Output)> RunAsync(params string[] args)
    {
        var original = Console.Out;
        using var output = new StringWriter();
        Console.SetOut(output);
        try
        {
            var exit = await CliRunner.RunAsync(args, Overrides);
            SqliteConnection.ClearAllPools();
            return (exit, output.ToString());
        }
        finally
        {
            Console.SetOut(original);
        }
    }

    /// <summary>Jim's station with one scanned group under an Evidence profile, backed up to the stick.</summary>
    private async Task<string> JimsBackupAsync()
    {
        Directory.CreateDirectory(Stick);
        Assert.Equal(0, (await RunAsync("scan", "--data-dir", Jim, "--group", Path.Combine(Evidence, "Dental"), "--source", "feeder")).Exit);
        using (var db = new FgScanner.Data.FgScannerDbContext(FgScanner.Data.DbBootstrapper.BuildOptions(Path.Combine(Jim, "fgscanner.db"))))
        {
            db.Profiles.Add(new FgScanner.Data.Profile { Name = "Evidence", BaseDirectory = Evidence });
            db.SaveChanges();
        }

        SqliteConnection.ClearAllPools();
        var (exit, output) = await RunAsync("station", "backup", "--data-dir", Jim, "--to", Stick, "--evidence", Evidence, "--station", "jim");
        Assert.True(exit == 0, output);
        return Directory.GetDirectories(Stick).Single();
    }

    private static SortedDictionary<string, string> Hashes(params string[] roots)
    {
        var all = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var root in roots.Where(Directory.Exists))
        {
            // The apply log records every attempt, refused ones included, by design.
            foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).Where(f => Path.GetFileName(f) != "station-apply.log"))
            {
                all[file] = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(file)));
            }
        }

        return all;
    }

    private static List<string> Paths(string database, string sql)
    {
        using var connection = new SqliteConnection($"Data Source={database};Mode=ReadOnly;Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        using var reader = command.ExecuteReader();
        var values = new List<string>();
        while (reader.Read())
        {
            values.Add(reader.GetString(0));
        }

        return values;
    }

    [Fact]
    public async Task A_backup_opens_as_a_test_copy_with_its_evidence_in_the_folder_named()
    {
        var backup = await JimsBackupAsync();
        var jimBefore = Hashes(Jim, Evidence);
        var (_, verified) = await RunAsync("station", "verify", backup);

        var (exit, output) = await RunAsync(
            "station", "apply", backup, "--data-dir", Workspace, "--test-copy", "--evidence", TestEvidence);

        Assert.True(exit == 0, output);
        Assert.Equal(verified.Split(" · ")[0], output.Trim().Split(" · ")[0]);
        Assert.Equal(
            Directory.GetFiles(Path.Combine(Evidence, "Dental")).Select(Path.GetFileName).Order(StringComparer.Ordinal),
            Directory.GetFiles(Path.Combine(TestEvidence, "Dental")).Select(Path.GetFileName).Order(StringComparer.Ordinal));
        var db = Path.Combine(Workspace, "fgscanner.db");
        Assert.All(Paths(db, "SELECT DirectoryPath FROM Groups"), p => Assert.StartsWith(TestEvidence + Path.DirectorySeparatorChar, p));
        Assert.Contains(TestEvidence, Paths(db, "SELECT BaseDirectory FROM Profiles WHERE BaseDirectory <> ''"));
        Assert.True(File.Exists(Path.Combine(Workspace, "test-copy.json")));
        Assert.Equal(jimBefore, Hashes(Jim, Evidence));
    }

    [Fact]
    public async Task A_test_copy_can_never_be_sent_or_returned()
    {
        var backup = await JimsBackupAsync();
        Assert.Equal(0, (await RunAsync("station", "apply", backup, "--data-dir", Workspace, "--test-copy", "--evidence", TestEvidence)).Exit);
        var transport = Directory.CreateDirectory(Path.Combine(_root, "to-jim")).FullName;

        var (exit, output) = await RunAsync("station", "pack", "--data-dir", Workspace, "--to", transport, "--evidence", TestEvidence);

        Assert.Equal(1, exit);
        Assert.Contains("test copy", output);
        Assert.Empty(Directory.GetDirectories(transport));
    }

    [Fact]
    public async Task A_test_copy_refuses_a_folder_that_already_holds_files()
    {
        var backup = await JimsBackupAsync();
        Directory.CreateDirectory(TestEvidence);
        File.WriteAllText(Path.Combine(TestEvidence, "keep.txt"), "Franz's");

        var (exit, output) = await RunAsync("station", "apply", backup, "--data-dir", Workspace, "--test-copy", "--evidence", TestEvidence);

        Assert.Equal(1, exit);
        Assert.Contains("already holds files", output);
        Assert.False(File.Exists(Path.Combine(Workspace, "fgscanner.db")));
    }

    [Fact]
    public async Task A_test_copy_refuses_a_workspace_that_already_has_a_station()
    {
        var backup = await JimsBackupAsync();
        Assert.Equal(0, (await RunAsync("station", "apply", backup, "--data-dir", Workspace, "--test-copy", "--evidence", TestEvidence)).Exit);
        var before = Hashes(Workspace, TestEvidence);

        var (exit, output) = await RunAsync(
            "station", "apply", backup, "--data-dir", Workspace, "--test-copy", "--evidence", TestEvidence + "-2");

        Assert.Equal(1, exit);
        Assert.Contains("already has a station", output);
        Assert.Equal(before, Hashes(Workspace, TestEvidence));
    }

    /// <summary>Revision C SR-1: the scans' folder must be apart from the workspace, or a pack could
    /// plant a file beside the database (a -wal SQLite replays, or the database itself).</summary>
    [Theory]
    [InlineData("same")]
    [InlineData("parent")]
    [InlineData("inside")]
    public async Task A_test_copy_refuses_a_scans_folder_that_overlaps_the_workspace(string how)
    {
        var backup = await JimsBackupAsync();
        var evidence = how switch
        {
            "same" => Workspace,
            "parent" => Path.Combine(_root, "both"),
            _ => Path.Combine(Workspace, "scans"),
        };
        var workspace = how == "parent" ? Path.Combine(_root, "both", "ws") : Workspace;

        var (exit, output) = await RunAsync("station", "apply", backup, "--data-dir", workspace, "--test-copy", "--evidence", evidence);

        Assert.Equal(1, exit);
        Assert.Contains("apart from the workspace", output);
        Assert.False(File.Exists(Path.Combine(workspace, "fgscanner.db")));
    }

    [Fact]
    public async Task A_test_copy_refuses_a_workspace_with_a_leftover_journal()
    {
        var backup = await JimsBackupAsync();
        Directory.CreateDirectory(Workspace);
        File.WriteAllText(Path.Combine(Workspace, "fgscanner.db-wal"), "planted");

        var (exit, output) = await RunAsync("station", "apply", backup, "--data-dir", Workspace, "--test-copy", "--evidence", TestEvidence);

        Assert.Equal(1, exit);
        Assert.Contains("already has a station", output);
    }

    /// <summary>Revision C SR-5: the code Franz read out is the copy that opens.</summary>
    [Fact]
    public async Task A_test_copy_refuses_a_code_that_is_not_the_one_checked()
    {
        var backup = await JimsBackupAsync();

        var (exit, output) = await RunAsync(
            "station", "apply", backup, "--data-dir", Workspace, "--test-copy", "--evidence", TestEvidence, "--expect-code", "0000-0000");

        Assert.Equal(1, exit);
        Assert.Contains("not the one whose code you checked", output);
        Assert.False(File.Exists(Path.Combine(Workspace, "fgscanner.db")));
    }

    [Fact]
    public async Task A_test_copy_needs_the_folder_named()
    {
        var backup = await JimsBackupAsync();

        var (exit, _) = await RunAsync("station", "apply", backup, "--data-dir", Workspace, "--test-copy");

        Assert.Equal(1, exit);
    }
}
