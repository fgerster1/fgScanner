using System.IO;
using System.Text.Json;

namespace FgScanner.App.Views;

/// <summary>
/// Persists the staged answers OUTSIDE the package folder (SPEC-2026-008
/// AC-4): one JSON file per package id under the app's draft directory,
/// written atomically through the house AtomicFileWriter after every
/// change. The draft carries the package checksum it belongs to — a
/// re-export of the same id is a DIFFERENT package, and answers to one
/// must never silently attach to another.
/// </summary>
public sealed class IndexDraftStore(string directory)
{
    public static string DefaultDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "FGScanner", "index-drafts");

    private sealed record DraftFile(
        string PackageId,
        string PackageChecksum,
        Dictionary<string, List<StagedAnswer>> Answers);

    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    private string PathFor(string packageId) => Path.Combine(directory, packageId + ".json");

    /// <summary>Synchronous on purpose: the caller surfaces a failure the
    /// moment the answer was staged, not on a later tick.</summary>
    public void Save(
        string packageId, string packageChecksum,
        IReadOnlyDictionary<string, IReadOnlyList<StagedAnswer>> snapshot)
    {
        Directory.CreateDirectory(directory);
        var draft = new DraftFile(packageId, packageChecksum,
            snapshot.ToDictionary(kv => kv.Key, kv => kv.Value.ToList(), StringComparer.Ordinal));
        var bytes = JsonSerializer.SerializeToUtf8Bytes(
            draft, WriteOptions);
        var (outcome, message) = new Core.Index.AtomicFileWriter()
            .WriteAsync(PathFor(packageId), stream => stream.WriteAsync(bytes, 0, bytes.Length))
            .GetAwaiter().GetResult();
        if (outcome != Core.Index.ExportOutcome.Success)
        {
            throw new IOException(message ?? "the draft could not be saved");
        }
    }

    /// <summary>Loads the draft for this exact package. Returns the
    /// snapshot, or null with <paramref name="notice"/> set when there is
    /// no usable draft (absent: silent; unreadable or for a different
    /// export: said out loud, never merged, never deleted).</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<StagedAnswer>>? Load(
        string packageId, string packageChecksum, out string? notice)
    {
        notice = null;
        var path = PathFor(packageId);
        if (!File.Exists(path))
        {
            return null;
        }

        DraftFile? draft;
        try
        {
            draft = JsonSerializer.Deserialize<DraftFile>(File.ReadAllBytes(path));
        }
        catch (JsonException)
        {
            draft = null;
        }

        if (draft?.Answers is null)
        {
            notice = $"The saved draft at {path} is damaged and was left untouched; " +
                "answers start fresh.";
            return null;
        }

        if (draft.PackageChecksum != packageChecksum)
        {
            notice = $"A saved draft for {packageId} belongs to a different export of " +
                "this package and was left untouched; answers start fresh.";
            return null;
        }

        return draft.Answers.ToDictionary(
            kv => kv.Key, kv => (IReadOnlyList<StagedAnswer>)kv.Value, StringComparer.Ordinal);
    }

    public void Delete(string packageId)
    {
        var path = PathFor(packageId);
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }
}
