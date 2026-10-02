using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace FgScanner.Core.IndexPackages;

/// <summary>A package opened from a zip: what the reader returned, and the
/// folder it was extracted to (where drafts and delete-after-confirm act).</summary>
public sealed record ZipOpenResult(IndexPackage Package, string PackageDirectory);

/// <summary>
/// Opens the one file the portal serves for a batch (JimsStuff SPEC-2026-007 §08.6, ADR-0015).
/// The zip is extracted into <c>&lt;extractRoot&gt;\&lt;packageId&gt;</c> and that folder is
/// opened through the UNCHANGED <see cref="PackageReader"/>, so every check the reader makes
/// (marker, version, every checksum, coherence) still runs on what was extracted — the zip is
/// transport, never trusted on its own.
/// <para>
/// Why not have Jim extract it: Windows lets you browse INTO a zip as if it were a folder, and
/// a folder picked there is not on disk, so the reader would refuse with "no manifest.json"
/// — a refusal that tells Jim nothing about what he did.
/// </para>
/// <para>
/// Extraction goes to a temporary sibling folder that is renamed into place only when every
/// entry landed inside it, so a refused or interrupted extraction leaves nothing to be
/// reused. Any entry naming a path outside the package (zip-slip) refuses the whole zip, in
/// the reader's own words for the same fault.
/// </para>
/// </summary>
public static partial class ZipPackageOpener
{
    [GeneratedRegex("^PKG-[0-9]{4}[0-9]*$")]
    private static partial Regex PackageIdPattern();

    public static ZipOpenResult Open(string zipPath, string extractRoot, string appVersion)
    {
        var packageId = ReadPackageId(zipPath);
        var target = Path.Combine(extractRoot, packageId);

        // Opening the same batch again (after a restart, say) reuses the folder — the reader
        // re-verifies every checksum on each open, so reuse never skips a check. A folder the
        // reader refuses (damaged on disk since) is replaced from the zip.
        if (Directory.Exists(target))
        {
            try
            {
                return new ZipOpenResult(PackageReader.Open(target, appVersion), target);
            }
            catch (PackageRefusedException)
            {
                Directory.Delete(target, recursive: true);
            }
        }

        Directory.CreateDirectory(extractRoot);
        var staging = Path.Combine(extractRoot, $"{packageId}.extracting-{Guid.NewGuid():N}");
        try
        {
            Extract(zipPath, staging);
            Directory.Move(staging, target);
        }
        catch
        {
            TryDelete(staging);
            throw;
        }

        try
        {
            return new ZipOpenResult(PackageReader.Open(target, appVersion), target);
        }
        catch (PackageRefusedException)
        {
            // Never leave a folder the reader refused: the next open would find it and try it
            // first, and a re-downloaded zip should simply replace it.
            TryDelete(target);
            throw;
        }
    }

    private static string ReadPackageId(string zipPath)
    {
        var name = Path.GetFileName(zipPath);
        try
        {
            using var zip = ZipFile.OpenRead(zipPath);
            var manifest = zip.GetEntry("manifest.json")
                ?? throw new PackageRefusedException(
                    $"no manifest.json in \"{name}\" — this is not an index package.");
            using var stream = manifest.Open();
            using var json = JsonDocument.Parse(stream);
            if (json.RootElement.ValueKind == JsonValueKind.Object
                && json.RootElement.TryGetProperty("packageId", out var id)
                && id.ValueKind == JsonValueKind.String
                && PackageIdPattern().IsMatch(id.GetString()!))
            {
                return id.GetString()!;
            }

            throw new PackageRefusedException(
                $"manifest.json in \"{name}\" is malformed — the package is damaged; download it again.");
        }
        catch (Exception ex) when (ex is InvalidDataException or JsonException or EndOfStreamException)
        {
            throw new PackageRefusedException(
                $"\"{name}\" is damaged — download it again.");
        }
        catch (IOException ex) when (ex is not FileNotFoundException and not DirectoryNotFoundException)
        {
            throw new PackageRefusedException(
                $"\"{name}\" cannot be read ({ex.Message}) — close other programs using it and try again.");
        }
    }

    private static void Extract(string zipPath, string staging)
    {
        var root = Path.GetFullPath(staging) + Path.DirectorySeparatorChar;
        try
        {
            using var zip = ZipFile.OpenRead(zipPath);
            foreach (var entry in zip.Entries)
            {
                var destination = Path.GetFullPath(Path.Combine(staging, entry.FullName));
                if (Path.IsPathRooted(entry.FullName)
                    || entry.FullName.Contains(':', StringComparison.Ordinal)
                    || !destination.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                {
                    throw new PackageRefusedException(
                        $"the zip lists \"{entry.FullName}\", which is outside the package — refusing to open it.");
                }

                if (entry.FullName.EndsWith('/'))
                {
                    Directory.CreateDirectory(destination);
                    continue;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                entry.ExtractToFile(destination, overwrite: false);
            }
        }
        catch (Exception ex) when (ex is InvalidDataException or EndOfStreamException)
        {
            throw new PackageRefusedException(
                $"\"{Path.GetFileName(zipPath)}\" is damaged — download it again.");
        }
    }

    private static void TryDelete(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best effort: a leftover staging folder is never opened (its name is not a
            // package id), and the next extraction uses a fresh one.
        }
    }
}
