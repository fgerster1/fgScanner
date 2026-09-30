using System.Security.Cryptography;
using System.Text.Json;
using Xunit;

namespace FgScanner.Core.Tests;

/// <summary>
/// SPEC-2026-005 AC-6, scanner half. The vendored contract copy under
/// docs/contract-vendored/ is synced from JimsStuff by build/sync-contract.ps1,
/// never edited by hand. The sync writes the same sync-manifest.json to both
/// repos; this test checks the vendored bytes against it, so a hand edit or a
/// stale vendor is a red build here, and a portal-side edit without a re-sync
/// is a red build in the JimsStuff suite (tests/test_contract_sync.py).
/// </summary>
public sealed class ContractSyncTests
{
    private static readonly string[] Excluded = ["sync-manifest.json", ".gitattributes"];

    private static string VendoredRoot()
    {
        // Walk up from bin/ to the repo root (folder containing FgScanner.slnx).
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "FgScanner.slnx")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        return Path.Combine(dir.FullName, "docs", "contract-vendored");
    }

    private static IEnumerable<string> VendoredFiles(string root) =>
        Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Where(p => !Excluded.Contains(Path.GetFileName(p)))
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
