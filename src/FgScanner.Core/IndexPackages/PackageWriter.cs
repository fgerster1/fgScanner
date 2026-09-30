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
/// positions). Every answer is validated here, before any file is written —
/// a batch Jim answered over days must never be refused by the portal for
/// a shape this writer could have caught at entry (SPEC-2026-008 AC-5).
/// </summary>
public static class PackageWriter
{
    /// <summary>The phase-2 doc-type-only surface, kept for the golden
    /// tests; delegates to the full surface and must stay byte-identical.</summary>
    public static void WriteResults(
        IndexPackage package, IReadOnlyList<DocTypeAnswer> answers, string outputPath)
    {
        foreach (var answer in answers)
        {
            // This surface emits fresh verdicts only: an empty or missing doc
            // type is a caller mistake here, never a withdrawal (main refused it).
            if (string.IsNullOrEmpty(answer.DocTypeId))
            {
                throw new ArgumentException(
                    $"answer for {answer.AnchorPageId} names no doc type");
            }
        }

        WriteResults(
            package,
            answers.Select(a => new IndexAnswer(
                a.AnchorPageId, IndexAnswerVocabulary.DocType, Qualifier: null,
                a.DocTypeId, a.DecidedBy, a.DecidedAt)).ToArray(),
            outputPath);
    }

