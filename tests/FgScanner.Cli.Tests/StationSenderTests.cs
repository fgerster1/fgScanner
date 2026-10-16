using System.Diagnostics;
using FgScanner.Core.Station;
using Xunit;

namespace FgScanner.Cli.Tests;

/// <summary>
/// Jim's first send runs on 0.5.4, which has no station verbs, so a PowerShell port of
/// <c>station pack</c> does it (SPEC-SCANNER-2026-010 §03 item 4, §11). The two must agree byte
/// for byte, or the code Jim reads out would not match the one Franz's verify prints. Run under
/// Windows PowerShell 5.1, which is what Jim's laptop has.
/// </summary>
public sealed class StationSenderTests : IDisposable
{
    private static readonly DateTimeOffset SentAt = new(2026, 10, 16, 9, 5, 0, TimeSpan.FromHours(-4));

    private readonly string _root = Directory.CreateTempSubdirectory("fg-sender").FullName;

    private string Station => Path.Combine(_root, "FGScanner");

    private string Evidence => Path.Combine(_root, "Evidence-Scans");

    public StationSenderTests()
    {
        Write(Path.Combine(Station, "fgscanner.db"), "database bytes");
        Write(Path.Combine(Station, "trash", "a12bc3c9", "scan_00263.jpg"), "trashed");
        Write(Path.Combine(Evidence, "Dental", "scan_00001.jpg"), "jpeg one");
        Write(Path.Combine(Evidence, "Dental", "originals", "scan_00001.jpg"), "jpeg one before");
        Write(Path.Combine(Evidence, "Dental", "scan_00001.md"), "# page");
        Write(Path.Combine(Evidence, "Dental", "index.json"), "{}");
        Write(Path.Combine(Evidence, "Toms' records , Trust", "scan_00002.jpg"), "jpeg two");
        Write(Path.Combine(Evidence, "Zoë", "scan_00003.jpg"), "jpeg three");
        var hidden = Path.Combine(Evidence, "Dental", "desktop.ini");
        Write(hidden, "[.ShellClassInfo]");
        File.SetAttributes(hidden, FileAttributes.Hidden | FileAttributes.System);
    }

