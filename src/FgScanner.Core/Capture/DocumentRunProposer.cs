using System.Globalization;
using FgScanner.Core.Evidence;

namespace FgScanner.Core.Capture;

/// <summary>One sheet's fields that decide which document it belongs to, in Sequence order.</summary>
public sealed record DocumentRunRow(
    string? Title, string? DocType, string? DocDate, string? DocNo, string? NoteState);

/// <summary>
/// The DocNo for every row, or — when any annotated sheet is malformed — no DocNos and the row
/// indexes that must be fixed first.
/// </summary>
public sealed record DocumentProposal(IReadOnlyList<string> DocNos, IReadOnlyList<int> Malformed);

/// <summary>
/// Proposes DocNo for a group's sheets (SPEC-2026-009 Q1). The portal import makes each run of
/// consecutive sheets sharing a DocNo one document and mints permanent page ids from that, so
/// this only proposes — the operator sees the runs and applies them. Operators repeat a Title
/// across a document's sheets, which is the signal read here, together with DocType and DocDate.
/// </summary>
public static class DocumentRunProposer
{
    private sealed record Unit(List<int> Rows, string? Typed, DocumentRunRow Key);

    public static DocumentProposal Propose(IReadOnlyList<DocumentRunRow> rows)
    {
        var (units, malformed) = Units(rows);
        if (malformed.Count > 0)
        {
            return new DocumentProposal([], malformed);
        }

        var result = new string[rows.Count];
        var next = rows
            .Select(r => int.TryParse(r.DocNo, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : 0)
            .DefaultIfEmpty(0)
            .Max() + 1;

        foreach (var run in Runs(units))
        {
            // A typed number is the operator naming this document, so the run's blank sheets
            // take it — those before the first typed sheet too (R-D5). A second, different
            // typed number in the run is a boundary the operator drew.
            var current = run.FirstOrDefault(u => u.Typed is not null)?.Typed
                ?? (next++).ToString(CultureInfo.InvariantCulture);
            foreach (var unit in run)
            {
                current = unit.Typed ?? current;
                foreach (var i in unit.Rows)
                {
                    result[i] = current;
                }
            }
        }

        return new DocumentProposal(result, []);
    }

    /// <summary>
    /// The documents the portal import will make of these rows (fgscanner_group.documents): a
    /// run of consecutive record sheets sharing a DocNo is one, and every annotation capture is
    /// one of its own, linked to its clean sheet — never a page of the record.
    /// </summary>
    public static (int Records, int Notes) ImportDocuments(
        IReadOnlyList<DocumentRunRow> rows, IReadOnlyList<string> docNos)
    {
        var records = 0;
        var notes = 0;
        string? lastRecord = null;
        for (var i = 0; i < rows.Count; i++)
        {
            if (IsAnnotation(rows[i].NoteState))
            {
                notes++;
                continue;
            }

            var docNo = string.IsNullOrWhiteSpace(docNos[i]) ? null : docNos[i];
            if (docNo is null || docNo != lastRecord)
            {
                records++;
            }

            lastRecord = docNo;
        }

        return (records, notes);
    }

    // An annotated sheet is captured as-found (and maybe its note's face) before it is captured
    // clean. The importer pairs each annotation capture with the next clean sheet in its DocNo
    // run and refuses a whole group when one has none (fgscanner_group._subject_of), so the
    // capture through its clean sheet is one unit, keyed by the clean sheet — the document's
    // page. A shape the capture sequence cannot produce is reported, never numbered: guessing
    // either glues a stack into one document or orphans a note (R-D1).
    private static (List<Unit> Units, List<int> Malformed) Units(IReadOnlyList<DocumentRunRow> rows)
    {
        var units = new List<Unit>();
        var malformed = new SortedSet<int>();
        List<int>? open = null;

        void Close(List<int> members)
        {
            var typed = members
                .Select(i => rows[i].DocNo?.Trim())
                .Where(d => !string.IsNullOrEmpty(d))
                .Distinct(StringComparer.Ordinal)
                .ToList();
            if (typed.Count > 1)
            {
                malformed.UnionWith(members);
                return;
            }

            units.Add(new Unit(members, typed.SingleOrDefault(), rows[members[^1]]));
        }

        void Orphan(List<int> members) =>
            malformed.UnionWith(members.Where(i => IsAnnotation(rows[i].NoteState)));

        for (var i = 0; i < rows.Count; i++)
        {
            var state = string.IsNullOrWhiteSpace(rows[i].NoteState) ? null : rows[i].NoteState;
            if (state is not (null or AnnotatedCaptureSequence.AsFound or AnnotatedCaptureSequence.NoteFace
                or AnnotatedCaptureSequence.Clean))
            {
                malformed.Add(i);
                continue;
            }

            if (state == AnnotatedCaptureSequence.AsFound)
            {
                if (open is not null)
                {
                    Orphan(open);
                }

                open = [i];
                continue;
            }

            if (open is not null)
            {
                open.Add(i);
                if (state == AnnotatedCaptureSequence.Clean)
                {
                    Close(open);
                    open = null;
                }

                continue;
            }

            if (state == AnnotatedCaptureSequence.NoteFace)
            {
                open = [i];
                continue;
            }

            Close([i]);
        }

        if (open is not null)
        {
            Orphan(open);
        }

        return (units, [.. malformed]);
    }

    private static List<List<Unit>> Runs(List<Unit> units)
    {
        var runs = new List<List<Unit>>();
        DocumentRunRow? runKey = null;
        foreach (var unit in units)
        {
            // A sheet nobody described — typically a kept duplex back — is a page of the
            // document before it, and must not become the key the next front is compared to.
            if (Undescribed(unit.Key) && runs.Count > 0)
            {
                runs[^1].Add(unit);
                continue;
            }

            if (runKey is null || !Continues(runKey, unit.Key))
            {
                runs.Add([unit]);
                runKey = Undescribed(unit.Key) ? null : unit.Key;
                continue;
            }

            runs[^1].Add(unit);
        }

        return runs;
    }

    private static bool IsAnnotation(string? state) =>
        state is AnnotatedCaptureSequence.AsFound or AnnotatedCaptureSequence.NoteFace;

    private static bool Undescribed(DocumentRunRow row) =>
        string.IsNullOrWhiteSpace(row.Title)
        && string.IsNullOrWhiteSpace(row.DocType)
        && string.IsNullOrWhiteSpace(row.DocDate);

    private static bool Continues(DocumentRunRow run, DocumentRunRow sheet) =>
        Same(run.DocType, sheet.DocType)
        && Same(run.DocDate, sheet.DocDate)
        && (string.IsNullOrWhiteSpace(sheet.Title) || Same(run.Title, sheet.Title));

    private static bool Same(string? a, string? b) =>
        string.Equals(a?.Trim() ?? "", b?.Trim() ?? "", StringComparison.Ordinal);
}
