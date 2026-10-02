using System.IO;
using FgScanner.App.Views;
using Xunit;

namespace FgScanner.App.Tests;

/// <summary>
/// JimsStuff SPEC-2026-007 §12, decided by Franz 2026-10-02 (ADR-0016).
/// Jim can type a person the list does not hold — FG Scanner first matches
/// it against the spellings on the list the way the portal does
/// (case and punctuation ignored), and only an unmatched name travels as
/// typed, for the portal to hold as a proposal for Franz. And a page with
/// no date at all can be answered "undated", so it can count as fully
/// indexed instead of staying a gap forever.
/// </summary>
public sealed class IndexTypedNameAndUndatedTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "fgscanner-tests", Guid.NewGuid().ToString("N"));

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

    private async Task<IndexViewModel> OpenGolden()
    {
        var source = IndexViewModelTests.GoldenDir();
        var target = Path.Combine(_root, "pkg");
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var dest = Path.Combine(target, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            File.Copy(file, dest);
        }

        var vm = new IndexViewModel(draftDirectory: Path.Combine(_root, "drafts"))
        {
            AppVersion = "0.6.0-test",
            DeciderNameProvider = () => "jim",
        };
        await vm.OpenPackageAsync(target);
        vm.SelectedDocument = vm.Documents.First(d => d.AnchorPageId == "TOM99005");
        return vm;
    }

    private static StagedAnswer? StagedPerson(IndexViewModel vm) =>
        vm.StagedAnswers.SingleOrDefault(a => a.Field == "person");

    [Theory]
    [InlineData("judd")]                  // an alias, any case
    [InlineData("J.O. Tomaiko")]          // a merged person's alias, punctuation aside
    public async Task A_typed_name_on_the_list_becomes_that_person(string typed)
    {
        var vm = await OpenGolden();
        vm.PersonText = typed;
        vm.PersonQualifierToAdd = "to";

        vm.AddPersonCommand.Execute(null);

        Assert.Equal("P0001", StagedPerson(vm)?.Value);
    }

    [Fact]
    public async Task A_name_not_on_the_list_travels_as_typed()
    {
        var vm = await OpenGolden();
        vm.PersonText = "  Regina Kilgore ";
        vm.PersonQualifierToAdd = "mentioned";

        vm.AddPersonCommand.Execute(null);

        var staged = StagedPerson(vm);
        Assert.Equal(("mentioned", "Regina Kilgore"), (staged?.Qualifier, staged?.Value));
    }

    [Fact]
    public async Task An_id_shaped_name_off_the_list_is_refused_in_words()
    {
        var vm = await OpenGolden();
        vm.PersonText = "P0042";

        vm.AddPersonCommand.Execute(null);

        Assert.Null(StagedPerson(vm));
        Assert.False(string.IsNullOrEmpty(vm.AnswerError));
    }

    [Fact]
    public async Task Undated_is_offered_and_needs_no_date_typed()
    {
        var vm = await OpenGolden();
        Assert.Contains("undated", vm.DateQualifiers);
        vm.DateText = "";
        vm.SelectedDateQualifier = "undated";

        vm.SetDateCommand.Execute(null);

        var staged = vm.StagedAnswers.Single(a => a.Field == "date");
        Assert.Equal(("undated", "undated"), (staged.Qualifier, staged.Value));
    }

    [Fact]
    public async Task Undated_ignores_a_date_left_in_the_box()
    {
        var vm = await OpenGolden();
        vm.DateText = "2021-07-18";
        vm.SelectedDateQualifier = "undated";

        vm.SetDateCommand.Execute(null);

        Assert.Equal("undated", vm.StagedAnswers.Single(a => a.Field == "date").Value);
    }
}
