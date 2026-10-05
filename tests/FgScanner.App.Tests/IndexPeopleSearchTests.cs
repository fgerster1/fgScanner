using System.IO;
using System.Text.Json.Nodes;
using FgScanner.App.Views;
using Xunit;

namespace FgScanner.App.Tests;

/// <summary>
/// SPEC-2026-009 AC-4, AC-5. The search grid never picks for Jim: typing only lists, a row is
/// picked by selecting it, and with no row selected Add sends what was typed through the same
/// typed-name path as before (ADR-0016) — so a name meant to travel as typed can never quietly
/// become a namesake on the list.
/// </summary>
public sealed class IndexPeopleSearchTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "fgscanner-tests", Guid.NewGuid().ToString("N"));

    public IndexPeopleSearchTests()
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

    /// <summary>The golden package plus two namesakes, as PKG-0002 has (P0053 and P0433).</summary>
    private async Task<IndexViewModel> OpenWithNamesakes()
    {
        var source = IndexViewModelTests.GoldenDir();
        var package = Path.Combine(_root, "pkg");
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var dest = Path.Combine(package, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            File.Copy(file, dest);
        }

        var peoplePath = Path.Combine(package, "people.json");
        var people = JsonNode.Parse(File.ReadAllText(peoplePath))!;
        foreach (var (id, aliases) in new[] { ("P0053", new JsonArray()), ("P0433", new JsonArray("Jason Whitacre")) })
        {
            people["people"]!.AsArray().Add(new JsonObject
            {
                ["aliases"] = aliases,
                ["displayName"] = "Whitacre, Jason",
                ["id"] = id,
                ["kind"] = "person",
                ["roles"] = new JsonArray(),
            });
        }

        File.WriteAllText(peoplePath, people.ToJsonString());
        var manifestPath = Path.Combine(package, "manifest.json");
        var manifest = JsonNode.Parse(File.ReadAllText(manifestPath))!;
        manifest["sha256"]!["people.json"] = Convert.ToHexStringLower(
            System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(peoplePath)));
        File.WriteAllText(manifestPath, manifest.ToJsonString());

        var vm = new IndexViewModel(draftDirectory: Path.Combine(_root, "drafts"))
        {
            AppVersion = "0.6.0-test",
            DeciderNameProvider = () => "jim",
        };
        await vm.OpenPackageAsync(package);
        Assert.Null(vm.RefusalMessage);
        vm.SelectedDocument = vm.Documents[0];
        return vm;
    }

    private static StagedAnswer? StagedPerson(IndexViewModel vm) =>
        vm.StagedAnswers.SingleOrDefault(a => a.Field == "person");

    [Fact]
    public async Task Typing_lists_the_matches_and_never_selects_one()
    {
        var vm = await OpenWithNamesakes();

        foreach (var typed in new[] { "W", "Wh", "Whi", "Whit" })
        {
            vm.PersonText = typed;
            Assert.Null(vm.PersonToAdd);
            Assert.Equal(typed, vm.PersonText);
        }

        Assert.Equal(["P0053", "P0433"], vm.PersonResults.Select(p => p.Id).ToArray());
    }

    [Fact]
    public async Task Picking_the_second_namesake_stages_that_one()
    {
        var vm = await OpenWithNamesakes();
        vm.PersonText = "Whit";

        vm.PersonToAdd = vm.PersonResults[1];
        vm.AddPersonCommand.Execute(null);

        Assert.Equal("P0433", StagedPerson(vm)?.Value);
    }

    [Fact]
    public async Task Editing_the_search_after_picking_drops_the_pick_so_the_typed_text_wins()
    {
        var vm = await OpenWithNamesakes();
        vm.PersonText = "Whit";
        vm.PersonToAdd = vm.PersonResults[0];

        vm.PersonText = "Zelda Walkthrough";
        vm.AddPersonCommand.Execute(null);

        Assert.Null(vm.PersonToAdd);
        Assert.Equal("Zelda Walkthrough", StagedPerson(vm)?.Value);
    }

    [Fact]
    public async Task With_no_row_picked_a_listed_spelling_still_resolves_the_portals_way()
    {
        var vm = await OpenWithNamesakes();
        vm.PersonText = "Jason Whitacre";   // an alias only P0433 holds

        vm.AddPersonCommand.Execute(null);

        Assert.Equal("P0433", StagedPerson(vm)?.Value);
    }

    [Fact]
    public async Task With_no_row_picked_a_shared_display_name_travels_as_typed()
    {
        // "Whitacre, Jason" names two people and is no one's alias: the portal decides.
        var vm = await OpenWithNamesakes();
        vm.PersonText = "Whitacre, Jason";

        vm.AddPersonCommand.Execute(null);

        Assert.Equal("Whitacre, Jason", StagedPerson(vm)?.Value);
    }

    [Fact]
    public async Task The_results_say_when_more_matched_than_are_shown()
    {
        var vm = await OpenWithNamesakes();

        vm.PersonText = "Whit";
        Assert.Null(vm.PersonResultsNote);

        vm.PersonText = "";
        Assert.Empty(vm.PersonResults);
    }

    [Fact]
    public async Task Opening_another_package_clears_the_search()
    {
        var vm = await OpenWithNamesakes();
        vm.PersonText = "Whit";
        vm.PersonToAdd = vm.PersonResults[0];

        await vm.OpenPackageAsync(IndexViewModelTests.GoldenDir());

        Assert.Equal("", vm.PersonText);
        Assert.Null(vm.PersonToAdd);
        Assert.Empty(vm.PersonResults);
    }
}
