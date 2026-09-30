using System.IO;
using FgScanner.App.Views;
using FgScanner.Core.IndexPackages;
using Xunit;

namespace FgScanner.App.Tests;

/// <summary>
/// SPEC-2026-008 AC-1/AC-2 (view-model half). The Index section opens a
/// package through the production reader and, when the reader refuses,
/// shows its message VERBATIM — the refusal texts are the contract's own
/// words (the newer-version update message especially), and rephrasing
/// them is how an operator ends up told the wrong thing at the worst
/// moment. A refusal never leaves the section unusable.
/// </summary>
public sealed class IndexViewModelTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "fgscanner-tests", Guid.NewGuid().ToString("N"));

    public IndexViewModelTests()
    {
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    /// <summary>Same anchor walk as Core.Tests' TestPaths (surgical rule:
    /// per-project copies stay local).</summary>
    private static string GoldenPackageDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "FgScanner.slnx")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        return Path.Combine(dir.FullName, "docs", "contract-vendored",
            "golden", "package", "PKG-0001");
    }

    private string CopyOfGolden()
    {
        var target = Path.Combine(_root, "pkg");
        foreach (var file in Directory.EnumerateFiles(
            GoldenPackageDir(), "*", SearchOption.AllDirectories))
        {
            var dest = Path.Combine(target, Path.GetRelativePath(GoldenPackageDir(), file));
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            File.Copy(file, dest);
        }

        return target;
    }

    private static IndexViewModel CreateViewModel() => new() { AppVersion = "0.6.0-test" };

    [Fact]
    public async Task Something_that_is_not_a_package_shows_the_readers_refusal_verbatim()
    {
        var stray = Path.Combine(_root, "stray");
        Directory.CreateDirectory(stray);
        string expected;
        try
        {
            PackageReader.Open(stray, appVersion: "0.6.0-test");
            expected = "(the reader accepted it)";
        }
        catch (PackageRefusedException ex)
        {
            expected = ex.Message;
        }

        var vm = CreateViewModel();
        await vm.OpenPackageAsync(stray);

        Assert.Equal(expected, vm.RefusalMessage);
        Assert.Null(vm.Package);
    }

    [Fact]
    public async Task A_newer_format_package_shows_the_update_message_word_for_word()
    {
        var newer = Path.Combine(_root, "newer");
        Directory.CreateDirectory(newer);
        File.WriteAllText(Path.Combine(newer, "manifest.json"),
            """{"formatVersion": 99, "indexPackage": 1}""");

        var vm = CreateViewModel();
        await vm.OpenPackageAsync(newer);

        Assert.Equal(PackageReader.UpdateMessage, vm.RefusalMessage);
    }

    [Fact]
    public async Task A_refusal_leaves_the_section_usable_and_a_good_open_clears_it()
    {
        var vm = CreateViewModel();
        await vm.OpenPackageAsync(Path.Combine(_root, "nowhere"));
        Assert.NotNull(vm.RefusalMessage);

        await vm.OpenPackageAsync(CopyOfGolden());

        Assert.Null(vm.RefusalMessage);
        Assert.NotNull(vm.Package);
        Assert.Equal("PKG-0001", vm.Package!.PackageId);
        Assert.Equal(6, vm.Package.Documents.Count);
    }

    [Fact]
    public async Task The_open_pins_the_app_version_for_provenance()
    {
        var vm = CreateViewModel();
        await vm.OpenPackageAsync(CopyOfGolden());
        Assert.Equal("0.6.0-test", vm.Package!.AppVersionAtOpen);
    }
}
