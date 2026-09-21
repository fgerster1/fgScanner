using FgScanner.Data;
using Xunit;

namespace FgScanner.Data.Tests;

/// <summary>
/// Adoption drops a page whose SHA-256 is already in the group, which is right for every path that
/// has one today — a folder adopted twice, a replayed batch — and catastrophic for a duplex run.
/// The blank back of a printed sheet is byte-identical to the blank back of every other sheet
/// scanned at the same settings, so ten sheets would save one blank and silently drop nine,
/// shifting every pairing after it. The blank back of an evidence page is evidence (§05 Q2a).
///
/// No existing test catches this, because FakeScanService deliberately stamps a run number into
/// its bitmaps so its pages never collide.
/// </summary>
public sealed class DuplexAdoptionTests : IDisposable
{
    private readonly TestDb _db = new();
    private readonly GroupService _service;
    private readonly string _groupsRoot;
    private readonly string _incoming;

    public DuplexAdoptionTests()
    {
        _service = new GroupService(_db.Factory);
        _groupsRoot = Path.Combine(_db.Root, "groups");
        _incoming = Path.Combine(_db.Root, "incoming");
        Directory.CreateDirectory(_groupsRoot);
        Directory.CreateDirectory(_incoming);
    }

    public void Dispose() => _db.Dispose();

    /// <summary>Ten files with identical bytes — what a stack of blank backs actually produces.</summary>
    private string[] IdenticalPages(int count)
    {
        var content = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x00, 0x01, 0x02, 0x03 };
        var paths = new string[count];
        for (var i = 0; i < count; i++)
        {
            paths[i] = Path.Combine(_incoming, $"back-{i + 1:D2}.png");
            File.WriteAllBytes(paths[i], content);
        }

        return paths;
    }

    [Fact]
    public async Task Identical_blank_backs_are_all_kept_in_a_duplex_run()
    {
        var ct = TestContext.Current.CancellationToken;
        var group = await _service.CreateGroupAsync(_groupsRoot, "Duplex stack", null, ct);

        var result = await _service.AdoptPagesAsync(
            group.Id, IdenticalPages(10), isBlank: null, keepIdenticalPages: true, ct);

        Assert.Equal(10, result.Adopted.Count);
        Assert.Empty(result.DuplicateSourceFiles);
    }

    /// <summary>
    /// The other half of the same guarantee. The checksum skip protects every path that is not a
    /// duplex run, so the flag has to be the exception and never the new default.
    /// </summary>
    [Fact]
    public async Task The_same_pages_adopted_normally_still_de_duplicate()
    {
        var ct = TestContext.Current.CancellationToken;
        var group = await _service.CreateGroupAsync(_groupsRoot, "Ordinary stack", null, ct);

        var result = await _service.AdoptPagesAsync(group.Id, IdenticalPages(10), ct);

        Assert.Single(result.Adopted);
        Assert.Equal(9, result.DuplicateSourceFiles.Count);
    }

    /// <summary>
    /// A duplex run does not make the group forget what it already holds for later runs: the flag
    /// is scoped to the call, not written to the group.
    /// </summary>
    [Fact]
    public async Task A_later_ordinary_adoption_still_de_duplicates_after_a_duplex_run()
    {
        var ct = TestContext.Current.CancellationToken;
        var group = await _service.CreateGroupAsync(_groupsRoot, "Mixed", null, ct);
        await _service.AdoptPagesAsync(group.Id, IdenticalPages(3), isBlank: null, keepIdenticalPages: true, ct);

        var result = await _service.AdoptPagesAsync(group.Id, IdenticalPages(2), ct);

        Assert.Empty(result.Adopted);
        Assert.Equal(2, result.DuplicateSourceFiles.Count);
    }

    /// <summary>
    /// Kept pages are whole pages, not one page counted ten times: each gets its own sequence and
    /// its own file, which is what the index rows and the scan_NNNNN names are built from.
    /// </summary>
    [Fact]
    public async Task Every_kept_back_gets_its_own_sequence_and_file()
    {
        var ct = TestContext.Current.CancellationToken;
        var group = await _service.CreateGroupAsync(_groupsRoot, "Sequences", null, ct);

        var result = await _service.AdoptPagesAsync(
            group.Id, IdenticalPages(4), isBlank: null, keepIdenticalPages: true, ct);

        var fileNames = result.Adopted.Select(p => p.FileName).ToList();
        Assert.Equal(4, fileNames.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(fileNames, name => Assert.True(File.Exists(Path.Combine(group.DirectoryPath, name))));
    }
}
