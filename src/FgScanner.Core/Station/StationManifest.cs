using System.Globalization;
using System.Security.Cryptography;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FgScanner.Core.Station;

/// <summary>
/// What a station pack holds, file by file (SPEC-SCANNER-2026-010 §07). It is written last, so a
/// pack without one is still arriving; and it is hashed into the four-digit code Jim reads out on
/// Teams, so both ends can tell they are looking at the same pack without comparing file lists.
/// </summary>
public sealed record StationManifest(
    int Format,
    string Station,
    string SentAt,
    string AppVersion,
    string? LastMigration,
    string DbSha256,
    string EvidenceRoot,
    string? BasedOn,
    IReadOnlyList<StationManifestFile> Files,
    StationContent? BasedOnContent = null,
    IReadOnlyList<StationManifestReport>? Reports = null)
{
    public const int CurrentFormat = 1;

    public const string FileName = "station-manifest.json";

    public const string DatabaseFileName = "fgscanner.db";

    public const string VerifyLogFileName = "station-verify.log";

    public const string EvidenceFolder = "evidence";

    public const string TrashFolder = "trash";

    /// <summary>The only reports a pack carries beside the station's files (§07, Q-B8).</summary>
    public static readonly IReadOnlyList<string> ReportNames = ["changes.jsonl", "needs-jim.txt"];

    // Fixed newline and escaping: the code Jim reads out is a hash of these bytes, so the same
    // manifest must serialize identically on his laptop and on Franz's PC.
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true,
        NewLine = "\n",
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static string FormatSentAt(DateTimeOffset sentAt) =>
        sentAt.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    public byte[] ToJsonBytes() => JsonSerializer.SerializeToUtf8Bytes(this, Json);

    public static StationManifest Parse(byte[] json) =>
        JsonSerializer.Deserialize<StationManifest>(json, Json)
            ?? throw new JsonException("The manifest is empty.");

    /// <summary>The first eight hex digits of the manifest's SHA-256, as <c>xxxx-xxxx</c> — short
    /// enough to read aloud.</summary>
    public static string CodeOf(byte[] manifestBytes)
    {
        // Eight hex characters, read out before anything changes (Revision D, D3): four were
        // guessable in about 65,000 tries.
        var hash = HashOf(manifestBytes);
        return $"{hash[..4]}-{hash[4..8]}";
    }

    /// <summary>The manifest's full SHA-256 — what a later pack names as its <c>basedOn</c>.</summary>
    public static string HashOf(byte[] manifestBytes) =>
        Convert.ToHexStringLower(SHA256.HashData(manifestBytes));
}

/// <summary>One file of the station's final state. <see cref="Rel"/> uses forward slashes and
/// starts with <c>evidence/</c> or <c>trash/</c>, so the same manifest reads the same on any
/// machine. <see cref="From"/> is null when the file travels in this pack; otherwise the receiver
/// already holds these bytes at <see cref="From"/> (the same rel when unchanged, another when
/// renamed), which is how a pack ships only what the other side lacks.</summary>
public sealed record StationManifestFile(
    string Rel,
    long Size,
    string Sha256,
    string Kind,
    Guid? GroupId = null,
    Guid? PageId = null,
    string? DbChecksum = null,
    string? From = null);

/// <summary>A report at the pack's top level (a return's change log or needs-Jim sheet), hashed
/// like every file so the chain-of-custody record cannot be edited in transit unnoticed.</summary>
public sealed record StationManifestReport(string Name, long Size, string Sha256);
