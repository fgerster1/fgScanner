using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace FgScanner.Core.IndexPackages;

/// <summary>
/// Opens an index package folder, refusing before anything opens: marker,
/// then formatVersion, then every checksum, then shape and coherence — only
/// a package that passed all of it is handed to a caller. The refusal
/// contract is total: NOTHING in a package folder may crash this class; any
/// damage, malformation or incoherence ends in a PackageRefusedException
/// with an operator-facing sentence. Core JSON files are read ONCE — the
/// bytes that were hashed are the bytes that are parsed, so a sync client
/// swapping a file mid-open cannot put unverified content in front of Jim.
/// </summary>
public static class PackageReader
{
    /// <summary>The one formatVersion this build reads (contract README: N).</summary>
    public const int KnownFormatVersion = 1;

    /// <summary>Verbatim from docs/contract-vendored/README.md — the schemas' law.
    /// Only for versions ABOVE the known one: a version below it cannot be
    /// fixed by updating, so it refuses as damage instead.</summary>
    public const string UpdateMessage =
        "This package needs a newer FG Scanner — close and reopen the app to update.";

    private static readonly string[] CoreFiles =
        ["seed.json", "people.json", "subjects.json", "doctypes.json"];

    /// <summary>
    /// <paramref name="appVersion"/> is pinned here, at open time, and echoed
    /// into results.json — an auto-update mid-batch cannot lie about which
    /// build showed Jim the pages. It must be a real version string: an empty
    /// pin would fail the portal's schema only after Jim answered the batch.
    /// </summary>
    public static IndexPackage Open(string packageDirectory, string appVersion)
    {
        if (string.IsNullOrWhiteSpace(appVersion))
        {
            throw new ArgumentException("appVersion must be the running build's version string");
        }

        var manifestPath = Path.Combine(packageDirectory, "manifest.json");
        if (!File.Exists(manifestPath))
        {
            throw new PackageRefusedException(
                $"no manifest.json in \"{packageDirectory}\" — this is not an index package.");
        }

        var manifestBytes = ReadOrRefuse(manifestPath, "manifest.json");
        using var manifest = ParseOrRefuse(manifestBytes, "manifest.json");
        if (manifest.RootElement.ValueKind != JsonValueKind.Object
            || !manifest.RootElement.TryGetProperty("indexPackage", out var marker)
            || marker.ValueKind != JsonValueKind.Number
            || !marker.TryGetInt32(out var markerValue) || markerValue != 1)
        {
            throw new PackageRefusedException(
                $"\"{packageDirectory}\" is not an index package — its manifest carries no indexPackage marker.");
        }

        // manifest.json is the one file no checksum protects (it is the
        // root of the hashes), so parseable-but-malformed damage must end
        // in the same operator refusal as any other damage — never a raw
        // exception's crash dialog.
        if (!manifest.RootElement.TryGetProperty("formatVersion", out var format)
            || format.ValueKind != JsonValueKind.Number
            || !format.TryGetInt32(out var formatVersion))
        {
            throw MalformedFile("manifest.json");
        }

        if (formatVersion > KnownFormatVersion)
        {
            throw new PackageRefusedException(UpdateMessage);
        }

        if (formatVersion < KnownFormatVersion)
        {
            // "Close and reopen to update" would be false advice with no
            // exit — no released exporter ever emitted this version.
            throw MalformedFile("manifest.json");
        }

        if (!manifest.RootElement.TryGetProperty("packageId", out var packageIdEl)
            || packageIdEl.ValueKind != JsonValueKind.String
            || GetStringOrRefuse(packageIdEl, "manifest.json") is not { } packageId
            || !Regex.IsMatch(packageId, "^PKG-[0-9]{4}[0-9]*$")
            || !manifest.RootElement.TryGetProperty("vocabularyVersion", out var vocabulary)
            || vocabulary.ValueKind != JsonValueKind.Number
            || !vocabulary.TryGetInt32(out var vocabularyVersion)
            || vocabularyVersion < 1
            || !manifest.RootElement.TryGetProperty("docCount", out var docCountEl)
            || docCountEl.ValueKind != JsonValueKind.Number
            || !docCountEl.TryGetInt32(out var docCount))
        {
            throw MalformedFile("manifest.json");
        }

        var (listed, coreBytes) = VerifyChecksums(packageDirectory, manifest);

        // A manifest listing only some files is schema-valid (additive
        // formats may add files), but the four core files are this
        // version's package: unlisted means unverified content in front of
        // Jim, and VerifyChecksums has already proven every listed file
        // exists and matches.
        foreach (var core in CoreFiles)
        {
            if (!coreBytes.ContainsKey(core))
            {
                throw new PackageRefusedException(
                    $"{core} is not covered by the package's checksums — the package is damaged; download it again.");
            }
        }

        var documents = ReadSeed(coreBytes["seed.json"]);
        CheckCoherence(documents, docCount, listed);
        var (people, subjects, docTypes) = ReadVocabularies(coreBytes);

        return new IndexPackage(
            PackageDirectory: packageDirectory,
            PackageId: packageId,
            FormatVersion: formatVersion,
            VocabularyVersion: vocabularyVersion,
            PackageChecksum: Convert.ToHexStringLower(SHA256.HashData(manifestBytes)),
            AppVersionAtOpen: appVersion,
            Documents: documents,
            People: people,
            Subjects: subjects,
            DocTypes: docTypes);
    }

