using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Xml;
using ClosedXML.Excel;

namespace FgScanner.Core.Index;

internal interface IFormatWriter
{
    string FileName(IndexExportData data);

    Task WriteAsync(Stream stream, IndexExportData data);
}

internal static class ColumnOrder
{
    public const string Group = "Group";
    public const string ImageName = "ImageName";
    public const string Ocred = "OCRed";
    public const string AiDescription = "AIDescription";
    public const string AiStatus = "AIStatus";

    public static IEnumerable<string> Headers(IndexExportData data) =>
        new[] { Group, ImageName, Ocred, AiDescription, AiStatus }.Concat(data.Fields.Select(f => f.Name));

    public static IEnumerable<string?> Cells(IndexExportData data, IndexRow row) =>
        new[] { data.GroupName, row.ImageName, row.Ocred, row.AiDescription, row.AiStatus }
            .Concat(data.Fields.Select(f => row.CustomValues.GetValueOrDefault(f.Name)));
}

/// <summary>RFC 4180: UTF-8 with BOM, CRLF, quoting, "" escaping — plus formula-injection prefixing.</summary>
internal sealed class CsvFormatWriter : IFormatWriter
{
    public string FileName(IndexExportData data) => "index.csv";

    public async Task WriteAsync(Stream stream, IndexExportData data)
    {
        // Excel only detects UTF-8 when the BOM is present.
        await using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true), leaveOpen: true)
        {
            NewLine = "\r\n",
        };
        var delimiter = data.CsvDelimiter;
        await writer.WriteLineAsync(string.Join(delimiter, ColumnOrder.Headers(data).Select(h => Encode(h, delimiter)))).ConfigureAwait(false);
        foreach (var row in data.Rows)
        {
            await writer.WriteLineAsync(string.Join(delimiter, ColumnOrder.Cells(data, row).Select(c => Encode(c, delimiter)))).ConfigureAwait(false);
        }
    }

    internal static string Encode(string? value, char delimiter)
    {
        if (string.IsNullOrEmpty(value))
        {
            return "";
        }

        // OCR/AI text is attacker-printable; a leading = + - @ would execute as a formula in Excel.
        // Plain numbers ("-42.75") are exempt — they are data, not formulas.
        if (value[0] is '=' or '+' or '-' or '@' or '\t' or '\r'
            && !double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
        {
            value = "'" + value;
        }

        return value.Contains(delimiter) || value.Contains('"') || value.Contains('\r') || value.Contains('\n')
            ? '"' + value.Replace("\"", "\"\"") + '"'
            : value;
    }
}

/// <summary>Real Excel workbook: typed cells (dates as dates, numbers as numbers), frozen header, auto-filter.</summary>
internal sealed class XlsxFormatWriter : IFormatWriter
{
    public string FileName(IndexExportData data) => "index.xlsx";

    public Task WriteAsync(Stream stream, IndexExportData data)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Index");
        var headers = ColumnOrder.Headers(data).ToList();
        for (var c = 0; c < headers.Count; c++)
        {
            var cell = sheet.Cell(1, c + 1);
            cell.Value = headers[c];
            cell.Style.Font.Bold = true;
        }

        var fieldTypeByColumn = new Dictionary<int, IndexFieldType>();
        for (var i = 0; i < data.Fields.Count; i++)
        {
            fieldTypeByColumn[5 + i] = data.Fields[i].Type; // custom fields start after the 5 fixed columns
        }

        for (var r = 0; r < data.Rows.Count; r++)
        {
            var cells = ColumnOrder.Cells(data, data.Rows[r]).ToList();
            for (var c = 0; c < cells.Count; c++)
            {
                var cell = sheet.Cell(r + 2, c + 1);
                var value = cells[c];
                if (string.IsNullOrEmpty(value))
                {
                    continue;
                }

                switch (fieldTypeByColumn.GetValueOrDefault(c, IndexFieldType.Text))
                {
                    case IndexFieldType.Date when DateOnly.TryParseExact(value, "yyyy-MM-dd", out var date):
                        cell.Value = date.ToDateTime(TimeOnly.MinValue);
                        cell.Style.DateFormat.Format = "yyyy-mm-dd";
                        break;
                    case IndexFieldType.Number when double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number):
                        cell.Value = number;
                        break;
                    default:
                        cell.Value = value; // ClosedXML stores this as literal text — a leading '=' never becomes a formula
                        break;
                }
            }
        }

        sheet.SheetView.FreezeRows(1);
        if (data.Rows.Count > 0)
        {
            sheet.Range(1, 1, data.Rows.Count + 1, headers.Count).SetAutoFilter();
        }

        sheet.Columns().AdjustToContents(minWidth: 8, maxWidth: 60);
        workbook.SaveAs(stream);
        return Task.CompletedTask;
    }
}

