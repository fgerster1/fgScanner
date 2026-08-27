using FgScanner.Data;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FgScanner.Data.Tests;

/// <summary>
/// Cross-group move. Document.GroupId was only ever assigned at insert, so correcting a
/// scanned-into-the-wrong-group mistake meant export-then-import, which minted a new document id
/// and lost field values, OCR status and the AI description.
/// </summary>
public sealed class MoveDocumentsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "fgscanner-tests", Guid.NewGuid().ToString("N"));
    private readonly string _dbPath;
    private readonly GroupService _groups;

    public MoveDocumentsTests()
    {
        Directory.CreateDirectory(_root);
        _dbPath = Path.Combine(_root, "test.db");
        using (var db = new FgScannerDbContext(DbBootstrapper.BuildOptions(_dbPath)))
        {
            db.Database.Migrate();
        }

        _groups = new GroupService(new TestFactory(_dbPath));
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private sealed class TestFactory(string dbPath) : IDbContextFactory<FgScannerDbContext>
    {
        public FgScannerDbContext CreateDbContext() => new(DbBootstrapper.BuildOptions(dbPath));
    }

    private async Task<Group> CreateGroupAsync(string name) =>
        await _groups.CreateGroupAsync(_root, name, null, TestContext.Current.CancellationToken);

    /// <summary>Adds a page with distinct content so checksums differ unless we mean them to match.</summary>
    private async Task<Guid> AddPageAsync(Group group, byte content, string? sidecarText = null)
    {
        var staging = Directory.CreateDirectory(Path.Combine(_root, Guid.NewGuid().ToString("N"))).FullName;
        var file = Path.Combine(staging, "scan.png");
        await File.WriteAllBytesAsync(file, [content, content, content], TestContext.Current.CancellationToken);
        var result = await _groups.AdoptPagesAsync(
            group.Id, [file], _ => false, TestContext.Current.CancellationToken);
        var page = result.Adopted.Single();

        if (sidecarText is not null)
        {
            var image = Path.Combine(group.DirectoryPath, page.FileName);
            await File.WriteAllTextAsync(
                Path.ChangeExtension(image, ".md"), sidecarText, TestContext.Current.CancellationToken);
        }

        return page.DocumentId;
    }

    [Fact]
    public async Task Moving_a_document_reassigns_it_and_relocates_its_files()
    {
        var source = await CreateGroupAsync("Source");
        var target = await CreateGroupAsync("Target");
        var documentId = await AddPageAsync(source, 1, "recognised text");

        var result = await _groups.MoveDocumentsAsync(
            source.Id, target.Id, [documentId], TestContext.Current.CancellationToken);

        Assert.Equal(1, result.MovedCount);
        await using var db = new FgScannerDbContext(DbBootstrapper.BuildOptions(_dbPath));
        var document = await db.Documents.Include(d => d.Pages)
            .SingleAsync(d => d.Id == documentId, TestContext.Current.CancellationToken);
        Assert.Equal(target.Id, document.GroupId);

        var fileName = document.Pages.Single().FileName;
        Assert.True(File.Exists(Path.Combine(target.DirectoryPath, fileName)));
        Assert.False(File.Exists(Path.Combine(source.DirectoryPath, fileName)));
    }

    [Fact]
    public async Task The_preserved_original_travels_with_the_image()
    {
        var source = await CreateGroupAsync("Source");
        var target = await CreateGroupAsync("Target");
        var documentId = await AddPageAsync(source, 1);
        await using (var db = new FgScannerDbContext(DbBootstrapper.BuildOptions(_dbPath)))
        {
            var sourceName = (await db.Pages.SingleAsync(TestContext.Current.CancellationToken)).FileName;
            var archive = Core.Imaging.OriginalArchive.PathFor(Path.Combine(source.DirectoryPath, sourceName));
            Directory.CreateDirectory(Path.GetDirectoryName(archive)!);
            await File.WriteAllBytesAsync(archive, [7, 7, 7], TestContext.Current.CancellationToken);
        }

        await _groups.MoveDocumentsAsync(
            source.Id, target.Id, [documentId], TestContext.Current.CancellationToken);

        await using var check = new FgScannerDbContext(DbBootstrapper.BuildOptions(_dbPath));
        var fileName = (await check.Pages.SingleAsync(TestContext.Current.CancellationToken)).FileName;
        var moved = Core.Imaging.OriginalArchive.PathFor(Path.Combine(target.DirectoryPath, fileName));
        Assert.True(File.Exists(moved), "the untouched capture must move with its page, under the page's new name");
        Assert.Equal([7, 7, 7], await File.ReadAllBytesAsync(moved, TestContext.Current.CancellationToken));
        Assert.False(Directory.EnumerateFiles(
            Path.Combine(source.DirectoryPath, Core.Imaging.OriginalArchive.FolderName)).Any());
    }

    [Fact]
    public async Task The_ocr_sidecar_travels_with_the_image()
    {
        var source = await CreateGroupAsync("Source");
        var target = await CreateGroupAsync("Target");
        var documentId = await AddPageAsync(source, 1, "recognised text");

        await _groups.MoveDocumentsAsync(
            source.Id, target.Id, [documentId], TestContext.Current.CancellationToken);

        await using var db = new FgScannerDbContext(DbBootstrapper.BuildOptions(_dbPath));
        var fileName = (await db.Pages.SingleAsync(TestContext.Current.CancellationToken)).FileName;
        var movedSidecar = Path.ChangeExtension(Path.Combine(target.DirectoryPath, fileName), ".md");
        Assert.True(File.Exists(movedSidecar), "the .md sidecar must move with its image");
        Assert.Equal("recognised text", await File.ReadAllTextAsync(movedSidecar, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Field_values_ocr_and_ai_survive_the_move()
    {
        var source = await CreateGroupAsync("Source");
        var target = await CreateGroupAsync("Target");
        var documentId = await AddPageAsync(source, 1);

        await using (var seed = new FgScannerDbContext(DbBootstrapper.BuildOptions(_dbPath)))
        {
            var doc = await seed.Documents.Include(d => d.Pages)
                .SingleAsync(d => d.Id == documentId, TestContext.Current.CancellationToken);
            doc.CustomFieldsJson = """{"Vendor":"Summit Racing"}""";
            var page = doc.Pages.Single();
            page.OcrStatus = OcrStatus.Yes;
            page.OcrText = "recognised text";
            page.AiDescription = "an invoice";
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await _groups.MoveDocumentsAsync(
            source.Id, target.Id, [documentId], TestContext.Current.CancellationToken);

        await using var db = new FgScannerDbContext(DbBootstrapper.BuildOptions(_dbPath));
        var moved = await db.Documents.Include(d => d.Pages)
            .SingleAsync(d => d.Id == documentId, TestContext.Current.CancellationToken);
        Assert.Contains("Summit Racing", moved.CustomFieldsJson);
        var movedPage = moved.Pages.Single();
        Assert.Equal(OcrStatus.Yes, movedPage.OcrStatus);
        Assert.Equal("recognised text", movedPage.OcrText);
        Assert.Equal("an invoice", movedPage.AiDescription);
    }

    [Fact]
    public async Task Content_already_in_the_target_is_reported_and_left_behind()
    {
        var source = await CreateGroupAsync("Source");
        var target = await CreateGroupAsync("Target");
        var documentId = await AddPageAsync(source, 7);
        await AddPageAsync(target, 7); // identical bytes, so identical checksum

        var result = await _groups.MoveDocumentsAsync(
            source.Id, target.Id, [documentId], TestContext.Current.CancellationToken);

        Assert.Equal(0, result.MovedCount);
        Assert.Single(result.SkippedAsDuplicate);
        await using var db = new FgScannerDbContext(DbBootstrapper.BuildOptions(_dbPath));
        var stillInSource = await db.Documents
            .SingleAsync(d => d.Id == documentId, TestContext.Current.CancellationToken);
        Assert.Equal(source.Id, stillInSource.GroupId);
    }

    [Fact]
    public async Task A_name_clash_in_the_target_does_not_overwrite_the_existing_file()
    {
        var source = await CreateGroupAsync("Source");
        var target = await CreateGroupAsync("Target");
        await AddPageAsync(target, 2);            // occupies scan_00001.png
        var documentId = await AddPageAsync(source, 3); // also called scan_00001.png

        await _groups.MoveDocumentsAsync(
            source.Id, target.Id, [documentId], TestContext.Current.CancellationToken);

        Assert.Equal(2, Directory.GetFiles(target.DirectoryPath, "*.png").Length);
    }

    [Fact]
    public async Task Sequences_are_contiguous_in_both_groups_afterwards()
    {
        var source = await CreateGroupAsync("Source");
        var target = await CreateGroupAsync("Target");
        var first = await AddPageAsync(source, 1);
        await AddPageAsync(source, 2);
        var third = await AddPageAsync(source, 3);
        await AddPageAsync(target, 9);

        await _groups.MoveDocumentsAsync(
            source.Id, target.Id, [first, third], TestContext.Current.CancellationToken);

        await using var db = new FgScannerDbContext(DbBootstrapper.BuildOptions(_dbPath));
        var sourceSeq = await db.Documents.Where(d => d.GroupId == source.Id)
            .OrderBy(d => d.Sequence).Select(d => d.Sequence)
            .ToListAsync(TestContext.Current.CancellationToken);
        var targetSeq = await db.Documents.Where(d => d.GroupId == target.Id)
            .OrderBy(d => d.Sequence).Select(d => d.Sequence)
            .ToListAsync(TestContext.Current.CancellationToken);

        Assert.Equal([1], sourceSeq);
        Assert.Equal([1, 2, 3], targetSeq);
    }

    [Fact]
    public async Task Moving_into_the_same_group_is_refused()
    {
        var group = await CreateGroupAsync("Only");
        var documentId = await AddPageAsync(group, 1);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _groups.MoveDocumentsAsync(
                group.Id, group.Id, [documentId], TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Search_still_finds_moved_text_under_the_new_group()
    {
        var source = await CreateGroupAsync("Source");
        var target = await CreateGroupAsync("Target");
        var documentId = await AddPageAsync(source, 1);
        await using (var seed = new FgScannerDbContext(DbBootstrapper.BuildOptions(_dbPath)))
        {
            var page = await seed.Pages.SingleAsync(TestContext.Current.CancellationToken);
            page.OcrText = "tallmadge";
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await _groups.MoveDocumentsAsync(
            source.Id, target.Id, [documentId], TestContext.Current.CancellationToken);

        // FTS is external-content over Pages.OcrText; a move must update rows, never delete+insert,
        // or the index silently drifts out of step with the table.
        var hits = await new SearchService(new TestFactory(_dbPath))
            .SearchAsync("tallmadge", cancellationToken: TestContext.Current.CancellationToken);
        var hit = Assert.Single(hits);
        Assert.Equal("Target", hit.GroupName);
    }
}
