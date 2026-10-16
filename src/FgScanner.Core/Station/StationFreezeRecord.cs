using System.Text.Json;

namespace FgScanner.Core.Station;

/// <summary>
/// What the station looked like when its pack left (SPEC-SCANNER-2026-010 §08). Apply compares
/// the live database against <see cref="LiveDbSha256"/>: a mismatch means Jim worked while his data
/// was with Franz, and swapping the database would lose that work, so apply refuses (AC-5).
/// </summary>
public sealed record StationFreezeRecord(
    string Pack,
    string Code,
    string SentAt,
    string LiveDbSha256,
    string ManifestSha256)
{
    public const string FileName = "station-freeze.json";

    /// <summary>The sent manifest itself, kept beside the record so the next pack can be based on it.</summary>
    public const string SentManifestFileName = "station-sent-manifest.json";

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    public static StationFreezeRecord? Read(string stationRoot)
    {
        var path = Path.Combine(stationRoot, FileName);
        return File.Exists(path)
            ? JsonSerializer.Deserialize<StationFreezeRecord>(File.ReadAllBytes(path), Json)
            : null;
    }

    /// <summary>The manifest lands first, so a record never points at a manifest that is not there.</summary>
    public static void Write(string stationRoot, StationFreezeRecord record, byte[] manifestBytes)
    {
        StationPacker.WriteAtomically(Path.Combine(stationRoot, SentManifestFileName), manifestBytes);
        StationPacker.WriteAtomically(
            Path.Combine(stationRoot, FileName), JsonSerializer.SerializeToUtf8Bytes(record, Json));
    }
}
