using System.IO;
using FgScanner.App.Views;
using Xunit;

namespace FgScanner.App.Tests;

/// <summary>
/// SPEC-2026-008 AC-4. Every answer change autosaves OUTSIDE the package
/// folder (the package is contract law and stays byte-identical);
/// reopening the package restores every staged answer; a draft written
/// against a DIFFERENT export of the same package id is refused with its
/// own message, because answers to one export must not silently attach
/// to another. A failed save is surfaced immediately — an unsaved answer
/// Jim believes saved is this screen's worst outcome.
/// </summary>
public sealed class IndexDraftStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "fgscanner-tests", Guid.NewGuid().ToString("N"));

    private string DraftDir => Path.Combine(_root, "drafts");

    public IndexDraftStoreTests()
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

    private IndexViewModel CreateViewModel() =>
        new(draftDirectory: DraftDir) { AppVersion = "0.6.0-test" };

    [Fact]
    public async Task Answers_survive_a_restart_and_reopen()
    {
        var package = CopyOfGolden();
        var vm = CreateViewModel();
        await vm.OpenPackageAsync(package);
        vm.SelectedDocument = vm.Documents[0];
        vm.Staging.Stage("TOM99001", "subject", null, "tractor");
        vm.Staging.Stage("TOM99005", "doc_type", null, "card-note");

        // A new view model with the same draft directory is the restart.
        var reopened = CreateViewModel();
        await reopened.OpenPackageAsync(package);

        Assert.Single(reopened.Staging.ForDocument("TOM99001"));
        Assert.Single(reopened.Staging.ForDocument("TOM99005"));
        Assert.Equal(2, reopened.Staging.AnsweredDocumentCount);
    }

    [Fact]
    public async Task The_package_folder_is_never_written()
    {
        var package = CopyOfGolden();
        var before = Directory.EnumerateFiles(package, "*", SearchOption.AllDirectories)
            .OrderBy(p => p, StringComparer.Ordinal)
            .Select(p => (Path: p, Written: File.GetLastWriteTimeUtc(p)))
            .ToArray();

        var vm = CreateViewModel();
        await vm.OpenPackageAsync(package);
        vm.Staging.Stage("TOM99001", "subject", null, "tractor");

        var after = Directory.EnumerateFiles(package, "*", SearchOption.AllDirectories)
            .OrderBy(p => p, StringComparer.Ordinal)
            .Select(p => (Path: p, Written: File.GetLastWriteTimeUtc(p)))
            .ToArray();
        Assert.Equal(before, after);
        Assert.True(File.Exists(
            Path.Combine(DraftDir, "PKG-0001.json")), "the draft went somewhere else");
    }

    [Fact]
    public async Task A_draft_from_a_different_export_is_refused_not_merged()
    {
        var package = CopyOfGolden();
        var vm = CreateViewModel();
        await vm.OpenPackageAsync(package);
        vm.Staging.Stage("TOM99001", "subject", null, "tractor");

        // The portal re-exported PKG-0001: same id, different bytes. The
        // draft names the checksum it belongs to, so the mismatch shows.
        var draftPath = Path.Combine(DraftDir, "PKG-0001.json");
        File.WriteAllText(draftPath, File.ReadAllText(draftPath)
            .Replace(vm.Package!.PackageChecksum, new string('0', 64)));

        var reopened = CreateViewModel();
        await reopened.OpenPackageAsync(package);

        Assert.Empty(reopened.Staging.ForDocument("TOM99001"));
        Assert.NotNull(reopened.DraftNotice);
        Assert.Contains("different", reopened.DraftNotice);
    }

    [Fact]
    public async Task A_failed_save_is_surfaced_immediately()
    {
        var package = CopyOfGolden();
        var vm = CreateViewModel();
        await vm.OpenPackageAsync(package);

        // Make the draft location unusable: a FILE where the directory
        // must be.
        if (Directory.Exists(DraftDir))
        {
            Directory.Delete(DraftDir, recursive: true);
        }

        File.WriteAllText(DraftDir, "in the way");

        vm.Staging.Stage("TOM99001", "subject", null, "tractor");

        Assert.NotNull(vm.DraftError);
    }
}
