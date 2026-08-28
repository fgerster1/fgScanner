using FgScanner.Core.Index;
using FgScanner.Data;
using Xunit;

namespace FgScanner.Data.Tests;

public class BatchFieldColumnsTests
{
    /// <summary>
    /// Every field that existed before this phase must migrate as Row, or a schema the operator
    /// never touched would start answering from the group.
    /// </summary>
    [Fact]
    public void FieldDefinition_defaults_to_row_scope()
    {
        var field = new FieldDefinition { Name = "Title" };

        Assert.Equal(FieldScope.Row, field.Scope);
    }

    [Fact]
    public void Group_starts_with_an_empty_batch_bag()
    {
        var group = new Group { Name = "g", DirectoryPath = "d" };

        Assert.Equal("{}", group.BatchFieldsJson);
    }

    /// <summary>Null is "unknown provenance" and must stay distinguishable from an empty string.</summary>
    [Fact]
    public void Page_captured_by_starts_null()
    {
        var page = new Page { FileName = "a.jpg", Checksum = "abc" };

        Assert.Null(page.CapturedBy);
    }
}
