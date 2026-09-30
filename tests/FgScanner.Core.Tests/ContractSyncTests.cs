using System.Security.Cryptography;
using System.Text.Json;
using Xunit;

namespace FgScanner.Core.Tests;

/// <summary>
/// JimsStuff SPEC-2026-005 (contract-slice) AC-6, scanner half. The vendored
/// contract copy under docs/contract-vendored/ is synced from JimsStuff by
/// build/sync-contract.ps1, never edited by hand. The sync writes the same
/// sync-manifest.json to both repos; the per-repo test proves the vendored
/// bytes against the vendored manifest (a hand edit reds this build; a
/// portal-side edit without a re-sync reds JimsStuff's suite). A vendor that
/// went stale WITH its manifest is self-consistent, which only the
/// cross-repo manifest comparison below can catch — it runs wherever the
/// JimsStuff repo sits beside this one, which the dev machine always has.
/// </summary>
public sealed class ContractSyncTests
{
    // The manifest never lists itself; .gitattributes is repo plumbing; the
    // last two are Explorer droppings — excluded HERE, in the sync script
    // and in JimsStuff's test alike, so an OS-minted hidden file cannot
    // wedge the drift check into a red no re-sync can clear.
    private static readonly string[] Excluded =
        ["sync-manifest.json", ".gitattributes", "Thumbs.db", "desktop.ini"];

    private static string VendoredRoot() =>
        Path.Combine(TestPaths.RepoRoot(), "docs", "contract-vendored");

    private static IEnumerable<string> VendoredFiles(string root) =>
        Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Where(p => !Excluded.Contains(Path.GetFileName(p), StringComparer.OrdinalIgnoreCase))
            .Select(p => Path.GetRelativePath(root, p).Replace('\\', '/'))
            .Order(StringComparer.Ordinal);

    [Fact]
    public void VendoredCopyMatchesTheSyncManifest()
    {
        var root = VendoredRoot();
        Assert.True(Directory.Exists(root),
            $"no vendored contract at {root} — run build/sync-contract.ps1");
        var manifestPath = Path.Combine(root, "sync-manifest.json");
        Assert.True(File.Exists(manifestPath),
            "no sync manifest in the vendored copy — run build/sync-contract.ps1");

        using var doc = JsonDocument.Parse(File.ReadAllText(manifestPath));
        var listed = doc.RootElement.GetProperty("files").EnumerateObject()
            .ToDictionary(p => p.Name, p => p.Value.GetString());

        var onDisk = VendoredFiles(root).ToList();
        Assert.Equal(listed.Keys.Order(StringComparer.Ordinal), onDisk);

        foreach (var rel in onDisk)
        {
            var bytes = File.ReadAllBytes(Path.Combine(root, rel));
            var digest = Convert.ToHexStringLower(SHA256.HashData(bytes));
            Assert.True(digest == listed[rel],
                $"{rel} drifted from the sync manifest — the vendored copy is " +
                "synced, never edited; re-run build/sync-contract.ps1");
        }
    }

    [Fact]
    public void BothRepositoriesCarryTheSameSyncManifest()
    {
        var sibling = Path.Combine(Path.GetDirectoryName(TestPaths.RepoRoot())!,
            "JimsStuff", "docs", "contract", "sync-manifest.json");
        Assert.SkipWhen(!File.Exists(sibling),
            "JimsStuff repo not beside this one (CI) — the cross-repo check runs on the dev machine");
        Assert.True(File.ReadAllBytes(sibling).SequenceEqual(
                File.ReadAllBytes(Path.Combine(VendoredRoot(), "sync-manifest.json"))),
            "the two repos' sync manifests differ — one side's sync output was lost; " +
            "re-run build/sync-contract.ps1 and commit BOTH repos");
    }

    [Fact]
    public void VendoredCopyCarriesTheGoldenPackage()
    {
        // The golden package is what ContractGoldenTests opens; an otherwise
        // consistent vendor that lost it would pass the hash check and then
        // fail confusingly later.
        var golden = Path.Combine(VendoredRoot(), "golden", "package", "PKG-0001");
        Assert.True(File.Exists(Path.Combine(golden, "manifest.json")),
            $"no golden package under {golden} — run build/sync-contract.ps1");
    }
}
