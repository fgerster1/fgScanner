using FgScanner.Core.Index;

namespace FgScanner.Core.Tests;

internal static class ExporterTestData
{
    public static readonly IReadOnlyList<IndexFieldDef> Fields =
    [
        new("Vendor", IndexFieldType.Text, Required: true),
        new("InvoiceDate", IndexFieldType.Date, Required: false),
        new("Amount", IndexFieldType.Number, Required: false),
        new("Category", IndexFieldType.List, Required: false),
    ];

    /// <summary>Deterministic dataset covering quoting, injection, unicode, newlines, and a 1000-char description.</summary>
    public static IndexExportData Build(params IndexFormat[] formats) => new(
        GroupName: "Invoices 2026",
        GroupDirectory: "", // set by tests
        ProfileName: "Accounting",
        SchemaVersion: 3,
        AppVersion: "1.2.3",
        GeneratedUtc: new DateTime(2026, 8, 20, 12, 0, 0, DateTimeKind.Utc),
        Fields: Fields,
        Formats: formats,
        Rows:
        [
            new IndexRow("scan_00001.png", "Yes", 96.4, "A plain description.", "Done", new Dictionary<string, string?>
            {
                ["Vendor"] = "Acme Corp",
                ["InvoiceDate"] = "2026-08-19",
                ["Amount"] = "1234.5",
                ["Category"] = "Utilities",
            }),
            new IndexRow("scan_00002.png", "Failed", null, "Comma, \"quotes\" and\r\na new line.", "Pending", new Dictionary<string, string?>
            {
                ["Vendor"] = "Müller & Söhne GmbH — 日本語",
                ["InvoiceDate"] = null,
                ["Amount"] = "-42.75",
                ["Category"] = null,
            }),
            new IndexRow("scan_00003.png", "No", null, "=1+2+cmd|' /C calc'!A0", "Skipped", new Dictionary<string, string?>
            {
                ["Vendor"] = "@SUM(A1:A9)",
                ["InvoiceDate"] = "2026-01-02",
                ["Amount"] = "0",
                ["Category"] = "Other",
            }),
            new IndexRow("scan_00004.png", "Yes", 21.3, new string('D', 1000), "Done", new Dictionary<string, string?>
            {
                ["Vendor"] = "Long Description Test",
                ["InvoiceDate"] = "2026-12-31",
                ["Amount"] = "99999.99",
                ["Category"] = "Archive",
            }),
        ]);
}
