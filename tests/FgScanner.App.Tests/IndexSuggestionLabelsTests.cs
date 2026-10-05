using System.IO;
using System.Text.Json.Nodes;
using FgScanner.App.Views;
using Xunit;

namespace FgScanner.App.Tests;

/// <summary>
/// SPEC-2026-009 AC-1, AC-2, AC-6, AC-7. The Index screen named people, subjects and doc types by
/// their ids, so Jim read "person → P0386" and had to know who that was. Rows now carry the label,
/// with the id beside it — two different people share the name "Whitacre, Jason" in PKG-0002, so a
/// name alone could not say which one the AI meant. What is staged and exported is unchanged.
/// </summary>
public sealed class IndexSuggestionLabelsTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "fgscanner-tests", Guid.NewGuid().ToString("N"));

    public IndexSuggestionLabelsTests()
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

    /// <summary>The golden package with TOM99005 carrying the suggestion shapes PKG-0002 really
    /// holds: people by id (one not on the list), a key flag of "yes", and fields the contract
    /// does not ask about. Re-hashed so only the edit under test differs.</summary>
    private string PackageWithRealSuggestionShapes()
    {
        var package = CopyOfGolden();
        var seedPath = Path.Combine(package, "seed.json");
        var seed = JsonNode.Parse(File.ReadAllText(seedPath))!;
        var document = seed["documents"]!.AsArray()
            .Single(d => (string?)d!["anchorPageId"] == "TOM99005")!;
        var suggestions = document["suggestions"]!.AsArray();
        long id = 100;
        void Add(string field, string? qualifier, string value, string? reason = null) =>
            suggestions.Add(new JsonObject
            {
                ["field"] = field,
                ["qualifier"] = qualifier,
                ["reasonQuote"] = reason,
                ["suggestionId"] = id++,
                ["value"] = value,
            });
        Add("person", "mentioned", "P0002");
        Add("person", "mentioned", "P9999");
        Add("subject", null, "tractor");
        Add("key_flag", null, "yes", "the Doctor has called her into the office");
        Add("key_flag", null, "maybe");
        Add("amount", null, "130000", "> $130,000.");
        Add("expense_category", null, "Pending Liabilities");
        Add("payee", null, "P0001");
        File.WriteAllText(seedPath, seed.ToJsonString());

        var manifestPath = Path.Combine(package, "manifest.json");
        var manifest = JsonNode.Parse(File.ReadAllText(manifestPath))!;
        manifest["sha256"]!["seed.json"] = Convert.ToHexStringLower(
            System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(seedPath)));
        File.WriteAllText(manifestPath, manifest.ToJsonString());
        return package;
    }

    private IndexViewModel CreateViewModel() => new(draftDirectory: Path.Combine(_root, "drafts"))
    {
        AppVersion = "0.6.0-test",
        DeciderNameProvider = () => "jim",
    };

    private async Task<IndexViewModel> OpenOnTom99005()
    {
        var vm = CreateViewModel();
        await vm.OpenPackageAsync(PackageWithRealSuggestionShapes());
        Assert.Null(vm.RefusalMessage);
        vm.SelectedDocument = vm.Documents.Single(d => d.AnchorPageId == "TOM99005");
        return vm;
    }

    private static SuggestionRow Row(IndexViewModel vm, string field, string value) =>
        vm.SuggestionRows.Single(r => r.Source.Field == field && r.Source.Value == value);

    [Fact]
    public async Task Labels_name_people_subjects_and_doc_types_from_the_package()
    {
        var vm = CreateViewModel();
        await vm.OpenPackageAsync(CopyOfGolden());
        var labels = new IndexLabels(vm.Package!);

        Assert.Equal("Tomaiko, Judson O.", labels.Person("P0001"));
        Assert.Equal("Farmall Cub tractor", labels.Subject("tractor"));
        Assert.Equal("Card or note", labels.DocType("card-note"));
        Assert.Null(labels.Person("P9999"));
        Assert.Null(labels.Subject("no-such-subject"));
        Assert.Null(labels.DocType("no-such-type"));
    }

    [Fact]
    public async Task A_person_suggestion_shows_the_name_with_the_id_beside_it()
    {
        var vm = await OpenOnTom99005();

        var row = Row(vm, "person", "P0002");
        Assert.Equal("Loepp, Thomas C.", row.ValueLabel);
        Assert.Equal("P0002", row.IdText);
        Assert.True(row.CanAccept);
    }

    [Fact]
    public async Task Subject_and_doc_type_suggestions_show_their_labels()
    {
        var vm = await OpenOnTom99005();

        Assert.Equal("Farmall Cub tractor", Row(vm, "subject", "tractor").ValueLabel);
        Assert.Equal("Card or note", Row(vm, "doc_type", "card-note").ValueLabel);
    }

    [Fact]
    public async Task A_person_id_not_on_the_list_shows_the_bare_id_and_says_so()
    {
        var vm = await OpenOnTom99005();

        var row = Row(vm, "person", "P9999");
        Assert.Equal("P9999", row.ValueLabel);
        Assert.Equal("not on the people list", row.InfoText);
    }

    [Fact]
    public async Task Suggestions_for_fields_the_batch_does_not_ask_are_shown_but_cannot_be_accepted()
    {
        var vm = await OpenOnTom99005();

        foreach (var (field, value) in new[] { ("amount", "130000"), ("expense_category", "Pending Liabilities"), ("payee", "P0001") })
        {
            var row = Row(vm, field, value);
            Assert.False(row.CanAccept);
            Assert.Equal("for information — not asked in this batch", row.InfoText);
        }

        // A payee is a person id too: Jim reads the name, not the code.
        Assert.Equal("Tomaiko, Judson O.", Row(vm, "payee", "P0001").ValueLabel);
    }

    [Fact]
    public async Task Accepting_a_key_document_suggestion_of_yes_ticks_key_document()
    {
        var vm = await OpenOnTom99005();

        vm.AcceptSuggestionCommand.Execute(Row(vm, "key_flag", "yes").Source);

        Assert.Null(vm.AnswerError);
        Assert.True(vm.KeyFlagChecked);
        var staged = Assert.Single(vm.StagedAnswers, a => a.Field == "key_flag");
        Assert.Equal("true", staged.Value);
    }

    [Fact]
    public async Task A_key_document_suggestion_that_is_not_yes_is_still_refused()
    {
        var vm = await OpenOnTom99005();

        vm.AcceptSuggestionCommand.Execute(Row(vm, "key_flag", "maybe").Source);

        Assert.NotNull(vm.AnswerError);
        Assert.DoesNotContain(vm.StagedAnswers, a => a.Field == "key_flag");
    }

    [Fact]
    public async Task Accepting_a_person_suggestion_still_stages_the_id_not_the_name()
    {
        var vm = await OpenOnTom99005();

        vm.AcceptSuggestionCommand.Execute(Row(vm, "person", "P0002").Source);

        var staged = Assert.Single(vm.StagedAnswers);
        Assert.Equal(("person", "mentioned", "P0002"), (staged.Field, staged.Qualifier, staged.Value));
    }

    [Fact]
    public async Task Staged_answers_show_the_name_with_the_id_beside_it()
    {
        var vm = await OpenOnTom99005();

        vm.AcceptSuggestionCommand.Execute(Row(vm, "person", "P0002").Source);

        var row = Assert.Single(vm.StagedAnswerRows);
        Assert.Equal("Loepp, Thomas C.", row.ValueLabel);
        Assert.Equal("P0002", row.IdText);
    }
}
