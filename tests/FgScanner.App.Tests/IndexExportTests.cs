using System.IO;
using System.Text.Json;
using FgScanner.App.Views;
using Xunit;

namespace FgScanner.App.Tests;

/// <summary>
/// SPEC-2026-008 AC-7. Export drains the staging through the widened
/// writer into &lt;parent&gt;\&lt;packageId&gt;-results.json: seq 1..N in seed
/// order, the appVersion pinned at open, the package checksum echoed,
/// decidedAt = the moment each answer was STAGED (export-time stamps
/// would make a partial-then-full send-back duplicate every earlier row
/// at the portal, whose dedupe key includes decidedAt). Unchanged
/// re-export is byte-identical.
/// </summary>
public sealed class IndexExportTests : IDisposable
{
    private static readonly DateTimeOffset StagedAt =
        new(2026, 10, 2, 9, 0, 0, TimeSpan.Zero);

    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "fgscanner-tests", Guid.NewGuid().ToString("N"));

    public IndexExportTests()
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

    private IndexViewModel CreateViewModel(string decider = "jim")
    {
        var vm = new IndexViewModel(draftDirectory: Path.Combine(_root, "drafts"))
        {
            AppVersion = "0.6.0-test",
            DeciderNameProvider = () => decider,
        };
        vm.Staging.Clock = () => StagedAt;
        return vm;
    }

    private string ResultsPath() => Path.Combine(_root, "PKG-0001-results.json");

    [Fact]
    public async Task Export_writes_results_beside_the_package_in_seed_order()
    {
        var vm = CreateViewModel();
        await vm.OpenPackageAsync(CopyOfGolden());
        // Staged out of seed order on purpose.
        vm.Staging.Stage("TOM99005", "key_flag", null, "true");
        vm.Staging.Stage("TOM99001", "subject", null, "tractor");
        vm.Staging.Stage("TOM99001", "person", "from", "P0002");

        vm.ExportResultsCommand.Execute(null);

        Assert.True(File.Exists(ResultsPath()), vm.ExportMessage);
        using var doc = JsonDocument.Parse(File.ReadAllBytes(ResultsPath()));
        var rootElement = doc.RootElement;
        Assert.Equal("PKG-0001", rootElement.GetProperty("packageId").GetString());
        Assert.Equal("0.6.0-test", rootElement.GetProperty("appVersion").GetString());
        Assert.Equal(vm.Package!.PackageChecksum,
            rootElement.GetProperty("packageChecksum").GetString());
        var answers = rootElement.GetProperty("answers").EnumerateArray().ToArray();
        Assert.Equal(3, answers.Length);
        // Seed order first (TOM99001 before TOM99005), staging order within.
        Assert.Equal(
            ["TOM99001", "TOM99001", "TOM99005"],
            answers.Select(a => a.GetProperty("anchorPageId").GetString()).ToArray());
        Assert.Equal(
            [1, 2, 3],
            answers.Select(a => a.GetProperty("seq").GetInt32()).ToArray());
        Assert.All(answers, a => Assert.Equal("jim", a.GetProperty("decidedBy").GetString()));
        Assert.All(answers, a => Assert.Equal("2026-10-02T09:00:00Z",
            a.GetProperty("decidedAt").GetString()));
        Assert.Contains("3", vm.ExportMessage);
    }

    [Fact]
    public async Task An_unchanged_re_export_is_byte_identical()
    {
        var vm = CreateViewModel();
        await vm.OpenPackageAsync(CopyOfGolden());
        vm.Staging.Stage("TOM99001", "subject", null, "tractor");

        vm.ExportResultsCommand.Execute(null);
        var first = File.ReadAllBytes(ResultsPath());
        vm.ExportResultsCommand.Execute(null);

        Assert.Equal(first, File.ReadAllBytes(ResultsPath()));
    }

    [Fact]
    public async Task Answers_survive_a_restart_with_their_original_decidedAt()
    {
        var package = CopyOfGolden();
        var vm = CreateViewModel();
        await vm.OpenPackageAsync(package);
        vm.Staging.Stage("TOM99001", "subject", null, "tractor");

        var reopened = CreateViewModel();
        reopened.Staging.Clock = () => StagedAt.AddDays(3);
        await reopened.OpenPackageAsync(package);
        reopened.ExportResultsCommand.Execute(null);

        using var doc = JsonDocument.Parse(File.ReadAllBytes(ResultsPath()));
        var answer = doc.RootElement.GetProperty("answers")[0];
        // The answer keeps the moment Jim DECIDED it, not the later export.
        Assert.Equal("2026-10-02T09:00:00Z", answer.GetProperty("decidedAt").GetString());
    }

    [Fact]
    public async Task Exporting_nothing_refuses_with_a_sentence_and_no_file()
    {
        var vm = CreateViewModel();
        await vm.OpenPackageAsync(CopyOfGolden());

        vm.ExportResultsCommand.Execute(null);

        Assert.False(File.Exists(ResultsPath()));
        Assert.NotNull(vm.ExportMessage);
        Assert.Contains("no answers", vm.ExportMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task An_empty_decider_name_refuses_before_any_file_is_written()
    {
        var vm = CreateViewModel(decider: " ");
        await vm.OpenPackageAsync(CopyOfGolden());
        vm.Staging.Stage("TOM99001", "subject", null, "tractor");

        vm.ExportResultsCommand.Execute(null);

        Assert.False(File.Exists(ResultsPath()));
        Assert.Contains("decider", vm.ExportMessage, StringComparison.OrdinalIgnoreCase);
    }
}
