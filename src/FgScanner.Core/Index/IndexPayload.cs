using System.Text.Json;

namespace FgScanner.Core.Index;

/// <summary>
/// The manifest + rows object serialized both into index.json and into commit-hook webhook
/// bodies (PLAN prompt 10) — one shape, so scripts written against either stay interchangeable.
/// </summary>
public static class IndexPayload
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static object Build(IndexExportData data) => new
    {
        Manifest = ManifestBuilder.Build(data),
        Rows = data.Rows.Select(r => new
        {
            Group = data.GroupName,
            r.ImageName,
            OCRed = r.Ocred,
            AiDescription = r.AiDescription ?? "",
            r.AiStatus,
            Fields = data.Fields.ToDictionary(f => f.Name, f => r.CustomValues.GetValueOrDefault(f.Name) ?? ""),
        }),
    };

    public static string ToJson(IndexExportData data) => JsonSerializer.Serialize(Build(data), Options);

    public static Task WriteAsync(Stream stream, IndexExportData data) =>
        JsonSerializer.SerializeAsync(stream, Build(data), Options);
}
