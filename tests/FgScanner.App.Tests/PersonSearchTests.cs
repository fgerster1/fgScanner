using FgScanner.App.Views;
using FgScanner.Core.IndexPackages;
using Xunit;

namespace FgScanner.App.Tests;

/// <summary>
/// SPEC-2026-009 AC-3. The people search replaces a 1,312-entry pull-down: typing part of any
/// spelling — display name or alias, anywhere in it, capitals, accents and punctuation aside —
/// lists the people it could be. It only lists; picking is a separate act (AC-4).
/// </summary>
public sealed class PersonSearchTests
{
    private static PackagePerson P(string id, string name, params string[] aliases) =>
        new(id, name, "person", [], aliases);

    private static readonly PackagePerson[] People =
    [
        P("P0053", "Whitacre, Jason"),
        P("P0433", "Whitacre, Jason", "Jason Whitacre"),
        P("P0100", "Tomaiko, Judson O.", "Judd", "J. O. Tomaiko"),
        P("P0200", "Zoë Bélanger"),
        P("P0300", "Anderson, Ann", "Whit"),
    ];

    private static string[] Ids(PersonSearch.Result result) => [.. result.People.Select(p => p.Id)];

    [Fact]
    public void Part_of_a_name_anywhere_finds_every_person_it_could_be()
    {
        var ids = Ids(new PersonSearch(People).Filter("whit"));

        Assert.Equal(["P0300", "P0053", "P0433"], ids);
    }

    [Fact]
    public void An_alias_finds_its_person()
    {
        Assert.Equal(["P0433"], Ids(new PersonSearch(People).Filter("jason whitacre")));
        Assert.Equal(["P0100"], Ids(new PersonSearch(People).Filter("judd")));
    }

    [Fact]
    public void Capitals_accents_and_punctuation_do_not_matter()
    {
        var search = new PersonSearch(People);

        Assert.Equal(["P0200"], Ids(search.Filter("zoe belanger")));
        Assert.Equal(["P0053", "P0433"], Ids(search.Filter("WHITACRE JASON")));
        Assert.Equal(["P0100"], Ids(search.Filter("j.o. tomaiko")));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(",.")]
    public void Nothing_typed_lists_nobody(string typed)
    {
        var result = new PersonSearch(People).Filter(typed);

        Assert.Empty(result.People);
        Assert.Equal(0, result.Total);
    }

    [Fact]
    public void A_wide_search_is_capped_and_says_how_many_matched()
    {
        var many = Enumerable.Range(1, 450)
            .Select(i => P($"P{i:0000}", $"Smith, Person {i:000}"))
            .ToArray();

        var result = new PersonSearch(many).Filter("smith");

        Assert.Equal(PersonSearch.MaxShown, result.People.Count);
        Assert.Equal(450, result.Total);
        Assert.Equal("Smith, Person 001", result.People[0].DisplayName);
    }
}
