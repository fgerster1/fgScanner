using FgScanner.Core;
using FgScanner.Core.Index;
using FgScanner.Data;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FgScanner.Data.Tests;

public sealed class CapturedByTests : IDisposable
{
    private readonly TestDb _db = new();
    private readonly GroupService _groups;
    private readonly ProfileService _profiles;
    private readonly IndexingService _indexing;
    private readonly string _groupsRoot;

    public CapturedByTests()
    {
        _groups = new GroupService(_db.Factory);
        _profiles = new ProfileService(_db.Factory);
        _indexing = new IndexingService(_db.Factory, _profiles, new IndexExporter());
        _groupsRoot = Path.Combine(_db.Root, "groups");
        Directory.CreateDirectory(_groupsRoot);
    }

    public void Dispose() => _db.Dispose();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<string> MakeImageAsync(string name)
    {
        var incoming = Path.Combine(_db.Root, "incoming-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(incoming);
        var path = Path.Combine(incoming, name);
        await File.WriteAllBytesAsync(path, [0x01, 0xFF], Ct);
        return path;
    }

    [Fact]
    public async Task An_adopted_page_records_the_current_user()
    {
        var group = await _groups.CreateGroupAsync(_groupsRoot, "Captured", null, Ct);
        await _groups.AdoptPagesAsync(group.Id, [await MakeImageAsync("p1.png")], Ct);

        await using var db = _db.Factory.CreateDbContext();
        var captured = await db.Pages
            .Where(p => p.Document!.GroupId == group.Id)
            .Select(p => p.CapturedBy)
            .ToListAsync(Ct);

        Assert.All(captured, c => Assert.Equal(Environment.UserName, c));
    }

    /// <summary>
    /// Retro-processing adopts files scanned elsewhere, possibly years ago on another machine.
    /// Naming whoever ran the import as their captor would be a fabrication, and on an evidence
    /// station a fabricated provenance is worse than an absent one.
    /// </summary>
    [Fact]
    public async Task A_retro_processed_page_records_no_captor()
    {
        var trash = new TrashService(_db.Factory, Path.Combine(_db.Root, "trash"));
        var retro = new RetroProcessService(_db.Factory, _groups, trash, new FakePdfRenderer());
        var folder = Path.Combine(_db.Root, "Existing Scans");
        Directory.CreateDirectory(folder);
        await File.WriteAllBytesAsync(Path.Combine(folder, "holiday-1998.jpg"), [1, 1], Ct);
        await File.WriteAllBytesAsync(Path.Combine(folder, "contract page 2.png"), [2, 2], Ct);

        var report = await retro.ProcessFolderAsync(folder, null, Ct);
        Assert.Equal(2, report.AdoptedImages);

        await using var db = _db.Factory.CreateDbContext();
        var captured = await db.Pages.Select(p => p.CapturedBy).ToListAsync(Ct);

        Assert.All(captured, Assert.Null);
    }

    [Fact]
    public async Task The_json_row_carries_captured_by()
    {
        var group = await _groups.CreateGroupAsync(_groupsRoot, "Captured", null, Ct);
        await _groups.AdoptPagesAsync(group.Id, [await MakeImageAsync("p1.png")], Ct);

        var json = IndexPayload.ToJson(await _indexing.BuildExportDataAsync(group.Id, Ct));

        Assert.Contains("\"capturedBy\"", json, StringComparison.Ordinal);
        Assert.Contains(Environment.UserName, json, StringComparison.Ordinal);
    }

    /// <summary>Renders one "page" per 100 bytes of PDF size — deterministic, no Pdfium needed.</summary>
    private sealed class FakePdfRenderer : IPdfRenderer
    {
        public async Task<IReadOnlyList<string>> RenderPagesAsync(
            string pdfPath, string outputDirectory, CancellationToken cancellationToken = default)
        {
            var pageCount = Math.Max(1, (int)(new FileInfo(pdfPath).Length / 100));
            var files = new List<string>();
            for (var i = 1; i <= pageCount; i++)
            {
                var file = Path.Combine(outputDirectory, $"r{i}.png");
                await File.WriteAllBytesAsync(
                    file, [(byte)i, .. System.Text.Encoding.UTF8.GetBytes(Path.GetFileName(pdfPath))],
                    cancellationToken);
                files.Add(file);
            }

            return files;
        }
    }
}
