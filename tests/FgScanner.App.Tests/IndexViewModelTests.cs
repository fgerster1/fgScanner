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

    [Fact]
    public async Task Documents_keep_the_seed_order_with_their_page_counts()
    {
        var vm = CreateViewModel();
        await vm.OpenPackageAsync(CopyOfGolden());

        // The portal exported them in priority order; reordering them here
        // would silently defeat the planner (SPEC-2026-008 AC-2).
        Assert.Equal(
            ["TOM99001", "TOM99003", "TOM99005", "TOM99007", "TOM99009", "TOM99011"],
            vm.Documents.Select(d => d.AnchorPageId).ToArray());
        Assert.All(vm.Documents, d => Assert.Equal(2, d.Pages.Count));
    }

    [Fact]
    public async Task Selecting_a_document_shows_its_first_page_and_its_suggestions()
    {
        var vm = CreateViewModel();
        var package = CopyOfGolden();
        await vm.OpenPackageAsync(package);

        vm.SelectedDocument = vm.Documents.Single(d => d.AnchorPageId == "TOM99005");

        Assert.Equal(
            Path.GetFullPath(Path.Combine(package, "images", "TOM99005.jpg")),
            Path.GetFullPath(vm.CurrentPageImagePath!));
        Assert.True(File.Exists(vm.CurrentPageImagePath));
        var suggestion = Assert.Single(vm.SelectedDocument.Suggestions);
        Assert.Equal("card-note", suggestion.Value);
        Assert.Contains("thinking of you", suggestion.ReasonQuote);
    }

    [Fact]
    public async Task Page_navigation_stays_within_the_selected_document()
    {
        var vm = CreateViewModel();
        await vm.OpenPackageAsync(CopyOfGolden());
        vm.SelectedDocument = vm.Documents[0];
        Assert.Equal("1 of 2", vm.PagePositionText);

        vm.NextPageCommand.Execute(null);
        Assert.Equal("2 of 2", vm.PagePositionText);
        Assert.Contains("TOM99002", vm.CurrentPageImagePath);

        // Past the end stays put; a viewer that wraps silently is how an
        // operator reads page 1 believing it is page 3.
        vm.NextPageCommand.Execute(null);
        Assert.Equal("2 of 2", vm.PagePositionText);

        vm.PreviousPageCommand.Execute(null);
        Assert.Equal("1 of 2", vm.PagePositionText);

        // Switching documents resets to that document's first page.
        vm.SelectedDocument = vm.Documents[1];
        Assert.Equal("1 of 2", vm.PagePositionText);
        Assert.Contains("TOM99003", vm.CurrentPageImagePath);
    }
}