    public static void WriteResults(
        IndexPackage package, IReadOnlyList<IndexAnswer> answers, string outputPath)
    {
        if (string.IsNullOrWhiteSpace(package.AppVersionAtOpen) || package.VocabularyVersion < 1)
        {
            // What gets echoed must pass the portal's schema — fail here,
            // never after Jim answered the whole batch.
            throw new ArgumentException(
                "the package carries no usable provenance (appVersion/vocabularyVersion)");
        }

        var validator = new AnswerValidator(package);
        foreach (var answer in answers)
        {
            validator.Validate(answer);
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
                writer.WriteString("field", answer.Field);
                if (answer.Qualifier is null)
                {
                    writer.WriteNull("qualifier");
                }
                else
                {
                    writer.WriteString("qualifier", answer.Qualifier);
                }

                writer.WriteNumber("seq", seq);
                writer.WriteString("value", answer.Value);
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
        if (outcome == Index.ExportOutcome.Locked)
        {
            // Not the writer's own Locked text: that was written for the group
            // index ("the data is safe in the database"), and no database holds
            // index answers.
            throw new IOException(
                $"\"{Path.GetFileName(outputPath)}\" is open in another program. " +
                "Close it and export again — nothing was written.");
        }

        if (outcome != Index.ExportOutcome.Success)
        {
            throw new IOException(
                $"\"{Path.GetFileName(outputPath)}\" could not be written ({message}). " +
                "Nothing was written; close any program holding it and export again.");
        }
    }

    /// <summary>
    /// The per-answer rules, against one package's own vocabularies — public
    /// so a UI can refuse an answer when it is ENTERED, not days later when
    /// the batch is exported (SPEC-2026-008 AC-5).
    /// </summary>
    public sealed class AnswerValidator
    {
        private readonly string _packageId;
        private readonly HashSet<string> _anchors;
        private readonly HashSet<string> _docTypes;
        private readonly HashSet<string> _subjects;
        private readonly HashSet<string> _people;

        public AnswerValidator(IndexPackage package)
        {
            _packageId = package.PackageId;
            _anchors = package.Documents.Select(d => d.AnchorPageId).ToHashSet(StringComparer.Ordinal);
            // Membership, not activity: vocabularies ship soft-deleted rows
            // precisely so old values stay valid ("any older vocabulary stays
            // importable forever"), and the portal importer accepts them.
            // Hiding inactive ids from a picker is the UI's job.
            _docTypes = package.DocTypes.Select(d => d.Id).ToHashSet(StringComparer.Ordinal);
            _subjects = package.Subjects.Select(s => s.Id).ToHashSet(StringComparer.Ordinal);
            _people = package.People.Select(p => p.Id).ToHashSet(StringComparer.Ordinal);
        }

        public void Validate(IndexAnswer answer) =>
            PackageWriter.Validate(answer, _packageId, _anchors, _docTypes, _subjects, _people);
    }

    private static void Validate(
        IndexAnswer answer, string packageId, HashSet<string> anchors,
        HashSet<string> docTypes, HashSet<string> subjects, HashSet<string> people)
    {
        if (answer.Value is null)
        {
            throw new ArgumentException(
                $"answer for {answer.AnchorPageId} has no value (use \"\" to withdraw)");
        }

        if (!anchors.Contains(answer.AnchorPageId))
        {
            throw new ArgumentException(
                $"answer for {answer.AnchorPageId}, which is not a document in {packageId}");
        }

        if (!IndexAnswerVocabulary.Fields.Contains(answer.Field))
        {
            throw new ArgumentException(
                $"\"{answer.Field}\" is not an index answer field this contract carries");
        }

        // The qualifier names the decision slot (anchor, field, qualifier),
        // so it is validated even on a withdrawal.
        switch (answer.Field)
        {
            case IndexAnswerVocabulary.Person:
                if (answer.Qualifier is null
                    || !IndexAnswerVocabulary.PersonQualifiers.Contains(answer.Qualifier))
                {
                    throw new ArgumentException(
                        $"a person answer needs a qualifier from " +
                        $"{{{string.Join(", ", IndexAnswerVocabulary.PersonQualifiers)}}}, " +
                        $"got \"{answer.Qualifier}\"");
                }

                break;
            case IndexAnswerVocabulary.Date:
                if (answer.Qualifier is null
                    || !IndexAnswerVocabulary.DateQualifiers.Contains(answer.Qualifier))
                {
                    throw new ArgumentException(
                        $"a date answer needs a qualifier from " +
                        $"{{{string.Join(", ", IndexAnswerVocabulary.DateQualifiers)}}}, " +
                        $"got \"{answer.Qualifier}\"");
                }

                break;
            default:
                if (answer.Qualifier is not null)
                {
                    throw new ArgumentException(
                        $"a {answer.Field} answer carries no qualifier, got \"{answer.Qualifier}\"");
                }

                break;
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

        if (answer.Value.Length == 0)
        {
            if (answer.Field is IndexAnswerVocabulary.Person or IndexAnswerVocabulary.Subject)
            {
                // Multi-value slots include the value (SPEC-2026-003 §07 as
                // amended 2026-09-30), so an empty one cannot say WHICH person
                // or subject it withdraws; removal there is the portal's job.
                throw new ArgumentException(
                    $"an empty {answer.Field} answer cannot name what it withdraws");
            }

            // A single-value withdrawal: the empty value is the contract's own
            // spelling for "remove the current decision in this slot".
            return;
        }

        switch (answer.Field)
        {
            case IndexAnswerVocabulary.DocType when !docTypes.Contains(answer.Value):
                throw new ArgumentException(
                    $"\"{answer.Value}\" is not a doc type in this package's vocabulary");
            case IndexAnswerVocabulary.Subject when !subjects.Contains(answer.Value):
                throw new ArgumentException(
                    $"\"{answer.Value}\" is not a subject in this package's vocabulary");
            case IndexAnswerVocabulary.Person when !people.Contains(answer.Value):
                throw new ArgumentException(
                    $"\"{answer.Value}\" is not a person in this package's vocabulary");
            case IndexAnswerVocabulary.Date when !DateOnly.TryParseExact(
                answer.Value, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out _):
                throw new ArgumentException(
                    $"a date answer must be yyyy-MM-dd and a real calendar date, " +
                    $"got \"{answer.Value}\"");
            case IndexAnswerVocabulary.KeyFlag when answer.Value != IndexAnswerVocabulary.KeyFlagTrue:
                throw new ArgumentException(
                    $"a key_flag answer is \"{IndexAnswerVocabulary.KeyFlagTrue}\" or a " +
                    $"withdrawal, got \"{answer.Value}\"");
        }
    }
}
