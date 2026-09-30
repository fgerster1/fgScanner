using System.Security.Cryptography;
using System.Text.Json;

namespace FgScanner.Core.IndexPackages;

/// <summary>
/// Opens an index package folder, refusing before anything opens: marker,
/// then formatVersion, then every checksum — only a package that passed all
/// three is parsed. The formatVersion check comes before the checksums on
/// purpose: a future format may hash differently, and the honest answer to
/// version N+1 is the update message, never "damaged".
/// </summary>
public static class PackageReader
{
    /// <summary>The one formatVersion this build reads (contract README: N).</summary>
    public const int KnownFormatVersion = 1;

    /// <summary>Verbatim from docs/contract-vendored/README.md — the schemas' law.</summary>
    public const string UpdateMessage =
        "This package needs a newer FG Scanner — close and reopen the app to update.";

    /// <summary>
    /// <paramref name="appVersion"/> is pinned here, at open time, and echoed
    /// into results.json — an auto-update mid-batch cannot lie about which
    /// build showed Jim the pages.
    /// </summary>
    public static IndexPackage Open(string packageDirectory, string appVersion)
    {
        var manifestPath = Path.Combine(packageDirectory, "manifest.json");
        if (!File.Exists(manifestPath))
        {
            throw new PackageRefusedException(
                $"no manifest.json in \"{packageDirectory}\" — this is not an index package.");
        }

        var manifestBytes = File.ReadAllBytes(manifestPath);
        using var manifest = ParseOrRefuse(manifestBytes, "manifest.json");
        if (!manifest.RootElement.TryGetProperty("indexPackage", out var marker)
            || marker.ValueKind != JsonValueKind.Number
            || !marker.TryGetInt32(out var markerValue) || markerValue != 1)
        {
            throw new PackageRefusedException(
                $"\"{packageDirectory}\" is not an index package — its manifest carries no indexPackage marker.");
        }

        // manifest.json is the one file no checksum protects (it is the
        // root of the hashes), so parseable-but-malformed damage must end
        // in the same operator refusal as any other damage — never a raw
        // KeyNotFoundException crash dialog.
        if (!manifest.RootElement.TryGetProperty("formatVersion", out var format)
            || !format.TryGetInt32(out var formatVersion))
        {
            throw MalformedManifest();
        }

        if (formatVersion != KnownFormatVersion)
        {
            throw new PackageRefusedException(UpdateMessage);
        }

        if (!manifest.RootElement.TryGetProperty("packageId", out var packageId)
            || packageId.ValueKind != JsonValueKind.String
            || !manifest.RootElement.TryGetProperty("vocabularyVersion", out var vocabulary)
            || !vocabulary.TryGetInt32(out var vocabularyVersion))
        {
            throw MalformedManifest();
        }

        var listed = VerifyChecksums(packageDirectory, manifest);

        var documents = ReadSeed(Path.Combine(packageDirectory, "seed.json"));
        foreach (var page in documents.SelectMany(d => d.Pages))
        {
            // "Passed every check" must cover the pages Jim will be shown:
            // an image seed.json names but the manifest never hashed is
            // unverified content, refused now rather than mid-batch.
            if (!listed.Contains(page.Image))
            {
                throw new PackageRefusedException(
                    $"page {page.PageId} ({page.Image}) is not covered by the package's checksums — " +
                    "the package is damaged; download it again.");
            }
        }

        var (people, subjects, docTypes) = ReadVocabularies(packageDirectory);

        return new IndexPackage(
            PackageDirectory: packageDirectory,
            PackageId: packageId.GetString()!,
            FormatVersion: formatVersion,
            VocabularyVersion: vocabularyVersion,
            PackageChecksum: Convert.ToHexStringLower(SHA256.HashData(manifestBytes)),
            AppVersionAtOpen: appVersion,
            Documents: documents,
            People: people,
            Subjects: subjects,
            DocTypes: docTypes);
    }

    private static PackageRefusedException MalformedManifest() => new(
        "manifest.json is malformed — the package is damaged; download it again.");

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

