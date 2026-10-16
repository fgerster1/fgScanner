using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using FgScanner.App.Services;
using Xunit;

namespace FgScanner.App.Tests;

/// <summary>
/// SPEC-SCANNER-2026-010 AC-11. Through 0.7.1, pressing Yes on an update did nothing: NetSparkle's
/// generated script waited 90 seconds for an app that never closed, then ran the download — saved
/// with no extension under <c>C:\Users\Franz Gerster\…</c> — by an unquoted path, which cmd split
/// at the space. The repaired updater starts the verified file itself, as an .exe, and then
/// closes; nothing here runs a real installer.
/// </summary>
public sealed class UpdateInstallerTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("fg-update").FullName;

    private readonly List<ProcessStartInfo> _started = [];

    private int _closed;

    private Func<ProcessStartInfo, Process?> _start;

    private readonly Queue<bool> _busy = new();

    private int _waits;

    private bool _signatureValid = true;

    private readonly List<string> _order = [];

    public UpdateInstallerTests()
    {
        _start = psi =>
        {
            _started.Add(psi);
            return null;
        };
    }

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

    private string UpdateFolder => Path.Combine(_root, "Franz Gerster", "AppData", "Local", "Temp", "FGScanner", "update");

    /// <summary>Where NetSparkle leaves the download: a GUID, no extension, under a profile with a space.</summary>
    private string Download()
    {
        var path = Path.Combine(_root, "Franz Gerster", "AppData", "Local", "Temp", "39362164-4dc1-42be-adf4-f0cc859a0ff0");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "installer bytes");
        return path;
    }

    private UpdateInstaller Installer() => new(
        psi =>
        {
            _order.Add("start");
            return _start(psi);
        },
        () => _closed++,
        () =>
        {
            _order.Add(File.Exists(Path.Combine(UpdateFolder, "fgscanner-0.8.1-win-x64.exe")) ? "busy? (copied)" : "busy? (not copied)");
            return _busy.Count > 0 && _busy.Dequeue();
        },
        UpdateFolder,
        _ =>
        {
            _waits++;
            return Task.CompletedTask;
        },
        path =>
        {
            _order.Add("verify " + Path.GetFileName(path));
            return _signatureValid;
        });

    [Fact]
    public async Task The_download_is_started_directly_as_an_exe_and_the_app_closes()
    {
        var installed = await Installer().InstallAsync(Download(), "0.8.1");

        Assert.True(installed);
        var psi = Assert.Single(_started);
        Assert.Equal(Path.Combine(UpdateFolder, "fgscanner-0.8.1-win-x64.exe"), psi.FileName);
        Assert.Equal("/VERYSILENT /NORESTART", psi.Arguments);
        Assert.True(psi.UseShellExecute);
        Assert.Equal("installer bytes", File.ReadAllText(psi.FileName));
        Assert.Equal(1, _closed);
    }

    [Fact]
    public async Task A_refused_administrator_prompt_keeps_the_app_open_and_removes_the_copy()
    {
        _start = _ => throw new Win32Exception(1223);

        var installed = await Installer().InstallAsync(Download(), "0.8.1");

        Assert.False(installed);
        Assert.Equal(0, _closed);
        Assert.False(File.Exists(Path.Combine(UpdateFolder, "fgscanner-0.8.1-win-x64.exe")));
    }

    [Fact]
    public async Task A_capture_in_hand_is_finished_before_the_app_closes()
    {
        _busy.Enqueue(true);
        _busy.Enqueue(true);

        await Installer().InstallAsync(Download(), "0.8.1");

        Assert.Equal(2, _waits);
        Assert.Single(_started);
        Assert.Equal(1, _closed);
    }

    /// <summary>Revision D, P9-8 and D4: the copy is made first, the capture in hand is waited
    /// out after it, and the exact file about to start is checked against the signature last.</summary>
    [Fact]
    public async Task The_copy_is_waited_on_then_verified_then_started()
    {
        _busy.Enqueue(true);

        await Installer().InstallAsync(Download(), "0.8.1");

        Assert.Equal(["busy? (copied)", "busy? (copied)", "verify fgscanner-0.8.1-win-x64.exe", "start"], _order);
    }

    /// <summary>Revision D, D4: a copy swapped in the temp folder after NetSparkle's check is
    /// caught by checking it again right before it runs with administrator rights.</summary>
    [Fact]
    public async Task A_copy_that_fails_its_signature_is_never_started()
    {
        _signatureValid = false;

        var installed = await Installer().InstallAsync(Download(), "0.8.1");

        Assert.False(installed);
        Assert.Empty(_started);
        Assert.Equal(0, _closed);
        Assert.False(File.Exists(Path.Combine(UpdateFolder, "fgscanner-0.8.1-win-x64.exe")));
    }

    [Theory]
    [InlineData("../../evil")]
    [InlineData("0.8.1\\..\\x")]
    public async Task A_strange_version_never_names_a_file_outside_the_update_folder(string version)
    {
        await Installer().InstallAsync(Download(), version);

        var psi = Assert.Single(_started);
        Assert.Equal(UpdateFolder, Path.GetDirectoryName(psi.FileName));
        Assert.EndsWith(".exe", psi.FileName);
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData("https://github.com/fgerster1/fgScanner/releases/download/v0.8.0/appcast.xml", false)]
    [InlineData("http://github.com/fgerster1/fgScanner/releases/download/v0.8.0/appcast.xml", true)]
    [InlineData("https://evil.example/fgerster1/fgScanner/releases/download/v0.8.0/appcast.xml", true)]
    [InlineData("https://github.com/someone-else/fgScanner/releases/download/v0.8.0/appcast.xml", true)]
    [InlineData("https://github.com/fgerster1/fgScanner/releases/download/../../../evil/appcast.xml", true)]
    public void The_appcast_override_only_names_this_repositorys_releases(string? value, bool usesDefault)
    {
        var url = UpdateService.ResolveAppcastUrl(value);

        Assert.Equal(usesDefault ? UpdateService.AppcastUrl : value, url);
    }
}