    private static PackageRefusedException MalformedFile(string name) => new(
        $"{name} is malformed — the package is damaged; download it again.");

    private static byte[] ReadOrRefuse(string path, string name)
    {
        try
        {
            return File.ReadAllBytes(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // AV, an open preview, or an offline cloud placeholder holding
            // a file is an operator situation, not a crash dialog.
            throw new PackageRefusedException(
                $"{name} cannot be read ({ex.Message}) — close other programs using the package and try again.");
        }
    }

    private static JsonDocument ParseOrRefuse(byte[] bytes, string name)
    {
        try
        {
            return JsonDocument.Parse(bytes);
        }
        catch (JsonException ex)
        {
            throw new PackageRefusedException(
                $"{name} is not readable JSON ({ex.Message}) — the package is damaged; download it again.");
        }
    }

    private static string GetStringOrRefuse(JsonElement element, string name)
    {
        try
        {
            return element.GetString()
                ?? throw MalformedFile(name);
        }
        catch (InvalidOperationException)
        {
            throw MalformedFile(name);
        }
    }

    private static (HashSet<string> Listed, Dictionary<string, byte[]> CoreBytes) VerifyChecksums(
        string packageDirectory, JsonDocument manifest)
    {
        if (!manifest.RootElement.TryGetProperty("sha256", out var map)
            || map.ValueKind != JsonValueKind.Object)
        {
            throw MalformedFile("manifest.json");
        }

        // Only listed files are the package; a stray Thumbs.db dropped by
        // Explorer must not strand Jim's batch. A listed file that is
        // missing or altered refuses the whole package. The four core JSON
        // files keep their verified bytes: hashing one handle and parsing a
        // second read would let a sync client swap the file in between.
        var root = Path.GetFullPath(packageDirectory);
        var rootPrefix = root.EndsWith(Path.DirectorySeparatorChar) ? root : root + Path.DirectorySeparatorChar;
        var listed = new HashSet<string>(StringComparer.Ordinal);
        var coreBytes = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var entry in map.EnumerateObject())
        {
            if (entry.Value.ValueKind != JsonValueKind.String)
            {
                throw MalformedFile("manifest.json");
            }

            // The manifest is untrusted input: a listed path that resolves
            // outside the package folder is refused unresolved — hashing it
            // would let a tampered package probe files it has no business
            // naming, with the refusal text as the oracle. Path.GetFullPath
            // itself throws on a null character in the name.
            string full;
            try
            {
                full = Path.GetFullPath(Path.Combine(root, entry.Name.Replace('/', Path.DirectorySeparatorChar)));
            }
            catch (ArgumentException)
            {
                throw MalformedFile("manifest.json");
            }

            if (!full.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
            {
                throw new PackageRefusedException(
                    $"the manifest lists \"{entry.Name}\", which is outside the package — refusing to open it.");
            }

            if (!File.Exists(full))
            {
                throw new PackageRefusedException(
                    $"{entry.Name} is missing from the package — the package is damaged; download it again.");
            }

            string digest;
            if (CoreFiles.Contains(entry.Name))
            {
                var bytes = ReadOrRefuse(full, entry.Name);
                digest = Convert.ToHexStringLower(SHA256.HashData(bytes));
                coreBytes[entry.Name] = bytes;
            }
            else
            {
                try
                {
                    using var stream = File.OpenRead(full);
                    digest = Convert.ToHexStringLower(SHA256.HashData(stream));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    throw new PackageRefusedException(
                        $"{entry.Name} cannot be read ({ex.Message}) — close other programs using the package and try again.");
                }
            }

            if (digest != GetStringOrRefuse(entry.Value, "manifest.json"))
            {
                throw new PackageRefusedException(
                    $"{entry.Name} does not match its checksum — the package is damaged; download it again.");
            }

            listed.Add(entry.Name);
        }

        return (listed, coreBytes);
    }

    private static void CheckCoherence(
        List<SeedDocument> documents, int docCount, HashSet<string> listed)
    {
        // A repeated anchor, a page claimed by two documents, an anchor
        // that is not its document's first page, or a docCount that does
        // not match the seed: all exportable by a careless CLI call, and
        // each ends as a package stuck partial forever or two conflicting
        // current values for one document. Refused here, on the dev
        // machine, not discovered on Jim's laptop mid-batch.
        if (documents.Count != docCount)
        {
            throw new PackageRefusedException(
                $"the manifest says {docCount} document(s) but seed.json carries {documents.Count} — " +
                "the package is damaged; download it again.");
        }

        var anchors = new HashSet<string>(StringComparer.Ordinal);
        var pageIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var document in documents)
        {
            if (document.Pages.Count == 0)
            {
                throw new PackageRefusedException(
                    $"document {document.AnchorPageId} has no pages — the package is damaged; download it again.");
            }

            if (!anchors.Add(document.AnchorPageId))
            {
                throw new PackageRefusedException(
                    $"anchor {document.AnchorPageId} appears twice — the package is damaged; download it again.");
            }

            if (document.Pages[0].PageId != document.AnchorPageId)
            {
                throw new PackageRefusedException(
                    $"anchor {document.AnchorPageId} is not its document's first page — " +
                    "the package is damaged; download it again.");
            }

            foreach (var page in document.Pages)
            {
                if (!pageIds.Add(page.PageId))
                {
                    throw new PackageRefusedException(
                        $"page {page.PageId} appears in two documents — the package is damaged; download it again.");
                }

                // "Passed every check" must cover the pages Jim will be
                // shown: an image seed.json names but the manifest never
                // hashed is unverified content.
                if (!listed.Contains(page.Image))
                {
                    throw new PackageRefusedException(
                        $"page {page.PageId} ({page.Image}) is not covered by the package's checksums — " +
                        "the package is damaged; download it again.");
                }
            }
        }
    }

