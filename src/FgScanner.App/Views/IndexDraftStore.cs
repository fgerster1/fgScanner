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

    /// <summary>Exported: every answer an export of this draft has carried — what the
    /// portal may hold beyond the seed, so a later change can withdraw it. Absent in
    /// drafts written before it existed.</summary>
    private sealed record DraftFile(
        string PackageId,
        string PackageChecksum,
        Dictionary<string, List<StagedAnswer>> Answers,
        Dictionary<string, List<StagedAnswer>>? Exported = null);

    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    private string PathFor(string packageId) => Path.Combine(directory, packageId + ".json");

    /// <summary>Synchronous on purpose: the caller surfaces a failure the
    /// moment the answer was staged, not on a later tick.</summary>
    public void Save(
        string packageId, string packageChecksum,
        IReadOnlyDictionary<string, IReadOnlyList<StagedAnswer>> snapshot,
        IReadOnlyDictionary<string, IReadOnlyList<StagedAnswer>>? exported = null)
    {
        Directory.CreateDirectory(directory);
        var draft = new DraftFile(packageId, packageChecksum,
            snapshot.ToDictionary(kv => kv.Key, kv => kv.Value.ToList(), StringComparer.Ordinal),
            exported?.ToDictionary(kv => kv.Key, kv => kv.Value.ToList(), StringComparer.Ordinal));
        var bytes = JsonSerializer.SerializeToUtf8Bytes(draft, WriteOptions);
        var path = PathFor(packageId);
        var (outcome, message) = new Core.Index.AtomicFileWriter()
            .WriteAsync(path, stream => stream.WriteAsync(bytes, 0, bytes.Length))
            .GetAwaiter().GetResult();
        if (outcome != Core.Index.ExportOutcome.Success)
        {
            // Never the writer's Locked text: it promises "the data is safe in
            // the database", and no database holds these answers.
            throw new IOException(
                $"the draft file {path} could not be written ({message}). Close any " +
                "program holding it; the next answer you make will save everything again.");
        }
    }

    /// <summary>What <see cref="Load"/> found.</summary>
    public enum LoadResult
    {
        /// <summary>A draft for exactly this package: restore it.</summary>
        Restored,

        /// <summary>No draft yet.</summary>
        None,

        /// <summary>A damaged or other-export draft, moved aside so a save
        /// cannot overwrite it; answers start fresh.</summary>
        SetAside,

        /// <summary>The draft exists but could not be read at all; saving
        /// now would overwrite it, so answering must wait for a reopen.</summary>
        Unreadable,
    }

    /// <summary>Loads the draft for this exact package. A draft that cannot
    /// be used is never merged and never overwritten: it is moved aside
    /// under a name that says which export it belonged to, and the notice
    /// says where it went.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<StagedAnswer>>? Load(
        string packageId, string packageChecksum, out LoadResult result, out string? notice,
        out IReadOnlyDictionary<string, IReadOnlyList<StagedAnswer>>? exported)
    {
        notice = null;
        exported = null;
        var path = PathFor(packageId);
        if (!File.Exists(path))
        {
            result = LoadResult.None;
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
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            result = LoadResult.Unreadable;
            notice = $"The saved draft {path} could not be read ({ex.Message}). Close " +
                "any program holding it and open the package again — answering is " +
                "paused so nothing overwrites it.";
            return null;
        }

        if (draft?.Answers is not null && draft.PackageChecksum == packageChecksum)
        {
            result = LoadResult.Restored;
            exported = draft.Exported?.ToDictionary(
                kv => kv.Key, kv => (IReadOnlyList<StagedAnswer>)kv.Value, StringComparer.Ordinal);
            return draft.Answers.ToDictionary(
                kv => kv.Key, kv => (IReadOnlyList<StagedAnswer>)kv.Value, StringComparer.Ordinal);
        }

        var why = draft?.Answers is null
            ? "is damaged"
            : "belongs to a different export of this package";
        var tag = draft?.PackageChecksum is { Length: >= 12 } old ? old[..12] : "damaged";
        var aside = Path.Combine(directory, string.Format(
            System.Globalization.CultureInfo.InvariantCulture,
            "{0}.set-aside-{1}-{2:yyyyMMddHHmmss}.json", packageId, tag, DateTime.UtcNow));
        try
        {
            File.Move(path, aside);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            result = LoadResult.Unreadable;
            notice = $"The saved draft {path} {why} and could not be moved aside " +
                $"({ex.Message}). Answering is paused so nothing overwrites it.";
            return null;
        }

        result = LoadResult.SetAside;
        notice = $"The saved draft for {packageId} {why}; it was set aside as {aside} " +
            "and answers start fresh.";
        return null;
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
