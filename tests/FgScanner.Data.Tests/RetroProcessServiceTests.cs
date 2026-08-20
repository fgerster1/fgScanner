using FgScanner.Core;
using FgScanner.Data;
using Xunit;

namespace FgScanner.Data.Tests;

public sealed class RetroProcessServiceTests : IDisposable
{
    private readonly TestDb _db = new();
    private readonly GroupService _groups;
    private readonly TrashService _trash;
    private readonly RetroProcessService _retro;
    private readonly string _folder;

    public RetroProcessServiceTests()
    {
        _groups = new GroupService(_db.Factory);
        _trash = new TrashService(_db.Factory, Path.Combine(_db.Root, "trash"));
        _retro = new RetroProcessService(_db.Factory, _groups, _trash, new FakePdfRenderer());
        _folder = Path.Combine(_db.Root, "Existing Scans");
        Directory.CreateDirectory(_folder);
    }

    public void Dispose() => _db.Dispose();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

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

    private async Task WriteImageAsync(string name, byte[] content) =>
        await File.WriteAllBytesAsync(Path.Combine(_folder, name), content, Ct);

    [Fact]
    public async Task Fresh_folder_registers_images_in_place_keeping_names()
    {
        await WriteImageAsync("holiday-1998.jpg", [1, 1]);
        await WriteImageAsync("contract page 2.png", [2, 2]);
        await WriteImageAsync("notes.txt".Replace("notes.txt", "notes.txt"), [9]); // non-image ignored

        var report = await _retro.ProcessFolderAsync(_folder, null, Ct);

        Assert.Equal(2, report.AdoptedImages);
        Assert.Empty(report.DuplicateFiles);
        var pages = await _groups.GetPagesAsync(report.GroupId, Ct);
        Assert.Equal(["contract page 2.png", "holiday-1998.jpg"], pages.Select(p => p.FileName));
        Assert.All(pages, p => Assert.True(
            File.Exists(Path.Combine(_folder, p.FileName)), "files stay in place, unrenamed"));
    }

    [Fact]
    public async Task Second_run_over_unchanged_folder_changes_nothing()
    {
        await WriteImageAsync("a.png", [1]);
        await WriteImageAsync("b.png", [2]);
        var first = await _retro.ProcessFolderAsync(_folder, null, Ct);
        var pagesAfterFirst = await _groups.GetPagesAsync(first.GroupId, Ct);

        var second = await _retro.ProcessFolderAsync(_folder, null, Ct);

        Assert.False(second.ChangedAnything, "idempotence is the acceptance bar");
        Assert.Equal(0, second.AdoptedImages + second.AdoptedPdfPages);
        Assert.Empty(second.DuplicateFiles);
        var pagesAfterSecond = await _groups.GetPagesAsync(second.GroupId, Ct);
        Assert.Equal(
            pagesAfterFirst.Select(p => (p.Id, p.FileName, p.Checksum)),
            pagesAfterSecond.Select(p => (p.Id, p.FileName, p.Checksum)));
    }

    [Fact]
    public async Task Partial_folder_adopts_only_the_new_files()
    {
        await WriteImageAsync("old.png", [1]);
        var first = await _retro.ProcessFolderAsync(_folder, null, Ct);
        await WriteImageAsync("new.png", [2]);

        var second = await _retro.ProcessFolderAsync(_folder, null, Ct);

        Assert.Equal(1, second.AdoptedImages);
        Assert.Equal(2, (await _groups.GetPagesAsync(first.GroupId, Ct)).Count);
    }

    [Fact]
    public async Task Duplicate_content_is_reported_not_rerowed()
    {
        await WriteImageAsync("original.png", [7, 7, 7]);
        await _retro.ProcessFolderAsync(_folder, null, Ct);
        await WriteImageAsync("copy-of-original.png", [7, 7, 7]);

        var report = await _retro.ProcessFolderAsync(_folder, null, Ct);

        Assert.Equal(0, report.AdoptedImages);
        Assert.Equal(["copy-of-original.png"], report.DuplicateFiles);
        Assert.Single(await _groups.GetPagesAsync(report.GroupId, Ct));
    }

