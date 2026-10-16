using System.Text.Json;

namespace FgScanner.Core.Station;

/// <summary>
/// The state both sides last shared (SPEC-SCANNER-2026-010 §07): written when a pack leaves and
/// again when one is applied. The next pack is based on <see cref="SentManifestFileName"/>, and
/// <see cref="Content"/> is what the other side's apply compares against — a difference means
/// someone worked while the data was away, and swapping the database would lose that work (AC-5).
/// </summary>
public sealed record StationFreezeRecord(
    string Pack,
    string Code,
    string SentAt,
    string LiveDbSha256,
    string ManifestSha256,
    StationContent? Content = null)
{
    public const string FileName = "station-freeze.json";

    /// <summary>The sent manifest itself, kept beside the record so the next pack can be based on it.</summary>
    public const string SentManifestFileName = "station-sent-manifest.json";

    /// <summary>A copy of the database the 0.5.4 sender sent. That sender cannot read SQLite, so
    /// apply works out <see cref="Content"/> from this copy instead (Revision D, P10-3).</summary>
    public const string SentDatabaseFileName = "station-sent.db";

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
