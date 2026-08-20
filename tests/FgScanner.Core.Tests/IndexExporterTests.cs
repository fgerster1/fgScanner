using System.Globalization;
using System.Text;
using ClosedXML.Excel;
using FgScanner.Core.Index;
using Xunit;

namespace FgScanner.Core.Tests;

public sealed class IndexExporterTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "fgscanner-tests", Guid.NewGuid().ToString("N"));

    public IndexExporterTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private async Task<ExportResult> ExportAsync(params IndexFormat[] formats)
    {
        var data = ExporterTestData.Build(formats) with { GroupDirectory = _dir };
        return await new IndexExporter().ExportAsync(data, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Csv_output_matches_snapshot()
    {
        await ExportAsync(IndexFormat.Csv);
        await VerifyFile(Path.Combine(_dir, "index.csv"));
    }

    [Fact]
    public async Task Xml_output_matches_snapshot()
    {
        await ExportAsync(IndexFormat.Xml);
        await VerifyFile(Path.Combine(_dir, "index.xml"));
    }

    [Fact]
    public async Task Json_output_matches_snapshot()
    {
        await ExportAsync(IndexFormat.Json);
        await VerifyFile(Path.Combine(_dir, "index.json"));
    }

    [Fact]
    public async Task Manifest_matches_snapshot()
    {
        await ExportAsync(IndexFormat.Csv);
        await VerifyFile(Path.Combine(_dir, "manifest.json"));
    }

    [Fact]
    public async Task Csv_starts_with_utf8_bom_and_uses_crlf()
    {
        await ExportAsync(IndexFormat.Csv);
        var bytes = await File.ReadAllBytesAsync(Path.Combine(_dir, "index.csv"), TestContext.Current.CancellationToken);
        Assert.Equal([0xEF, 0xBB, 0xBF], bytes[..3]);
        var text = Encoding.UTF8.GetString(bytes);
        Assert.Contains("\r\n", text);
        Assert.DoesNotContain("\n\n", text.Replace("\r\n", "\n").Replace("\n", "\n")); // no stray bare LFs outside quoted cells
    }

    [Fact]
    public async Task Csv_neutralizes_formula_injection()
    {
        await ExportAsync(IndexFormat.Csv);
        var text = await File.ReadAllTextAsync(Path.Combine(_dir, "index.csv"), TestContext.Current.CancellationToken);
        Assert.Contains("'=1+2", text);   // leading = prefixed
        Assert.Contains("'@SUM", text);   // leading @ prefixed
        Assert.DoesNotContain("\"=1+2", text);
    }

    [Fact]
    public async Task Semicolon_delimiter_is_honored()
    {
        var data = ExporterTestData.Build(IndexFormat.Csv) with { GroupDirectory = _dir, CsvDelimiter = ';' };
        await new IndexExporter().ExportAsync(data, TestContext.Current.CancellationToken);
        var firstLine = (await File.ReadAllLinesAsync(Path.Combine(_dir, "index.csv"), TestContext.Current.CancellationToken))[0];
        Assert.Contains("Group;ImageName;OCRed", firstLine);
    }

    [Fact]
    public async Task Xlsx_round_trips_typed_cells()
    {
        await ExportAsync(IndexFormat.Xlsx);

        using var workbook = new XLWorkbook(Path.Combine(_dir, "index.xlsx"));
        var sheet = workbook.Worksheet("Index");

        Assert.Equal("Group", sheet.Cell(1, 1).GetString());
        Assert.Equal("Vendor", sheet.Cell(1, 6).GetString());

        // Row 2 = first data row. Date and number are REAL typed cells, not text.
        Assert.Equal(XLDataType.DateTime, sheet.Cell(2, 7).DataType);
        Assert.Equal(new DateTime(2026, 8, 19), sheet.Cell(2, 7).GetDateTime());
        Assert.Equal(XLDataType.Number, sheet.Cell(2, 8).DataType);
        Assert.Equal(1234.5, sheet.Cell(2, 8).GetDouble());

        // Injection row stays literal text, never a formula.
        var injectionCell = sheet.Cell(4, 4);
        Assert.Equal(XLDataType.Text, injectionCell.DataType);
        Assert.False(injectionCell.HasFormula);
        Assert.StartsWith("=1+2", injectionCell.GetString());

        // Unicode survives.
        Assert.Contains("日本語", sheet.Cell(3, 6).GetString());

        Assert.Equal(1, sheet.SheetView.SplitRow); // frozen header
        Assert.True(sheet.AutoFilter.IsEnabled);
    }

    [Fact]
    public async Task Xml_validates_against_committed_xsd()
    {
        await ExportAsync(IndexFormat.Xml);

        var xsdPath = FindRepoFile(Path.Combine("docs", "index-schema.xsd"));
        var settings = new System.Xml.XmlReaderSettings { ValidationType = System.Xml.ValidationType.Schema };
        settings.Schemas.Add(null, xsdPath);
        var errors = new List<string>();
        settings.ValidationEventHandler += (_, e) => errors.Add(e.Message);

        using var reader = System.Xml.XmlReader.Create(Path.Combine(_dir, "index.xml"), settings);
        while (reader.Read())
        {
        }

        Assert.Empty(errors);
    }

    [Fact]
    public async Task Locked_target_reports_locked_without_throwing_and_leaves_no_temp()
    {
        var target = Path.Combine(_dir, "index.csv");
        await File.WriteAllTextAsync(target, "old", TestContext.Current.CancellationToken);
        using var holder = new FileStream(target, FileMode.Open, FileAccess.Read, FileShare.None);

        var writer = new AtomicFileWriter(maxAttempts: 3, initialDelay: TimeSpan.FromMilliseconds(1));
        var data = ExporterTestData.Build(IndexFormat.Csv) with { GroupDirectory = _dir };
        var result = await new IndexExporter(writer).ExportAsync(data, TestContext.Current.CancellationToken);

        var csv = result.Results.Single(r => r.Format == IndexFormat.Csv && r.Path.EndsWith("index.csv", StringComparison.Ordinal));
        Assert.Equal(ExportOutcome.Locked, csv.Outcome);
        Assert.Contains("open in another program", csv.Message);
        Assert.False(File.Exists(target + ".tmp"));
    }

    [Fact]
    public async Task Lock_released_mid_retry_lets_the_write_succeed()
    {
        var target = Path.Combine(_dir, "index.csv");
        await File.WriteAllTextAsync(target, "old", TestContext.Current.CancellationToken);
        var holder = new FileStream(target, FileMode.Open, FileAccess.Read, FileShare.None);
        _ = Task.Run(async () =>
        {
            await Task.Delay(150);
            holder.Dispose();
        }, TestContext.Current.CancellationToken);

        var writer = new AtomicFileWriter(maxAttempts: 10, initialDelay: TimeSpan.FromMilliseconds(50));
        var data = ExporterTestData.Build(IndexFormat.Csv) with { GroupDirectory = _dir };
        var result = await new IndexExporter(writer).ExportAsync(data, TestContext.Current.CancellationToken);

        Assert.True(result.AllSucceeded);
        Assert.Contains("Invoices 2026", await File.ReadAllTextAsync(target, TestContext.Current.CancellationToken));
    }

    private static string FindRepoFile(string relative)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "FgScanner.slnx")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        return Path.Combine(dir.FullName, relative);
    }

    private static Task VerifyFile(string path) =>
        Verifier.VerifyFile(path)
            .UseDirectory("snapshots")
            .ScrubLinesContaining("\"directory\":"); // temp path differs per run
}