    /// <summary>Runs one file's parse under the refusal contract: shape
    /// damage in a checksummed file is damage, never a crash.</summary>
    private static T ParsePhase<T>(string name, Func<T> parse)
    {
        try
        {
            return parse();
        }
        catch (Exception ex) when (
            ex is KeyNotFoundException or InvalidOperationException or FormatException)
        {
            throw MalformedFile(name);
        }
    }

    private static long? OptionalInteger(JsonElement parent, string property, string name)
    {
        if (!parent.TryGetProperty(property, out var el) || el.ValueKind != JsonValueKind.Number)
        {
            return null;
        }

        // 1.0 is a valid integer under JSON Schema 2020-12 even though the
        // exporter never writes it that way.
        if (el.TryGetInt64(out var value))
        {
            return value;
        }

        var floating = el.GetDouble();
        return floating == Math.Floor(floating) ? (long)floating : throw MalformedFile(name);
    }

    private static List<SeedDocument> ReadSeed(byte[] seedBytes) => ParsePhase("seed.json", () =>
    {
        using var seed = ParseOrRefuse(seedBytes, "seed.json");
        var documents = new List<SeedDocument>();
        foreach (var doc in seed.RootElement.GetProperty("documents").EnumerateArray())
        {
            documents.Add(new SeedDocument(
                AnchorPageId: doc.GetProperty("anchorPageId").GetString()
                    ?? throw MalformedFile("seed.json"),
                DocumentId: OptionalInteger(doc, "documentId", "seed.json"),
                Title: doc.TryGetProperty("title", out var title) ? title.GetString() : null,
                Pages: [.. doc.GetProperty("pages").EnumerateArray().Select(p => new SeedPage(
                    p.GetProperty("pageId").GetString() ?? throw MalformedFile("seed.json"),
                    p.GetProperty("image").GetString() ?? throw MalformedFile("seed.json")))],
                Decisions: [.. doc.GetProperty("decisions").EnumerateArray().Select(d => new SeedDecision(
                    d.GetProperty("field").GetString() ?? throw MalformedFile("seed.json"),
                    // qualifier is NOT a required key (seed.schema.json):
                    // an additive-change package may stop emitting nulls.
                    d.TryGetProperty("qualifier", out var dq) ? dq.GetString() : null,
                    d.GetProperty("value").GetString() ?? throw MalformedFile("seed.json")))],
                Suggestions: [.. doc.GetProperty("suggestions").EnumerateArray().Select(s => new SeedSuggestion(
                    OptionalInteger(s, "suggestionId", "seed.json")
                        ?? throw MalformedFile("seed.json"),
                    s.GetProperty("field").GetString() ?? throw MalformedFile("seed.json"),
                    s.TryGetProperty("qualifier", out var sq) ? sq.GetString() : null,
                    s.GetProperty("value").GetString() ?? throw MalformedFile("seed.json"),
                    s.TryGetProperty("reasonQuote", out var quote) ? quote.GetString() : null))]));
            if (documents[^1].Pages.Count == 0)
            {
                // seed.schema.json: pages has minItems 1.
                throw MalformedFile("seed.json");
            }
        }

        return documents;
    });

