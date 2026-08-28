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
        // Both bags are rewrapped rather than queried as given: a caller's TryGetValue uses
        // whatever comparer its dictionary carries, so without this the helper matches names
        // case-insensitively for one call site and ordinally for the next.
        var batch = CaseInsensitive(batchValues);
        var document = CaseInsensitive(documentValues);

        var merged = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var field in fields)
        {
            var source = field.Scope == FieldScope.Batch ? batch : document;
            if (source.TryGetValue(field.Name, out var value))
            {
                merged[field.Name] = value;
            }
        }

        return merged;
    }

    // Copied key by key rather than through the copy constructor, which throws on a bag holding
    // two keys that differ only in case — an export is the wrong place to discover that.
    private static Dictionary<string, string?> CaseInsensitive(IReadOnlyDictionary<string, string?> bag)
    {
        var copy = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in bag)
        {
            copy[key] = value;
        }

        return copy;
    }
}
