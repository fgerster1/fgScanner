using System.Reflection;
using Xunit;

namespace FgScanner.App.Tests;

/// <summary>
/// App.xaml.cs reads this assembly's version into IndexingService.AppVersion, which is stamped
/// into every exported manifest.json and index.xml. With no &lt;Version&gt; set, .NET defaults it
/// to 1.0.0 and every export ever written claims to come from build "1.0.0" — the installers were
/// 0.1.0 through 0.3.1 and nothing recorded which one captured a page. For evidence work that is
/// a provenance hole, so the release number is pinned in Directory.Build.props.
/// </summary>
public sealed class AppVersionTests
{
    [Fact]
    public void The_app_assembly_carries_a_real_release_number()
    {
        var version = typeof(FgScanner.App.App).Assembly.GetName().Version;

        Assert.NotNull(version);
        Assert.NotEqual(new Version(1, 0, 0, 0), version);
        Assert.NotEqual(new Version(0, 0, 0, 0), version);
    }

    [Fact]
    public void Every_shipped_assembly_reports_the_same_release_number()
    {
        // The installer derives its version from the published exe, so a project that opted out of
        // the shared number would ship a payload disagreeing with the box it came in.
        var app = typeof(FgScanner.App.App).Assembly.GetName().Version;

        foreach (var assembly in (Assembly[])[
            typeof(FgScanner.Data.IndexingService).Assembly,
            typeof(FgScanner.Core.Index.IndexPayload).Assembly,
            typeof(FgScanner.Scanning.Editing.ImageEditor).Assembly,
            typeof(FgScanner.Ocr.TesseractRunner).Assembly,
            typeof(FgScanner.Ai.CredentialStore).Assembly])
        {
            Assert.Equal(app, assembly.GetName().Version);
        }
    }

    [Fact]
    public void The_version_the_updater_reads_is_the_bare_release_number()
    {
        // NetSparkle compares the appcast's "0.5.3" with the exe's informational version. The SDK
        // appends "+<commit>" to that by default, and NetSparkle ranks "0.5.3" above
        // "0.5.3+4f1360f…", so 0.5.3 offered itself as an update to 0.5.3 on every start.
        var app = typeof(FgScanner.App.App).Assembly;
        var informational = app.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

        Assert.Equal(app.GetName().Version?.ToString(3), informational);
    }
}
