using FgScanner.Data;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FgScanner.Data.Tests;

/// <summary>
/// Regression cover for BUG-5: editing a page's image left its OCR text, confidence and FTS entry
/// describing the pre-edit image, with nothing marking them stale. Rotating three upside-down pages
/// on 2026-08-24 left sidecars from the misfed scan in place — the grid still claimed "Yes", search
/// still returned the reversed text, and only a manual Re-OCR corrected it.
/// </summary>
public sealed class EditedPageOcrTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "fgscanner-tests", Guid.NewGuid().ToString("N"));
    private readonly string _dbPath;
    private readonly GroupService _groupService;
    private readonly OcrQueueService _ocrQueue;

    public EditedPageOcrTests()
    {
        Directory.CreateDirectory(_root);
        _dbPath = Path.Combine(_root, "test.db");
        using (var db = new FgScannerDbContext(DbBootstrapper.BuildOptions(_dbPath)))
        {
            db.Database.Migrate();
        }

        _groupService = new GroupService(new TestFactory(_dbPath));
        _ocrQueue = new OcrQueueService(new TestFactory(_dbPath));
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

    private async Task<Guid> CreateOcredPageAsync(OcrStatus status = OcrStatus.Yes)
    {
        var group = await _groupService.CreateGroupAsync(_root, "G", null, TestContext.Current.CancellationToken);
        var staging = Directory.CreateDirectory(Path.Combine(_root, Guid.NewGuid().ToString("N"))).FullName;
        var file = Path.Combine(staging, "scan_00001.png");
        await File.WriteAllBytesAsync(file, [1, 2, 3], TestContext.Current.CancellationToken);
        await _groupService.AdoptPagesAsync(group.Id, [file], _ => false, TestContext.Current.CancellationToken);

        await using var db = new FgScannerDbContext(DbBootstrapper.BuildOptions(_dbPath));
        var page = await db.Pages.SingleAsync(TestContext.Current.CancellationToken);
        page.OcrStatus = status;
        page.OcrText = "text recognised from the pre-edit image";
        page.OcrMeanConfidence = 29.7;
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return page.Id;
    }

    [Fact]
    public async Task Editing_an_ocred_page_drops_the_stale_text_and_requeues_it()
    {
        var pageId = await CreateOcredPageAsync();

        var invalidated = await _ocrQueue.ReOcrEditedPageAsync(pageId, TestContext.Current.CancellationToken);

        Assert.True(invalidated);
        await using var db = new FgScannerDbContext(DbBootstrapper.BuildOptions(_dbPath));
        var page = await db.Pages.SingleAsync(TestContext.Current.CancellationToken);
        Assert.Null(page.OcrText);
        Assert.Null(page.OcrMeanConfidence);
        Assert.Equal(OcrStatus.Pending, page.OcrStatus);
        Assert.True(await db.Jobs.AnyAsync(
            j => j.PageId == pageId && j.Type == JobType.Ocr && j.State == JobState.Pending,
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_previously_failed_page_is_also_retried_after_an_edit()
    {
        var pageId = await CreateOcredPageAsync(OcrStatus.Failed);

        Assert.True(await _ocrQueue.ReOcrEditedPageAsync(pageId, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_page_that_never_had_ocr_is_left_alone()
    {
        var pageId = await CreateOcredPageAsync(OcrStatus.No);

        // Nothing to invalidate: re-OCRing here would impose OCR on a group whose profile may not
        // want it, purely because the user rotated a page.
        Assert.False(await _ocrQueue.ReOcrEditedPageAsync(pageId, TestContext.Current.CancellationToken));
        await using var db = new FgScannerDbContext(DbBootstrapper.BuildOptions(_dbPath));
        Assert.Empty(await db.Jobs.ToListAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Editing_the_same_page_twice_does_not_queue_it_twice()
    {
        var pageId = await CreateOcredPageAsync();

        await _ocrQueue.ReOcrEditedPageAsync(pageId, TestContext.Current.CancellationToken);
        await _ocrQueue.ReOcrEditedPageAsync(pageId, TestContext.Current.CancellationToken);

        await using var db = new FgScannerDbContext(DbBootstrapper.BuildOptions(_dbPath));
        Assert.Single(await db.Jobs.Where(j => j.PageId == pageId).ToListAsync(TestContext.Current.CancellationToken));
    }
}
