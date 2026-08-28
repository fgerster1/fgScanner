namespace FgScanner.Core.Index;

/// <summary>
/// Resolves the values one row shows. A batch-scoped field is answered by the group and never by
/// the row: a value the group owns must not be able to differ per row, and a copy left in a
/// document's JSON by an earlier row-scoped life must not resurface after a correction.
/// </summary>
public static class BatchFieldMerge
{
    public static IReadOnlyDictionary<string, string?> Effective(
        IReadOnlyList<IndexFieldDef> fields,
        IReadOnlyDictionary<string, string?> batchValues,
        IReadOnlyDictionary<string, string?> documentValues)
    {
        var merged = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var field in fields)
        {
            var source = field.Scope == FieldScope.Batch ? batchValues : documentValues;
            if (source.TryGetValue(field.Name, out var value))
            {
                merged[field.Name] = value;
            }
        }

        return merged;
    }
}
