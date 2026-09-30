using System.IO;
using FgScanner.App.Views;
using Xunit;

namespace FgScanner.App.Tests;

/// <summary>
/// SPEC-2026-008 AC-8 (Q4 as answered): "Remove package from this
/// computer" is enabled only after an export that covers the CURRENT
/// answers, asks an explicit in-section confirmation carrying the
/// counts, and on Yes removes the package folder and its draft. It is
/// the one irreversible act on this screen and it is never automatic.
/// </summary>
public sealed class IndexDeleteTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "fgscanner-tests", Guid.NewGuid().ToString("N"));

    public IndexDeleteTests()
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

    private string CopyOfGolden()
    {
        var source = IndexViewModelTests.GoldenDir();
        var target = Path.Combine(_root, "pkg");
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var dest = Path.Combine(target, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            File.Copy(file, dest);
        }

        return target;
    }

    private IndexViewModel CreateViewModel()
    {
        var vm = new IndexViewModel(draftDirectory: Path.Combine(_root, "drafts"))
        {
            AppVersion = "0.6.0-test",
            DeciderNameProvider = () => "jim",
        };
        return vm;
    }

    [Fact]
    public async Task Delete_arms_only_after_an_export_that_covers_the_answers()
    {
        var vm = CreateViewModel();
        await vm.OpenPackageAsync(CopyOfGolden());
        Assert.False(vm.DeleteEnabled);

        vm.Staging.Stage("TOM99001", "subject", null, "tractor");
        Assert.False(vm.DeleteEnabled);

        vm.ExportResultsCommand.Execute(null);
        Assert.True(vm.DeleteEnabled);

        // A post-export answer means the export no longer covers the
        // current state: disarmed until exported again.
        vm.Staging.Stage("TOM99005", "key_flag", null, "true");
        Assert.False(vm.DeleteEnabled);

        vm.ExportResultsCommand.Execute(null);
        Assert.True(vm.DeleteEnabled);
    }

    [Fact]
    public async Task Confirming_removes_the_package_folder_and_the_draft()
    {
        var package = CopyOfGolden();
        var vm = CreateViewModel();
        await vm.OpenPackageAsync(package);
        vm.Staging.Stage("TOM99001", "subject", null, "tractor");
        vm.ExportResultsCommand.Execute(null);

        vm.RequestDeleteCommand.Execute(null);
        Assert.NotNull(vm.PendingDeleteText);
        Assert.Contains("PKG-0001", vm.PendingDeleteText);
        Assert.Contains("6 document", vm.PendingDeleteText);

        vm.ConfirmDeleteCommand.Execute(null);

        Assert.False(Directory.Exists(package));
        Assert.False(File.Exists(Path.Combine(_root, "drafts", "PKG-0001.json")));
        Assert.Null(vm.Package);
        Assert.Null(vm.PendingDeleteText);
        // The exported results file SURVIVES — it is the work product.
        Assert.True(File.Exists(Path.Combine(_root, "PKG-0001-results.json")));
    }

    [Fact]
    public async Task Cancelling_removes_nothing()
    {
        var package = CopyOfGolden();
        var vm = CreateViewModel();
        await vm.OpenPackageAsync(package);
        vm.Staging.Stage("TOM99001", "subject", null, "tractor");
        vm.ExportResultsCommand.Execute(null);

        vm.RequestDeleteCommand.Execute(null);
        vm.CancelDeleteCommand.Execute(null);

        Assert.Null(vm.PendingDeleteText);
        Assert.True(Directory.Exists(package));
        Assert.True(File.Exists(Path.Combine(_root, "drafts", "PKG-0001.json")));
        Assert.NotNull(vm.Package);
    }
}