    public void Dispose()
    {
        try
        {
            foreach (var file in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories))
            {
                File.SetAttributes(file, FileAttributes.Normal);
            }

            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private static void Write(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    internal static string RepoFile(params string[] parts)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine([dir.FullName, .. parts]);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException(Path.Combine(parts));
    }

    internal static (int Exit, string Output) PowerShell(params string[] arguments) =>
        PowerShellWith(new Dictionary<string, string>(), arguments);

    internal static (int Exit, string Output) PowerShellWith(Dictionary<string, string> environment, params string[] arguments)
    {
        var start = new ProcessStartInfo("powershell.exe")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            StandardOutputEncoding = System.Text.Encoding.UTF8,
        };
        foreach (var (name, value) in environment)
        {
            start.Environment[name] = value;
        }

        foreach (var argument in (string[])["-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", .. arguments])
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, output);
    }

    private (int Exit, string Output) Send(string to) => PowerShell(
        "-File", RepoFile("build", "station", "Send-ToFranz-0.5.4.ps1"),
        "-StationRoot", Station, "-EvidenceRoot", Evidence, "-To", to,
        "-SentAt", SentAt.ToString("o", System.Globalization.CultureInfo.InvariantCulture), "-AppVersion", "0.5.4", "-NoAppCheck");

    [Fact]
    public void The_054_sender_packs_byte_for_byte_what_the_station_verb_packs()
    {
        var byScript = Directory.CreateDirectory(Path.Combine(_root, "to-franz")).FullName;
        var (exit, output) = Send(byScript);
        Assert.True(exit == 0, output);
        var scriptPack = Directory.GetDirectories(byScript).Single();
        var sentManifest = File.ReadAllBytes(Path.Combine(Station, StationFreezeRecord.SentManifestFileName));

        var byVerb = Directory.CreateDirectory(Path.Combine(_root, "to-franz-csharp")).FullName;
        var verb = new StationPacker(() => false, File.Copy, _ => long.MaxValue).Pack(new StationPackRequest(
            new StationSource("jim", Path.Combine(Station, "fgscanner.db"), Evidence, Path.Combine(Station, "trash"),
                "0.5.4", "20260914210756_AddFieldLengthAndMemo", null, SentAt),
            Station, byVerb, null, "send", 0));

        Assert.Null(verb.Problem);
        Assert.Equal("jim-2026-10-16-0905-send", Path.GetFileName(scriptPack));
        Assert.Equal(
            System.Text.Encoding.UTF8.GetString(File.ReadAllBytes(Path.Combine(verb.PackFolder!, StationManifest.FileName))),
            System.Text.Encoding.UTF8.GetString(File.ReadAllBytes(Path.Combine(scriptPack, StationManifest.FileName))));
        var verified = StationManifestVerifier.Verify(scriptPack);
        Assert.Equal(StationVerifyStatus.Ok, verified.Status);
        Assert.StartsWith($"OK {verified.Code} - leave the laptop on", output.Trim());
        Assert.Equal(File.ReadAllBytes(Path.Combine(scriptPack, StationManifest.FileName)), sentManifest);
        Assert.Equal(File.ReadAllBytes(Path.Combine(Station, "fgscanner.db")),
            File.ReadAllBytes(Path.Combine(Station, StationFreezeRecord.SentDatabaseFileName)));
    }

    [Theory]
    [InlineData("recovery", "unsaved scans")]
    [InlineData("journal", "not closed cleanly")]
    public void The_054_sender_refuses_a_station_not_at_rest_and_leaves_nothing(string state, string reason)
    {
        if (state == "recovery")
        {
            Write(Path.Combine(Station, "recovery", "s1", "page-00001.jpg"), "unsaved");
        }
        else
        {
            Write(Path.Combine(Station, "fgscanner.db-journal"), "half");
        }

        var to = Directory.CreateDirectory(Path.Combine(_root, "to-franz")).FullName;
        var (exit, output) = Send(to);

        Assert.Equal(1, exit);
        Assert.StartsWith("NOT OK -", output.Trim());
        Assert.Contains(reason, output);
        Assert.Empty(Directory.EnumerateFileSystemEntries(to));
    }

    /// <summary>Revision D, D5: the sender takes FG Scanner's own lock; while the app holds it the
    /// send is refused and nothing is written.</summary>
    [Fact]
    public void The_054_sender_refuses_while_the_app_holds_its_lock()
    {
        var to = Directory.CreateDirectory(Path.Combine(_root, "to-franz")).FullName;
        using var app = new Mutex(initiallyOwned: true, "FGScanner.SingleInstance", out var ours);
        Assert.True(ours, "another FG Scanner is running on this machine; close it to run this test");
        try
        {
            var (exit, output) = PowerShell(
                "-File", RepoFile("build", "station", "Send-ToFranz-0.5.4.ps1"),
                "-StationRoot", Station, "-EvidenceRoot", Evidence, "-To", to, "-AppVersion", "0.5.4");

            Assert.Equal(1, exit);
            Assert.Contains("FG Scanner is open", output);
            Assert.Empty(Directory.EnumerateFileSystemEntries(to));
        }
        finally
        {
            app.ReleaseMutex();
        }
    }

    /// <summary>Re-review R-7: the other sender tests skip the lock (the developer's own FG Scanner
    /// may be open); this one takes it for real and must leave it free afterwards.</summary>
    [Fact]
    public void The_054_sender_takes_the_apps_lock_and_lets_it_go()
    {
        var to = Directory.CreateDirectory(Path.Combine(_root, "to-franz")).FullName;

        var (exit, output) = PowerShell(
            "-File", RepoFile("build", "station", "Send-ToFranz-0.5.4.ps1"),
            "-StationRoot", Station, "-EvidenceRoot", Evidence, "-To", to, "-AppVersion", "0.5.4");

        Assert.True(exit == 0, output);
        using var after = new Mutex(initiallyOwned: true, "FGScanner.SingleInstance", out var free);
        Assert.True(free, "the sender left FG Scanner's lock held");
        after.ReleaseMutex();
    }

    /// <summary>Re-review R-7, Revision D D6: a send is based on the last shared baseline only when
    /// that baseline recorded its rows; after a refused first update everything goes again.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Send_my_scans_is_based_on_the_last_update_only_when_its_rows_were_recorded(bool recorded)
    {
        var app = Path.Combine(_root, "app");
        Directory.CreateDirectory(Path.Combine(app, "station"));
        Directory.CreateDirectory(Path.Combine(app, "cli"));
        File.Copy(RepoFile("build", "station", "Send-ToFranz.ps1"), Path.Combine(app, "station", "Send-ToFranz.ps1"));
        var drive = Directory.CreateDirectory(Path.Combine(_root, "drive", "to-franz")).Parent!.FullName;
        File.WriteAllText(Path.Combine(app, "station", "drive-folder.txt"), drive);
        var stationData = Directory.CreateDirectory(Path.Combine(_root, "appdata", "FGScanner")).FullName;
        File.WriteAllText(Path.Combine(stationData, "station-sent-manifest.json"), "{}");
        File.WriteAllText(Path.Combine(stationData, "station-freeze.json"),
            recorded ? "{\n  \"content\": {\n    \"sha256\": \"ab\"\n  }\n}" : "{\n  \"content\": null\n}");
        StandInCli(Path.Combine(app, "cli", "fgscanner.exe"));

        var (exit, output) = PowerShellWith(
            new Dictionary<string, string> { ["APPDATA"] = Path.Combine(_root, "appdata") },
            "-File", Path.Combine(app, "station", "Send-ToFranz.ps1"));

        Assert.True(exit == 0, output);
        var arguments = File.ReadAllText(Path.Combine(app, "cli", "args.txt"));
        Assert.Equal(recorded, arguments.Contains("--based-on", StringComparison.Ordinal));
        Assert.Equal(!recorded, output.Contains("Sending everything again", StringComparison.Ordinal));
    }

    /// <summary>A console program standing in for fgscanner.exe: it records its arguments.</summary>
    private static void StandInCli(string exe)
    {
        var source = Path.Combine(Path.GetDirectoryName(exe)!, "standin.cs");
        File.WriteAllText(source, """
            class StandIn
            {
                static void Main(string[] args)
                {
                    var here = System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetEntryAssembly().Location);
                    System.IO.File.WriteAllText(System.IO.Path.Combine(here, "args.txt"), string.Join("|", args));
                    System.Console.WriteLine("OK 1234-5678");
                }
            }
            """);
        var (exit, output) = PowerShell("-Command",
            $"Add-Type -TypeDefinition ([IO.File]::ReadAllText('{source}')) -OutputAssembly '{exe}' -OutputType ConsoleApplication");
        Assert.True(exit == 0 && File.Exists(exe), output);
    }

    [Fact]
    public void The_054_sender_refuses_another_version()
    {
        var to = Directory.CreateDirectory(Path.Combine(_root, "to-franz")).FullName;

        var (exit, output) = PowerShell(
            "-File", RepoFile("build", "station", "Send-ToFranz-0.5.4.ps1"),
            "-StationRoot", Station, "-EvidenceRoot", Evidence, "-To", to, "-AppVersion", "0.7.1");

        Assert.Equal(1, exit);
        Assert.Contains("this sender is for FG Scanner 0.5.4", output);
    }

    /// <summary>Revision D, D7: with two returns waiting the shortcut installs neither — a second
    /// one is either a superseded pack or one that should not be there.</summary>
    [Fact]
    public void Install_Franzs_update_refuses_when_more_than_one_return_waits()
    {
        var app = Directory.CreateDirectory(Path.Combine(_root, "app", "station")).FullName;
        File.Copy(RepoFile("build", "station", "Install-FranzUpdate.ps1"), Path.Combine(app, "Install-FranzUpdate.ps1"));
        var drive = Path.Combine(_root, "drive");
        Directory.CreateDirectory(Path.Combine(drive, "to-jim", "franz-2026-10-16-1405-return"));
        Directory.CreateDirectory(Path.Combine(drive, "to-jim", "franz-2026-10-16-1405-2-return"));
        File.WriteAllText(Path.Combine(app, "drive-folder.txt"), drive);

        var (exit, output) = PowerShell("-File", Path.Combine(app, "Install-FranzUpdate.ps1"));

        Assert.Equal(1, exit);
        Assert.Contains("more than one update is waiting", output);
    }

    /// <summary>The desktop shortcuts' scripts run under Jim's Windows PowerShell 5.1; a parse
    /// error there would only show on his screen.</summary>
    [Theory]
    [InlineData("Send-ToFranz.ps1")]
    [InlineData("Install-FranzUpdate.ps1")]
    [InlineData("Send-ToFranz-0.5.4.ps1")]
    public void The_station_scripts_parse_in_Windows_PowerShell(string script)
    {
        var path = RepoFile("build", "station", script);

        var (exit, output) = PowerShell("-Command",
            $"$e = $null; [void][System.Management.Automation.Language.Parser]::ParseFile('{path}', [ref]$null, [ref]$e); " +
            "if ($e.Count) { $e | ForEach-Object { $_.ToString() }; exit 1 }");

        Assert.True(exit == 0, output);
    }
}
