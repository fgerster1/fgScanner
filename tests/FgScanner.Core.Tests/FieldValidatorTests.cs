using FgScanner.Core.Index;
using Xunit;

namespace FgScanner.Core.Tests;

public class FieldValidatorTests
{
    private static readonly IndexFieldDef RequiredText = new("Vendor", IndexFieldType.Text, Required: true);
    private static readonly IndexFieldDef OptionalDate = new("Due", IndexFieldType.Date, Required: false);
    private static readonly IndexFieldDef OptionalNumber = new("Amount", IndexFieldType.Number, Required: false);
    private static readonly IndexFieldDef ListField = new("Category", IndexFieldType.List, Required: false);

    [Fact]
    public void Required_empty_fails() => Assert.NotNull(FieldValidator.Validate(RequiredText, ""));

    [Fact]
    public void Optional_empty_passes() => Assert.Null(FieldValidator.Validate(OptionalDate, null));

    [Theory]
    [InlineData("2026-08-20", true)]
    [InlineData("2026-2-3", false)]
    [InlineData("08/20/2026", false)]
    [InlineData("not a date", false)]
    public void Date_must_be_iso(string value, bool valid) =>
        Assert.Equal(valid, FieldValidator.Validate(OptionalDate, value) is null);

    [Theory]
    [InlineData("1234.5", true)]
    [InlineData("-0.5", true)]
    [InlineData("1,5", false)]
    [InlineData("abc", false)]
    public void Number_must_be_invariant(string value, bool valid) =>
        Assert.Equal(valid, FieldValidator.Validate(OptionalNumber, value) is null);

    [Fact]
    public void List_value_must_match_choices_case_insensitively()
    {
        var choices = new[] { "Utilities", "Rent" };
        Assert.Null(FieldValidator.Validate(ListField, "utilities", choices));
        Assert.NotNull(FieldValidator.Validate(ListField, "Groceries", choices));
    }

    [Fact]
    public void Tokens_expand()
    {
        var expanded = TokenExpander.Expand("$(group)-$(counter)", "Taxes", 7);
        Assert.Equal("Taxes-7", expanded);
        Assert.Matches(@"^\d{4}-\d{2}-\d{2}$", TokenExpander.Expand("$(today)", "g", 1));
        Assert.Equal(Environment.UserName, TokenExpander.Expand("$(user)", "g", 1));
    }
}
