using System.Text.Encodings.Web;
using System.Text.Json;

namespace FgScanner.Core.IndexPackages;

/// <summary>
/// Writes results.json under the contract's byte rules (vendored README):
/// sorted keys, two-space indent, UTF-8 without BOM, LF, one trailing
/// newline — byte-identical to what Python's exporter-side json.dumps
/// produces, which is what makes the golden comparison possible at all.
/// `seq` is positional within THIS file; an answer's identity at the portal
/// is its full content, so a revised answer that keeps its position still
/// imports as a new decision (the importer dedupes exact rows, never
/// positions). This slice cannot express a withdrawal (an empty value):
/// deliberate scope — the writer emits fresh doc_type verdicts only, and
/// the withdraw capability arrives with the phase-4 UI, test-first
/// (JimsStuff SPEC-2026-005 contract-slice, §22 finding 5).
/// </summary>
public static class PackageWriter
{
    public static void WriteResults(
        IndexPackage package, IReadOnlyList<DocTypeAnswer> answers, string outputPath)
    {
        if (string.IsNullOrWhiteSpace(package.AppVersionAtOpen) || package.VocabularyVersion < 1)
        {
            // What gets echoed must pass the portal's schema — fail here,
            // never after Jim answered the whole batch.
            throw new ArgumentException(
                "the package carries no usable provenance (appVersion/vocabularyVersion)");
        }

        var anchors = package.Documents.Select(d => d.AnchorPageId).ToHashSet(StringComparer.Ordinal);
        // Membership, not activity: vocabularies ship soft-deleted rows
        // precisely so old values stay valid ("any older vocabulary stays
        // importable forever"), and the portal importer accepts them.
        // Hiding inactive ids from a picker is the phase-4 UI's job.
        var docTypes = package.DocTypes.Select(d => d.Id).ToHashSet(StringComparer.Ordinal);
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
                    $"\"{answer.DocTypeId}\" is not a doc type in this package's vocabulary");
            }

            if (string.IsNullOrWhiteSpace(answer.DecidedBy))
            {
                // results.schema.json requires decidedBy minLength 1 — fail
                // here, not at the portal after the whole batch is answered.
                throw new ArgumentException(
                    $"answer for {answer.AnchorPageId} names no decider (decidedBy is empty)");
            }

            foreach (var ch in answer.DecidedBy)
            {
                // The one free-text field: .NET's encoder and Python's
                // json.dumps agree byte-for-byte on ordinary BMP letters
                // and punctuation but not on controls, line separators,
                // exotic spaces (NBSP pasted from Word, the French narrow
                // NBSP), BOMs, surrogate pairs, or private-use/unassigned
                // code points — those would break the contract's byte
                // rule, so a decider name is plain text or refused.
                var category = char.GetUnicodeCategory(ch);
                if (char.IsControl(ch) || char.IsSurrogate(ch)
                    || (char.IsWhiteSpace(ch) && ch != ' ')
                    || category is System.Globalization.UnicodeCategory.Format
                        or System.Globalization.UnicodeCategory.PrivateUse
                        or System.Globalization.UnicodeCategory.OtherNotAssigned)
                {
                    throw new ArgumentException(
                        $"decidedBy \"{answer.DecidedBy}\" contains a character that cannot " +
                        "round-trip the contract's byte rules — use plain text");
                }
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

        // Atomic write through the ONE house implementation
        // (FgScanner.Core.Index.AtomicFileWriter): temp beside the target,
        // replace, lock-retry with backoff — a crash mid-write must never
        // destroy a previous complete results.json, and a sync client or
        // AV holding the file gets retries and then an operator-facing
        // message instead of a raw first-attempt IOException.
        var bytes = buffer.ToArray();
        var (outcome, message) = new Index.AtomicFileWriter()
            .WriteAsync(outputPath, stream => stream.WriteAsync(bytes, 0, bytes.Length))
            .GetAwaiter().GetResult();
        if (outcome != Index.ExportOutcome.Success)
        {
            throw new IOException(message ?? "results.json could not be written");
        }
    }
}
