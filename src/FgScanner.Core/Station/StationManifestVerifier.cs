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
/// Checks a pack against its manifest: every file that travels present with its hash, nothing
/// extra. A pack is either exactly what was sent or it is refused whole — never patched by hand
/// (§12). Files the receiver already holds (<see cref="StationManifestFile.From"/>) are not in the
/// pack; apply checks them against the receiver's own copies.
/// </summary>
public static class StationManifestVerifier
{
    public static StationVerifyResult Verify(string packFolder) => Verify(packFolder, out _);

    /// <summary>Also hands back the manifest bytes that were verified, so apply acts on exactly
    /// those — a shared folder can change the file between a check and a second read.</summary>
    internal static StationVerifyResult Verify(string packFolder, out byte[]? manifestBytes)
    {
        manifestBytes = null;
        var manifestPath = Path.Combine(packFolder, StationManifest.FileName);
        if (!File.Exists(manifestPath))
        {
            // The sender writes the manifest last, so its absence means the upload is still going.
            return new StationVerifyResult(StationVerifyStatus.StillArriving, null, 0, 0, []);
        }

        var bytes = File.ReadAllBytes(manifestPath);
        manifestBytes = bytes;
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

        // JSON with holes parses; a null where every later step expects a value would be a crash
        // on Jim's screen instead of NOT OK (Revision D, P10-10).
        if (manifest.Files is null || manifest.DbSha256 is null || manifest.EvidenceRoot is null
            || manifest.Files.Any(f => f is null || f.Rel is null || f.Sha256 is null || f.Kind is null)
            || (manifest.Reports ?? []).Any(r => r is null || r.Name is null || r.Sha256 is null))
        {
            return new StationVerifyResult(StationVerifyStatus.NotOk, null, 0, 0, ["manifest unreadable: a required field is missing"]);
        }

        if (ShapeProblem(packFolder, manifest) is { } refusal)
        {
            return new StationVerifyResult(StationVerifyStatus.NotOk, StationManifest.CodeOf(bytes), 0, 0, [refusal]);
        }

        var shipped = manifest.Files.Where(f => f.From is null).ToList();
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

        foreach (var file in shipped)
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

        foreach (var report in manifest.Reports ?? [])
        {
            total += report.Size;
            var path = Path.Combine(packFolder, report.Name);
            if (!File.Exists(path))
            {
                problems.Add($"missing: {report.Name}");
            }
            else if (new FileInfo(path).Length != report.Size || StationManifestBuilder.Sha256Of(path) != report.Sha256)
            {
                problems.Add($"changed: {report.Name}");
            }
        }

        var expected = shipped.Select(f => f.Rel)
            .Concat((manifest.Reports ?? []).Select(r => r.Name))
            .Concat([StationManifest.FileName, StationManifest.DatabaseFileName, StationManifest.VerifyLogFileName])
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        List<string> present;
        try
        {
            present = [.. StationManifestBuilder.FilesUnder(packFolder)];
        }
        catch (InvalidDataException ex)
        {
            return new StationVerifyResult(StationVerifyStatus.NotOk, StationManifest.CodeOf(bytes), 0, 0, [ex.Message]);
        }

        problems.AddRange(present
            .Select(p => StationManifestBuilder.RelativeOf(packFolder, p))
            .Where(rel => !expected.Contains(rel))
            .Order(StringComparer.Ordinal)
            .Select(rel => $"extra: {rel}"));

        return new StationVerifyResult(
            problems.Count == 0 ? StationVerifyStatus.Ok : StationVerifyStatus.NotOk,
            StationManifest.CodeOf(bytes),
            shipped.Count + 1,
            total,
            problems);
    }

    /// <summary>
    /// The manifest comes through a shared folder and apply writes where it points, so a rel that
    /// could resolve outside <c>evidence/</c> or <c>trash/</c> refuses the pack before any file
    /// is opened — a refused pack is re-sent, never trimmed.
    /// </summary>
    private static string? ShapeProblem(string packFolder, StationManifest manifest)
    {
        if (manifest.Format != StationManifest.CurrentFormat)
        {
            return $"manifest format {manifest.Format} is not one this version reads ({StationManifest.CurrentFormat})";
        }

        var root = Path.GetFullPath(packFolder).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in manifest.Files)
        {
            foreach (var rel in file.From is null ? [file.Rel] : new[] { file.Rel, file.From })
            {
                if (!WellFormed(root, rel))
                {
                    return $"manifest names a file outside the pack: {rel}";
                }
            }

            if (!seen.Add(file.Rel))
            {
                return $"manifest lists a file twice: {file.Rel}";
            }
        }

        foreach (var report in manifest.Reports ?? [])
        {
            if (!StationManifest.ReportNames.Contains(report.Name, StringComparer.Ordinal))
            {
                return $"manifest names a file outside the pack: {report.Name}";
            }

            if (!seen.Add(report.Name))
            {
                return $"manifest lists a file twice: {report.Name}";
            }
        }

        return null;
    }

    private static bool WellFormed(string root, string rel)
    {
        var segments = rel.Split('/');
        return segments.Length >= 2
            && segments[0] is StationManifest.EvidenceFolder or StationManifest.TrashFolder
            && segments.All(s => s.Length > 0 && s != "." && s != "..")
            && rel.IndexOfAny(['\\', ':']) < 0
            && Path.GetFullPath(Path.Combine(root, rel)).StartsWith(root, StringComparison.OrdinalIgnoreCase);
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
