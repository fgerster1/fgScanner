using FgScanner.Core.Naming;
using Xunit;

namespace FgScanner.Core.Tests;

public class NamingEngineTests
{
    private static readonly NamingContext Context = new()
    {
        Timestamp = new DateTime(2026, 8, 20, 9, 5, 7, DateTimeKind.Local),
        GroupName = "Invoices 2026",
        DocumentSequence = 12,
        PageSequence = 3,
        FieldValues = new Dictionary<string, string?>
        {
            ["Vendor"] = "Acme Corp",
            ["Slash"] = @"a/b\c:d",
        },
    };

    [Fact]
    public void Date_and_time_tokens_expand_zero_padded()
    {
        Assert.Equal(
            "2026-08-20 09.05.07",
            NamingEngine.Expand("$(YYYY)-$(MM)-$(DD) $(hh).$(mm).$(ss)", Context));
        Assert.Equal("26", NamingEngine.Expand("$(YY)", Context));
    }

    [Theory]
    [InlineData("$(n)", 1, "1")]
    [InlineData("$(nn)", 7, "07")]
    [InlineData("$(nnn)", 42, "042")]
    [InlineData("$(nnnn)", 42, "0042")]
    public void Counter_tokens_pad_to_token_length(string pattern, int counter, string expected) =>
        Assert.Equal(expected, NamingEngine.Expand(pattern, Context, counter));

    [Fact]
    public void Metadata_tokens_expand()
    {
        Assert.Equal("Invoices 2026_12_3", NamingEngine.Expand("$(group)_$(doc)_$(page)", Context));
        Assert.Equal("Acme Corp.pdf", NamingEngine.Expand("$(field:Vendor).pdf", Context));
    }

    [Fact]
    public void Field_values_are_slugified_for_windows() =>
        Assert.Equal("a-b-c-d", NamingEngine.Expand("$(field:Slash)", Context));

    [Fact]
    public void Unknown_field_expands_empty_but_unknown_token_passes_through()
    {
        Assert.Equal("", NamingEngine.Expand("$(field:Nope)", Context));
        Assert.Equal("$(bogus)", NamingEngine.Expand("$(bogus)", Context));
    }

    [Fact]
    public void Barcode_is_reserved_and_expands_empty() =>
        Assert.Equal("scan-.pdf", NamingEngine.Expand("scan-$(barcode).pdf", Context));

    [Fact]
    public void Collision_bumps_counter_when_pattern_has_one()
    {
        var taken = new HashSet<string> { "doc-001.pdf", "doc-002.pdf" };
        Assert.Equal("doc-003.pdf", NamingEngine.ExpandUnique("doc-$(nnn).pdf", Context, taken.Contains));
    }

    [Fact]
    public void Collision_without_counter_gets_numeric_suffix()
    {
        var taken = new HashSet<string> { "Invoices 2026.pdf", "Invoices 2026 (2).pdf" };
        Assert.Equal(
            "Invoices 2026 (3).pdf",
            NamingEngine.ExpandUnique("$(group).pdf", Context, taken.Contains));
    }
}