    private static (List<PackagePerson>, List<PackageSubject>, List<PackageDocType>) ReadVocabularies(
        Dictionary<string, byte[]> coreBytes)
    {
        var people = ParsePhase("people.json", () =>
        {
            using var doc = ParseOrRefuse(coreBytes["people.json"], "people.json");
            return doc.RootElement.GetProperty("people").EnumerateArray()
                .Select(p => new PackagePerson(
                    p.GetProperty("id").GetString() ?? throw MalformedFile("people.json"),
                    p.GetProperty("displayName").GetString() ?? throw MalformedFile("people.json"),
                    p.GetProperty("kind").GetString() ?? throw MalformedFile("people.json"),
                    [.. p.GetProperty("roles").EnumerateArray()
                        .Select(r => r.GetString() ?? throw MalformedFile("people.json"))],
                    [.. p.GetProperty("aliases").EnumerateArray()
                        .Select(a => a.GetString() ?? throw MalformedFile("people.json"))]))
                .ToList();
        });

        var subjects = ParsePhase("subjects.json", () =>
        {
            using var doc = ParseOrRefuse(coreBytes["subjects.json"], "subjects.json");
            return doc.RootElement.GetProperty("subjects").EnumerateArray()
                .Select(s => new PackageSubject(
                    s.GetProperty("id").GetString() ?? throw MalformedFile("subjects.json"),
                    s.GetProperty("label").GetString() ?? throw MalformedFile("subjects.json"),
                    s.GetProperty("parentId").GetString(),
                    s.GetProperty("active").GetBoolean()))
                .ToList();
        });

        var docTypes = ParsePhase("doctypes.json", () =>
        {
            using var doc = ParseOrRefuse(coreBytes["doctypes.json"], "doctypes.json");
            return doc.RootElement.GetProperty("docTypes").EnumerateArray()
                .Select(d => new PackageDocType(
                    d.GetProperty("id").GetString() ?? throw MalformedFile("doctypes.json"),
                    d.GetProperty("label").GetString() ?? throw MalformedFile("doctypes.json"),
                    d.GetProperty("family").GetString() ?? throw MalformedFile("doctypes.json"),
                    d.GetProperty("active").GetBoolean()))
                .ToList();
        });

        return (people, subjects, docTypes);
    }
}
