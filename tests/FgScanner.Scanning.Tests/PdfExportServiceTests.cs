using System.Text;
using FgScanner.Scanning.Export;
using FgScanner.Scanning.Import;
using Xunit;

namespace FgScanner.Scanning.Tests;

public sealed class PdfExportServiceTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("fgs-pdf").FullName;
    private readonly PdfExportService _service = new();
    private readonly List<string> _pages;

    public PdfExportServiceTests()
    {
        _pages =
        [
            TestImages.CreateLinedPage(_dir, "p1.png"),
            TestImages.CreateLinedPage(_dir, "p2.png", width: 500, height: 700),
        ];
    }

    public void Dispose()
    {
        _service.Dispose();
        Directory.Delete(_dir, recursive: true);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>Raw bytes as latin-1 text for marker checks (safe for structural tokens).</summary>
    private static string RawText(string path) => Encoding.Latin1.GetString(File.ReadAllBytes(path));

    private async Task<int> CountPagesByReimportAsync(string pdfPath, string? password = null)
    {
        using var import = new FileImportService();
        var storage = new FakePageStorage(Path.Combine(_dir, "reimport-" + Guid.NewGuid().ToString("N")));
        var count = 0;
        await foreach (var _ in import.ImportAsync(pdfPath, storage, password, Ct))
        {
            count++;
        }

        return count;
    }

    [Fact]
    public async Task Export_produces_valid_pdf_with_metadata_snapshot()
    {
        var pdfPath = Path.Combine(_dir, "out.pdf");

        await _service.ExportAsync(_pages, pdfPath, new PdfExportOptions
        {
            Title = "FG Test Title",
            Author = "FG Author",
            Subject = "FG Subject",
            Keywords = "alpha, beta",
        }, Ct);

        var raw = RawText(pdfPath);
        var summary = new
        {
            Header = raw[..8],
            PageCount = await CountPagesByReimportAsync(pdfPath),
            HasTitle = raw.Contains("FG Test Title", StringComparison.Ordinal),
            HasAuthor = raw.Contains("FG Author", StringComparison.Ordinal),
            HasSubject = raw.Contains("FG Subject", StringComparison.Ordinal),
            HasKeywords = raw.Contains("alpha, beta", StringComparison.Ordinal),
            Encrypted = raw.Contains("/Encrypt", StringComparison.Ordinal),
            PdfAMarker = raw.Contains("pdfaid", StringComparison.Ordinal),
        };
        // CreationDate/ModDate/ID never enter the summary, so no scrubbing is needed.
        await Verify(summary).UseDirectory("snapshots");
    }

    [Theory]
    [InlineData(PdfCompatLevel.PdfA1B, "1.4")]
    [InlineData(PdfCompatLevel.PdfA2B, "1.7")]
    [InlineData(PdfCompatLevel.PdfA3B, "1.7")]
    [InlineData(PdfCompatLevel.PdfA3U, "1.7")]
    public async Task PdfA_levels_emit_conformance_metadata(PdfCompatLevel level, string version)
    {
        var pdfPath = Path.Combine(_dir, $"a-{level}.pdf");

        await _service.ExportAsync(_pages, pdfPath, new PdfExportOptions { Compat = level }, Ct);

        var raw = RawText(pdfPath);
        Assert.StartsWith($"%PDF-{version}", raw);
        Assert.Contains("pdfaid", raw, StringComparison.Ordinal);
        Assert.Equal(2, await CountPagesByReimportAsync(pdfPath));
    }

    [Fact]
    public async Task Encryption_locks_the_file_and_password_opens_it()
    {
        var pdfPath = Path.Combine(_dir, "locked.pdf");

        await _service.ExportAsync(_pages, pdfPath, new PdfExportOptions
        {
            Security = new PdfSecurity
            {
                OwnerPassword = "owner-secret",
                UserPassword = "user-secret",
                AllowPrinting = false,
                AllowContentCopying = false,
            },
        }, Ct);

        Assert.Contains("/Encrypt", RawText(pdfPath), StringComparison.Ordinal);
        Assert.Equal(2, await CountPagesByReimportAsync(pdfPath, "user-secret"));
        await Assert.ThrowsAnyAsync<Exception>(() => CountPagesByReimportAsync(pdfPath, "wrong"));
    }

    [Fact]
    public async Task Default_export_round_trips_page_count()
    {
        var pdfPath = Path.Combine(_dir, "plain.pdf");

        await _service.ExportAsync(_pages, pdfPath, new PdfExportOptions(), Ct);

        Assert.Equal(2, await CountPagesByReimportAsync(pdfPath));
    }
}