    [Fact]
    public async Task Renamed_file_is_rematched_by_checksum_keeping_its_row()
    {
        await WriteImageAsync("before.png", [5, 5]);
        var first = await _retro.ProcessFolderAsync(_folder, null, Ct);
        var originalPageId = (await _groups.GetPagesAsync(first.GroupId, Ct))[0].Id;
        File.Move(Path.Combine(_folder, "before.png"), Path.Combine(_folder, "after.png"));

        var report = await _retro.ProcessFolderAsync(_folder, null, Ct);

        Assert.Equal([("before.png", "after.png")], report.RematchedByChecksum);
        Assert.Empty(report.RowsWithoutFiles);
        Assert.Equal(0, report.AdoptedImages);
        var page = Assert.Single(await _groups.GetPagesAsync(first.GroupId, Ct));
        Assert.Equal(originalPageId, page.Id); // same row survived the rename
        Assert.Equal("after.png", page.FileName);
    }

    [Fact]
    public async Task Vanished_file_is_reported_and_removable_to_trash()
    {
        await WriteImageAsync("keep.png", [1]);
        await WriteImageAsync("gone.png", [2]);
        var first = await _retro.ProcessFolderAsync(_folder, null, Ct);
        File.Delete(Path.Combine(_folder, "gone.png"));

        var report = await _retro.ReconcileAsync(first.GroupId, Ct);
        Assert.Equal(["gone.png"], report.RowsWithoutFiles);

        var removed = await _retro.RemoveRowsWithoutFilesAsync(first.GroupId, Ct);

        Assert.Equal(1, removed);
        Assert.Equal(["keep.png"], (await _groups.GetPagesAsync(first.GroupId, Ct)).Select(p => p.FileName));
        Assert.Single(await _trash.ListAsync(Ct)); // restorable, never hard-deleted
    }

    [Fact]
    public async Task Foreign_index_csv_triggers_a_warning()
    {
        await WriteImageAsync("page.png", [1]);
        await File.WriteAllTextAsync(
            Path.Combine(_folder, "index.csv"), "SomeoneElses,Header\r\n", Ct);

        var report = await _retro.ProcessFolderAsync(_folder, null, Ct);

        Assert.Equal(["index.csv"], report.ForeignIndexFiles);
        Assert.True(File.Exists(Path.Combine(_folder, "index.csv")), "never overwritten silently");
    }

    [Fact]
    public async Task Our_own_index_files_are_not_foreign()
    {
        await WriteImageAsync("page.png", [1]);
        await File.WriteAllTextAsync(Path.Combine(_folder, "index.csv"), "ours", Ct);
        await File.WriteAllTextAsync(Path.Combine(_folder, "manifest.json"), "{}", Ct);

        var report = await _retro.ProcessFolderAsync(_folder, null, Ct);

        Assert.Empty(report.ForeignIndexFiles);
    }

    [Fact]
    public async Task Pdfs_render_to_pages_through_the_adoption_path()
    {
        await File.WriteAllBytesAsync(
            Path.Combine(_folder, "statement.pdf"), new byte[250], Ct); // fake: 2 pages

        var report = await _retro.ProcessFolderAsync(_folder, null, Ct);

        Assert.Equal(2, report.AdoptedPdfPages);
        var pages = await _groups.GetPagesAsync(report.GroupId, Ct);
        Assert.Equal(
            ["statement_page_001.png", "statement_page_002.png"],
            pages.Select(p => p.FileName).Order());
        Assert.All(pages, p => Assert.True(File.Exists(Path.Combine(_folder, p.FileName))));

        var again = await _retro.ProcessFolderAsync(_folder, null, Ct);
        Assert.Equal(0, again.AdoptedPdfPages); // rendered pages dedupe by content on re-run
    }

    [Fact]
    public async Task Sidecars_and_our_output_files_never_become_pages()
    {
        await WriteImageAsync("page.png", [1]);
        await File.WriteAllTextAsync(Path.Combine(_folder, "page.md"), "# ocr", Ct);
        await File.WriteAllTextAsync(Path.Combine(_folder, "manifest.json"), "{}", Ct);

        var report = await _retro.ProcessFolderAsync(_folder, null, Ct);

        Assert.Equal(1, report.AdoptedImages);
        Assert.Single(await _groups.GetPagesAsync(report.GroupId, Ct));
    }
}