    private static HashSet<string> VerifyChecksums(string packageDirectory, JsonDocument manifest)
    {
        if (!manifest.RootElement.TryGetProperty("sha256", out var map)
            || map.ValueKind != JsonValueKind.Object)
        {
            throw MalformedManifest();
        }

        // Only listed files are the package; a stray Thumbs.db dropped by
        // Explorer must not strand Jim's batch. A listed file that is
        // missing or altered refuses the whole package.
        var root = Path.GetFullPath(packageDirectory);
        var rootPrefix = root.EndsWith(Path.DirectorySeparatorChar) ? root : root + Path.DirectorySeparatorChar;
        var listed = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in map.EnumerateObject())
        {
            if (entry.Value.ValueKind != JsonValueKind.String)
            {
                throw MalformedManifest();
            }

            // The manifest is untrusted input: a listed path that resolves
            // outside the package folder is refused unresolved — hashing it
            // would let a tampered package probe files it has no business
            // naming, with the refusal text as the oracle.
            var full = Path.GetFullPath(Path.Combine(root, entry.Name.Replace('/', Path.DirectorySeparatorChar)));
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

            using var stream = File.OpenRead(full);
            var digest = Convert.ToHexStringLower(SHA256.HashData(stream));
            if (digest != entry.Value.GetString())
            {
                throw new PackageRefusedException(
                    $"{entry.Name} does not match its checksum — the package is damaged; download it again.");
            }

            listed.Add(entry.Name);
        }

        return listed;
    }

    private static List<SeedDocument> ReadSeed(string seedPath)
    {
        using var seed = ParseOrRefuse(File.ReadAllBytes(seedPath), "seed.json");
        var documents = new List<SeedDocument>();
        foreach (var doc in seed.RootElement.GetProperty("documents").EnumerateArray())
        {
            documents.Add(new SeedDocument(
                AnchorPageId: doc.GetProperty("anchorPageId").GetString()!,
                DocumentId: doc.TryGetProperty("documentId", out var id) && id.ValueKind == JsonValueKind.Number
                    ? id.GetInt64() : null,
                Title: doc.TryGetProperty("title", out var title) ? title.GetString() : null,
                Pages: [.. doc.GetProperty("pages").EnumerateArray().Select(p => new SeedPage(
                    p.GetProperty("pageId").GetString()!,
                    p.GetProperty("image").GetString()!))],
                Decisions: [.. doc.GetProperty("decisions").EnumerateArray().Select(d => new SeedDecision(
                    d.GetProperty("field").GetString()!,
                    // qualifier is NOT a required key (seed.schema.json):
                    // an additive-change package may stop emitting nulls.
                    d.TryGetProperty("qualifier", out var dq) ? dq.GetString() : null,
                    d.GetProperty("value").GetString()!))],
                Suggestions: [.. doc.GetProperty("suggestions").EnumerateArray().Select(s => new SeedSuggestion(
                    s.GetProperty("suggestionId").GetInt64(),
                    s.GetProperty("field").GetString()!,
                    s.TryGetProperty("qualifier", out var sq) ? sq.GetString() : null,
                    s.GetProperty("value").GetString()!,
                    s.TryGetProperty("reasonQuote", out var quote) ? quote.GetString() : null))]));
        }

        return documents;
    }

    private static (List<PackagePerson>, List<PackageSubject>, List<PackageDocType>) ReadVocabularies(
        string packageDirectory)
    {
        using var peopleDoc = ParseOrRefuse(
            File.ReadAllBytes(Path.Combine(packageDirectory, "people.json")), "people.json");
        var people = peopleDoc.RootElement.GetProperty("people").EnumerateArray()
            .Select(p => new PackagePerson(
                p.GetProperty("id").GetString()!,
                p.GetProperty("displayName").GetString()!,
                p.GetProperty("kind").GetString()!,
                [.. p.GetProperty("roles").EnumerateArray().Select(r => r.GetString()!)],
                [.. p.GetProperty("aliases").EnumerateArray().Select(a => a.GetString()!)]))
            .ToList();

        using var subjectsDoc = ParseOrRefuse(
            File.ReadAllBytes(Path.Combine(packageDirectory, "subjects.json")), "subjects.json");
        var subjects = subjectsDoc.RootElement.GetProperty("subjects").EnumerateArray()
            .Select(s => new PackageSubject(
                s.GetProperty("id").GetString()!,
                s.GetProperty("label").GetString()!,
                s.GetProperty("parentId").GetString(),
                s.GetProperty("active").GetBoolean()))
            .ToList();

        using var docTypesDoc = ParseOrRefuse(
            File.ReadAllBytes(Path.Combine(packageDirectory, "doctypes.json")), "doctypes.json");
        var docTypes = docTypesDoc.RootElement.GetProperty("docTypes").EnumerateArray()
            .Select(d => new PackageDocType(
                d.GetProperty("id").GetString()!,
                d.GetProperty("label").GetString()!,
                d.GetProperty("family").GetString()!,
                d.GetProperty("active").GetBoolean()))
            .ToList();

        return (people, subjects, docTypes);
    }
}
