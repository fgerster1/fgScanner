using System.Globalization;
using System.Text;
using System.Text.Json;

namespace FgScanner.Core.Station;

public enum StationVerifyStatus
{
    Ok,
    NotOk,
    StillArriving,
}

public sealed record StationVerifyResult(
    StationVerifyStatus Status,
    string? Code,
    int FileCount,
    long TotalBytes,
    IReadOnlyList<string> Problems);

/// <summary>
/// Checks a pack against its manifest: every file present with its hash, nothing extra. A pack
/// is either exactly what was sent or it is refused whole — never patched by hand (§12).
/// </summary>
public static class StationManifestVerifier
{
    public static StationVerifyResult Verify(string packFolder)
    {
        var manifestPath = Path.Combine(packFolder, StationManifest.FileName);
        if (!File.Exists(manifestPath))
        {
            // The sender writes the manifest last, so its absence means the upload is still going.
            return new StationVerifyResult(StationVerifyStatus.StillArriving, null, 0, 0, []);
        }

        var bytes = File.ReadAllBytes(manifestPath);
        StationManifest manifest;
        try
        {
            manifest = StationManifest.Parse(bytes);
        }
        catch (JsonException ex)
        {
            return new StationVerifyResult(
                StationVerifyStatus.NotOk, null, 0, 0, [$"manifest unreadable: {ex.Message}"]);
        }

        var problems = new List<string>();
        var database = Path.Combine(packFolder, StationManifest.DatabaseFileName);
        long total = 0;
        if (!File.Exists(database))
        {
            problems.Add($"missing: {StationManifest.DatabaseFileName}");
        }
        else
        {
            total += new FileInfo(database).Length;
            if (StationManifestBuilder.Sha256Of(database) != manifest.DbSha256)
            {
                problems.Add($"changed: {StationManifest.DatabaseFileName}");
            }
        }

        foreach (var file in manifest.Files)
        {
            total += file.Size;
            var path = Path.Combine(packFolder, file.Rel);
            if (!File.Exists(path))
            {
                problems.Add($"missing: {file.Rel}");
            }
            else if (new FileInfo(path).Length != file.Size || StationManifestBuilder.Sha256Of(path) != file.Sha256)
            {
                problems.Add($"changed: {file.Rel}");
            }
        }

        var expected = manifest.Files.Select(f => f.Rel)
            .Concat([StationManifest.FileName, StationManifest.DatabaseFileName, StationManifest.VerifyLogFileName])
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        problems.AddRange(Directory.EnumerateFiles(packFolder, "*", StationManifestBuilder.AllFiles)
            .Select(p => StationManifestBuilder.RelativeOf(packFolder, p))
            .Where(rel => !expected.Contains(rel))
            .Order(StringComparer.Ordinal)
            .Select(rel => $"extra: {rel}"));

        return new StationVerifyResult(
            problems.Count == 0 ? StationVerifyStatus.Ok : StationVerifyStatus.NotOk,
            StationManifest.CodeOf(bytes),
            manifest.Files.Count + 1,
            total,
            problems);
    }

    /// <summary>The one line Jim or Franz reads out.</summary>
    public static string Summary(StationVerifyResult result) => result.Status switch
    {
        StationVerifyStatus.Ok =>
            $"OK {result.Code} · {result.FileCount.ToString("N0", CultureInfo.InvariantCulture)} files · {Size(result.TotalBytes)}",
        StationVerifyStatus.StillArriving => "NOT YET — the pack is still arriving (no manifest yet)",
        _ => $"NOT OK — {result.Problems[0]}",
    };

    /// <summary>Writes every problem beside the manifest, so the one-line summary can stay short.</summary>
    public static void WriteLog(string packFolder, StationVerifyResult result, DateTimeOffset when)
    {
        var log = new StringBuilder()
            .Append(StationManifest.FormatSentAt(when)).Append(' ').AppendLine(Summary(result));
        foreach (var problem in result.Problems)
        {
            log.AppendLine(problem);
        }

        var target = Path.Combine(packFolder, StationManifest.VerifyLogFileName);
        var temp = target + ".tmp";
        File.WriteAllText(temp, log.ToString());
        File.Move(temp, target, overwrite: true);
    }

    private static string Size(long bytes) => bytes switch
    {
        >= 1L << 30 => (bytes / (double)(1L << 30)).ToString("0.00", CultureInfo.InvariantCulture) + " GB",
        >= 1L << 20 => (bytes / (double)(1L << 20)).ToString("0.0", CultureInfo.InvariantCulture) + " MB",
        _ => bytes.ToString("N0", CultureInfo.InvariantCulture) + " bytes",
    };
}