/// <summary>XML per PLAN §5.2; docs/index-schema.xsd describes this shape.</summary>
internal sealed class XmlFormatWriter : IFormatWriter
{
    public string FileName(IndexExportData data) => "index.xml";

    public async Task WriteAsync(Stream stream, IndexExportData data)
    {
        var settings = new XmlWriterSettings { Async = true, Indent = true, Encoding = new UTF8Encoding(false) };
        await using var xml = XmlWriter.Create(stream, settings);
        await xml.WriteStartElementAsync(null, "fgIndex", null).ConfigureAwait(false);
        await xml.WriteAttributeStringAsync(null, "group", null, data.GroupName).ConfigureAwait(false);
        await xml.WriteAttributeStringAsync(null, "profile", null, data.ProfileName).ConfigureAwait(false);
        await xml.WriteAttributeStringAsync(null, "schemaVersion", null, data.SchemaVersion.ToString(CultureInfo.InvariantCulture)).ConfigureAwait(false);
        await xml.WriteAttributeStringAsync(null, "appVersion", null, data.AppVersion).ConfigureAwait(false);
        await xml.WriteAttributeStringAsync(null, "generatedUtc", null, data.GeneratedUtc.ToString("O")).ConfigureAwait(false);

        var sequence = 0;
        foreach (var row in data.Rows)
        {
            sequence++;
            await xml.WriteStartElementAsync(null, "document", null).ConfigureAwait(false);
            await xml.WriteAttributeStringAsync(null, "sequence", null, sequence.ToString(CultureInfo.InvariantCulture)).ConfigureAwait(false);
            await xml.WriteAttributeStringAsync(null, "image", null, row.ImageName).ConfigureAwait(false);
            await xml.WriteAttributeStringAsync(null, "ocred", null, row.Ocred).ConfigureAwait(false);
            await xml.WriteAttributeStringAsync(null, "aiStatus", null, row.AiStatus).ConfigureAwait(false);
            if (!string.IsNullOrEmpty(row.AiDescription))
            {
                await xml.WriteElementStringAsync(null, "aiDescription", null, row.AiDescription).ConfigureAwait(false);
            }

            foreach (var field in data.Fields)
            {
                await xml.WriteStartElementAsync(null, "field", null).ConfigureAwait(false);
                await xml.WriteAttributeStringAsync(null, "name", null, field.Name).ConfigureAwait(false);
                await xml.WriteAttributeStringAsync(null, "type", null, field.Type.ToString().ToLowerInvariant()).ConfigureAwait(false);
                await xml.WriteStringAsync(row.CustomValues.GetValueOrDefault(field.Name) ?? "").ConfigureAwait(false);
                await xml.WriteEndElementAsync().ConfigureAwait(false);
            }

            await xml.WriteEndElementAsync().ConfigureAwait(false);
        }

        await xml.WriteEndElementAsync().ConfigureAwait(false);
    }
}

/// <summary>JSON with the manifest embedded, for scripts and web tools.</summary>
internal sealed class JsonFormatWriter : IFormatWriter
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public string FileName(IndexExportData data) => "index.json";

    public async Task WriteAsync(Stream stream, IndexExportData data)
    {
        var payload = new
        {
            Manifest = ManifestBuilder.Build(data),
            Rows = data.Rows.Select(r => new
            {
                Group = data.GroupName,
                r.ImageName,
                OCRed = r.Ocred,
                AiDescription = r.AiDescription ?? "",
                r.AiStatus,
                Fields = data.Fields.ToDictionary(f => f.Name, f => r.CustomValues.GetValueOrDefault(f.Name) ?? ""),
            }),
        };
        await JsonSerializer.SerializeAsync(stream, payload, Options).ConfigureAwait(false);
    }
}

internal static class ManifestBuilder
{
    public static object Build(IndexExportData data) => new
    {
        Group = data.GroupName,
        Directory = data.GroupDirectory,
        Profile = data.ProfileName,
        data.SchemaVersion,
        data.AppVersion,
        GeneratedUtc = data.GeneratedUtc.ToString("O"),
        Formats = data.Formats.Select(f => f.ToString().ToLowerInvariant()),
        Fields = data.Fields.Select(f => new
        {
            f.Name,
            Type = f.Type.ToString().ToLowerInvariant(),
            f.Required,
        }),
    };
}
