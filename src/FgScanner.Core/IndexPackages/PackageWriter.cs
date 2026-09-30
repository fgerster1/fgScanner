using System.Text.Encodings.Web;
using System.Text.Json;

namespace FgScanner.Core.IndexPackages;

/// <summary>
/// Writes results.json under the contract's byte rules (vendored README):
/// sorted keys, two-space indent, UTF-8 without BOM, LF, one trailing
/// newline — byte-identical to what Python's exporter-side json.dumps
/// produces, which is what makes the golden comparison possible at all.
/// </summary>
public static class PackageWriter
{
    public static void WriteResults(
        IndexPackage package, IReadOnlyList<DocTypeAnswer> answers, string outputPath)
    {
        var anchors = package.Documents.Select(d => d.AnchorPageId).ToHashSet(StringComparer.Ordinal);
        var docTypes = package.DocTypes.Where(d => d.Active)
            .Select(d => d.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var answer in answers)
        {
            if (!anchors.Contains(answer.AnchorPageId))
            {
                throw new ArgumentException(
                    $"answer for {answer.AnchorPageId}, which is not a document in {package.PackageId}");
            }

            if (!docTypes.Contains(answer.DocTypeId))
            {
                throw new ArgumentException(
                    $"\"{answer.DocTypeId}\" is not an active doc type in this package's vocabulary");
            }
        }

        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions
        {
            Indented = true,
            NewLine = "\n",
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        }))
        {
            // Keys in ordinal order at every level — the sort_keys=True half
            // of the byte rule.
            writer.WriteStartObject();
            writer.WriteStartArray("answers");
            var seq = 0;
            foreach (var answer in answers)
            {
                seq++;
                writer.WriteStartObject();
                writer.WriteString("anchorPageId", answer.AnchorPageId);
                writer.WriteString("decidedAt",
                    answer.DecidedAt.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'",
                        System.Globalization.CultureInfo.InvariantCulture));
                writer.WriteString("decidedBy", answer.DecidedBy);
                writer.WriteString("field", "doc_type");
                writer.WriteNull("qualifier");
                writer.WriteNumber("seq", seq);
                writer.WriteString("value", answer.DocTypeId);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteString("appVersion", package.AppVersionAtOpen);
            writer.WriteNumber("formatVersion", package.FormatVersion);
            writer.WriteString("packageChecksum", package.PackageChecksum);
            writer.WriteString("packageId", package.PackageId);
            writer.WriteNumber("vocabularyVersion", package.VocabularyVersion);
            writer.WriteEndObject();
        }

        buffer.WriteByte((byte)'\n');
        File.WriteAllBytes(outputPath, buffer.ToArray());
    }
}
