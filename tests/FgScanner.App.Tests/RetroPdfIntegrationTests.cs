using System.Drawing;
using System.IO;
using FgScanner.App.Services;
using FgScanner.Data;
using FgScanner.Scanning.Export;
using FgScanner.Scanning.Import;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FgScanner.App.Tests;

/// <summary>Real Pdfium end-to-end: a PDF dropped in a folder becomes registered page images.</summary>
public sealed class RetroPdfIntegrationTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("fgs-retro-int").FullName;
    private readonly FileImportService _fileImport = new();

    public void Dispose()
    {
        _fileImport.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed class Factory(string dbPath) : IDbContextFactory<FgScannerDbContext>
    {
        public FgScannerDbContext CreateDbContext() => new(DbBootstrapper.BuildOptions(dbPath));
    }

    private static string DrawPage(string directory, string name, int width, int height)
    {
        using var bitmap = new Bitmap(width, height);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(Color.White);
            using var brush = new SolidBrush(Color.Black);
            for (var y = 60; y < height - 60; y += 40)
            {
                graphics.FillRectangle(brush, 40, y, width - 80, 12);
            }
        }

        var path = Path.Combine(directory, name);
        bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png);
        return path;
    }

    [Fact]
    public async Task Pdf_in_folder_is_rendered_by_pdfium_and_registered_idempotently()
    {
        var dbPath = Path.Combine(_root, "test.db");
        var factory = new Factory(dbPath);
        await using (var db = factory.CreateDbContext())
        {
            await db.Database.MigrateAsync(Ct);
        }

        var groups = new GroupService(factory);
        var retro = new RetroProcessService(
            factory, groups, new TrashService(factory, Path.Combine(_root, "trash")),
            new FgScanner.Scanning.Import.Naps2PdfRenderer(_fileImport));

        // A real 2-page PDF via the phase-4 exporter.
        var folder = Path.Combine(_root, "OldRecords");
        Directory.CreateDirectory(folder);
        var imageDir = Path.Combine(_root, "src");
        Directory.CreateDirectory(imageDir);
        var pageA = DrawPage(imageDir, "a.png", 600, 800);
        var pageB = DrawPage(imageDir, "b.png", 500, 700);
        using (var pdfExport = new PdfExportService())
        {
            await pdfExport.ExportAsync(
                [pageA, pageB], Path.Combine(folder, "records.pdf"), new PdfExportOptions(), Ct);
        }

        var report = await retro.ProcessFolderAsync(folder, null, Ct);

        Assert.Equal(2, report.AdoptedPdfPages);
        var pages = await groups.GetPagesAsync(report.GroupId, Ct);
        Assert.Equal(
            ["records_page_001.png", "records_page_002.png"],
            pages.Select(p => p.FileName).Order());
        Assert.All(pages, p => Assert.True(File.Exists(Path.Combine(folder, p.FileName))));

        var second = await retro.ProcessFolderAsync(folder, null, Ct);
        Assert.False(second.ChangedAnything);
        Assert.Equal(2, (await groups.GetPagesAsync(report.GroupId, Ct)).Count);
    }
}
