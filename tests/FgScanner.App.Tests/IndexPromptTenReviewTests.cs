using System.IO;
using System.Text.Json.Nodes;
using FgScanner.App.Views;
using Xunit;

namespace FgScanner.App.Tests;

/// <summary>
/// JimsStuff SPEC-2026-007 Prompt 10 code-review findings against the zip
/// open (ADR-0015) and the typed-name / undated answers (ADR-0016), one test
/// per finding a headless test can reach.
/// </summary>
public sealed class IndexPromptTenReviewTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "fgscanner-tests", Guid.NewGuid().ToString("N"));

    private string DraftDir => Path.Combine(_root, "drafts");

    public IndexPromptTenReviewTests()
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

    private string CopyOfGolden(string name = "pkg")
    {
        var source = IndexViewModelTests.GoldenDir();
        var target = Path.Combine(_root, name);
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var dest = Path.Combine(target, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            File.Copy(file, dest);
        }

        return target;
    }

    /// <summary>Edit one package file in a copy and re-hash it in the
    /// manifest, so only the edit under test differs.</summary>
    private static void EditFile(string package, string file, Action<JsonNode> edit)
    {
        var path = Path.Combine(package, file);
        var node = JsonNode.Parse(File.ReadAllText(path))!;
        edit(node);
        File.WriteAllText(path, node.ToJsonString());
        var manifestPath = Path.Combine(package, "manifest.json");
        var manifest = JsonNode.Parse(File.ReadAllText(manifestPath))!;
        manifest["sha256"]![file] = Convert.ToHexStringLower(
            System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path)));
        File.WriteAllText(manifestPath, manifest.ToJsonString());
    }

    private static void AddPerson(string package, string id, string displayName, params string[] aliases) =>
        EditFile(package, "people.json", people => people["people"]!.AsArray().Add(new JsonObject
        {
            ["aliases"] = new JsonArray([.. aliases.Select(a => (JsonNode)JsonValue.Create(a)!)]),
            ["displayName"] = displayName,
            ["id"] = id,
            ["kind"] = "person",
            ["roles"] = new JsonArray(),
        }));

    private IndexViewModel CreateViewModel() => new(draftDirectory: DraftDir)
    {
        AppVersion = "0.6.0-test",
        DeciderNameProvider = () => "jim",
    };

    private async Task<IndexViewModel> Open(string package, string anchor = "TOM99005")
    {
        var vm = CreateViewModel();
        await vm.OpenPackageAsync(package);
        Assert.Null(vm.RefusalMessage);
        vm.SelectedDocument = vm.Documents.First(d => d.AnchorPageId == anchor);
        return vm;
    }

    private static StagedAnswer? StagedPerson(IndexViewModel vm) =>
        vm.StagedAnswers.SingleOrDefault(a => a.Field == "person");

    // --- F1: a dropdown pick stages exactly the picked person ---------------

    [Fact]
    public async Task A_picked_namesake_is_staged_not_the_first_one_with_that_name()
    {
        var package = CopyOfGolden();
        AddPerson(package, "P0003", "Loepp, Thomas C.");
        var vm = await Open(package);
        // The editable combo writes the picked item's DisplayName into its text.
        vm.PersonToAdd = vm.People.Single(p => p.Id == "P0003");
        vm.PersonText = "Loepp, Thomas C.";

        vm.AddPersonCommand.Execute(null);

        Assert.Equal("P0003", StagedPerson(vm)?.Value);
    }

    // --- F7: what an earlier export left on the portal is withdrawn ---------

    private (string? Field, string? Qualifier, string? Value)[] ExportedRows(string anchor = "TOM99005")
    {
        using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllBytes(
            Path.Combine(_root, "PKG-0001-results.json")));
        return doc.RootElement.GetProperty("answers").EnumerateArray()
            .Where(a => a.GetProperty("anchorPageId").GetString() == anchor)
            .Select(a => (a.GetProperty("field").GetString(),
                a.GetProperty("qualifier").GetString(), a.GetProperty("value").GetString()))
            .ToArray();
    }

    private static void StageDate(IndexViewModel vm, string qualifier, string date)
    {
        vm.SelectedDateQualifier = qualifier;
        vm.DateText = date;
        vm.SetDateCommand.Execute(null);
        Assert.Null(vm.AnswerError);
    }

    [Fact]
    public async Task A_qualifier_changed_after_an_export_withdraws_the_exported_slot()
    {
        var vm = await Open(CopyOfGolden());
        StageDate(vm, "about", "2021-07-01");
        vm.ExportResultsCommand.Execute(null);

        StageDate(vm, "exact", "2021-07-18");
        vm.ExportResultsCommand.Execute(null);

        Assert.Equal([("date", "about", ""), ("date", "exact", "2021-07-18")], ExportedRows());
    }

    [Fact]
    public async Task Turning_an_exported_date_undated_withdraws_it_even_after_a_restart()
    {
        var package = CopyOfGolden();
        var first = await Open(package);
        StageDate(first, "exact", "2021-07-18");
        first.ExportResultsCommand.Execute(null);

        var second = await Open(package);
        StageDate(second, "undated", "");
        second.ExportResultsCommand.Execute(null);

        Assert.Equal([("date", "exact", ""), ("date", "undated", "undated")], ExportedRows());
    }

    [Fact]
    public async Task Unticking_an_exported_key_flag_stages_a_withdrawal()
    {
        var vm = await Open(CopyOfGolden());
        vm.KeyFlagChecked = true;
        vm.ExportResultsCommand.Execute(null);

        vm.KeyFlagChecked = false;

        var staged = Assert.Single(vm.StagedAnswers);
        Assert.Equal(("key_flag", ""), (staged.Field, staged.Value));
        vm.ExportResultsCommand.Execute(null);
        Assert.Equal([("key_flag", null, "")], ExportedRows());
    }

    [Fact]
    public async Task Re_ticking_a_portal_flag_after_its_withdrawal_was_exported_stages_it_again()
    {
        var package = CopyOfGolden();
        EditFile(package, "seed.json", seed => seed["documents"]!.AsArray()
            .Single(d => (string?)d!["anchorPageId"] == "TOM99005")!["decisions"]!.AsArray()
            .Add(new JsonObject { ["field"] = "key_flag", ["qualifier"] = null, ["value"] = "true" }));
        var vm = await Open(package);
        vm.KeyFlagChecked = false;
        vm.ExportResultsCommand.Execute(null);

        vm.KeyFlagChecked = true;

        var staged = Assert.Single(vm.StagedAnswers);
        Assert.Equal(("key_flag", "true"), (staged.Field, staged.Value));
    }

    [Fact]
    public async Task Removing_an_exported_doc_type_chip_stages_its_withdrawal()
    {
        var vm = await Open(CopyOfGolden());
        vm.SelectedDocType = vm.ActiveDocTypes[0];
        vm.ExportResultsCommand.Execute(null);

        vm.RemoveStagedAnswerCommand.Execute(vm.StagedAnswers.Single());

        var staged = Assert.Single(vm.StagedAnswers);
        Assert.Equal(("doc_type", ""), (staged.Field, staged.Value));
    }

    // --- F3: a typed name resolves only the way the portal resolves it ------

    private async Task<string?> StageTyped(string package, string typed)
    {
        var vm = await Open(package);
        vm.PersonText = typed;
        vm.AddPersonCommand.Execute(null);
        Assert.Null(vm.AnswerError);
        return StagedPerson(vm)?.Value;
    }

    [Fact]
    public async Task A_display_name_that_is_no_spelling_travels_as_typed()
    {
        // The portal's resolve_alias reads its alias table only; a display
        // name that is not also a spelling is a question for Franz there.
        Assert.Equal("Tomaiko, Judson O.",
            await StageTyped(CopyOfGolden(), "Tomaiko, Judson O."));
    }

    [Fact]
    public async Task A_spelling_two_listed_people_share_travels_as_typed()
    {
        var package = CopyOfGolden();
        AddPerson(package, "P0003", "Lee, Ann", "Ann Lee");
        AddPerson(package, "P0004", "Lee, Ann (2)", "ann  lee.");

        Assert.Equal("Ann Lee", await StageTyped(package, "Ann Lee"));
    }

    [Fact]
    public async Task A_spelling_beyond_plain_ascii_travels_as_typed()
    {
        // Python's \w, \s and lower() differ from .NET's outside printable
        // ASCII (combining marks, dotted I), so no local match can promise
        // the portal's answer there.
        var package = CopyOfGolden();
        AddPerson(package, "P0003", "Muller, Zoe", "Zoë Müller");

        Assert.Equal("Zoë Müller", await StageTyped(package, "Zoë Müller"));
    }
}
