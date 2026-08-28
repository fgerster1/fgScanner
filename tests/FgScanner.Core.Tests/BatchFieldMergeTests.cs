using FgScanner.Core.Index;
using Xunit;

namespace FgScanner.Core.Tests;

public class BatchFieldMergeTests
{
    private static readonly IReadOnlyList<IndexFieldDef> Schema =
    [
        new("Box", IndexFieldType.Text, Required: true, Scope: FieldScope.Batch),
        new("Title", IndexFieldType.Text, Required: false),
    ];

    [Fact]
    public void Batch_field_is_answered_by_the_group()
    {
        var merged = BatchFieldMerge.Effective(
            Schema,
            batchValues: new Dictionary<string, string?> { ["Box"] = "12" },
            documentValues: new Dictionary<string, string?> { ["Title"] = "Deed" });

        Assert.Equal("12", merged["Box"]);
        Assert.Equal("Deed", merged["Title"]);
    }

    /// <summary>
    /// A field that was row-scoped before leaves its old value behind in every document's JSON.
    /// If that copy could resurface, "one source of truth" would be a convention rather than a
    /// property, and rows would silently disagree with the group after a correction.
    /// </summary>
    [Fact]
    public void Stale_document_copy_of_a_batch_field_never_resurfaces()
    {
        var merged = BatchFieldMerge.Effective(
            Schema,
            batchValues: new Dictionary<string, string?> { ["Box"] = "13" },
            documentValues: new Dictionary<string, string?> { ["Box"] = "12", ["Title"] = "Deed" });

        Assert.Equal("13", merged["Box"]);
    }

    [Fact]
    public void Group_value_for_a_row_scoped_field_is_ignored()
    {
        var merged = BatchFieldMerge.Effective(
            Schema,
            batchValues: new Dictionary<string, string?> { ["Title"] = "wrong" },
            documentValues: new Dictionary<string, string?> { ["Title"] = "Deed" });

        Assert.Equal("Deed", merged["Title"]);
    }

    [Fact]
    public void A_batch_field_with_no_group_value_yields_no_entry()
    {
        var merged = BatchFieldMerge.Effective(
            Schema,
            batchValues: new Dictionary<string, string?>(),
            documentValues: new Dictionary<string, string?> { ["Title"] = "Deed" });

        Assert.False(merged.ContainsKey("Box"));
    }

    /// <summary>Only schema fields survive; a value left over from a deleted field is dropped.</summary>
    [Fact]
    public void Values_outside_the_schema_are_dropped()
    {
        var merged = BatchFieldMerge.Effective(
            Schema,
            batchValues: new Dictionary<string, string?> { ["Retired"] = "x" },
            documentValues: new Dictionary<string, string?> { ["AlsoRetired"] = "y" });

        Assert.Empty(merged);
    }

    [Fact]
    public void Field_names_match_case_insensitively()
    {
        var merged = BatchFieldMerge.Effective(
            Schema,
            batchValues: new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase) { ["box"] = "12" },
            documentValues: new Dictionary<string, string?>());

        Assert.Equal("12", merged["Box"]);
    }
}
