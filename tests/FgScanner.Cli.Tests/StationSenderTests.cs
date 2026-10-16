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

    internal static (int Exit, string Output) PowerShell(params string[] arguments)
    {
        var start = new ProcessStartInfo("powershell.exe")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            StandardOutputEncoding = System.Text.Encoding.UTF8,
        };
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
