namespace FgScanner.Core.Index;

public enum IndexFormat
{
    Csv,
    Xlsx,
    Xml,
    Json,
}

public enum IndexFieldType
{
    Text,
    Date,
    Number,
    List,
}

/// <summary>
/// Whether a field is answered once per row or once per group. Batch fields exist because the
/// evidence station retyped Box and Operator on every page of a box; sticky only chained a value
/// onto new rows, so the first page still had to be typed and a correction had to be repeated.
/// </summary>
public enum FieldScope
{
    Row,
    Batch,
}

public sealed record IndexFieldDef(
    string Name, IndexFieldType Type, bool Required, FieldScope Scope = FieldScope.Row);

/// <summary>
/// One export row (= one document). Custom values are canonical strings: ISO dates, invariant numbers.
/// Sequence/PageId/Checksum/IsBlank exist so a copied group folder is self-contained for an external
/// importer (evidence export) — order, identity and integrity must not live only in the local DB.
/// </summary>
public sealed record IndexRow(
    string ImageName,
    string Ocred,
    double? OcrConfidence,
    string? AiDescription,
    string AiStatus,
    IReadOnlyDictionary<string, string?> CustomValues,
    int Sequence = 0,
    Guid PageId = default,
    string Checksum = "",
    bool IsBlank = false,
    string? OriginalChecksum = null,
    string? CapturedBy = null);

public sealed record IndexExportData(
    string GroupName,
    string GroupDirectory,
    string ProfileName,
    int SchemaVersion,
    string AppVersion,
    DateTime GeneratedUtc,
    IReadOnlyList<IndexFieldDef> Fields,
    IReadOnlyList<IndexFormat> Formats,
    IReadOnlyList<IndexRow> Rows)
{
    public char CsvDelimiter { get; init; } = ',';
}

public enum ExportOutcome
{
    Success,
    Locked,
    Error,
}

public sealed record FormatResult(IndexFormat Format, string Path, ExportOutcome Outcome, string? Message = null);

public sealed record ExportResult(IReadOnlyList<FormatResult> Results)
{
    public bool AllSucceeded => Results.All(r => r.Outcome == ExportOutcome.Success);
}
