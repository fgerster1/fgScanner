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

public sealed record IndexFieldDef(string Name, IndexFieldType Type, bool Required);

/// <summary>One export row (= one document). Custom values are canonical strings: ISO dates, invariant numbers.</summary>
public sealed record IndexRow(
    string ImageName,
    string Ocred,
    string? AiDescription,
    string AiStatus,
    IReadOnlyDictionary<string, string?> CustomValues);

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
