using System.Globalization;
using FgScanner.Core.Evidence;

namespace FgScanner.Core.Capture;

/// <summary>One sheet's fields that decide which document it belongs to, in Sequence order.</summary>
public sealed record DocumentRunRow(
    string? Title, string? DocType, string? DocDate, string? DocNo, string? NoteState);

/// <summary>
/// Proposes DocNo for a group's sheets (SPEC-2026-009 Q1). The portal import makes each run of
/// consecutive sheets sharing a DocNo one document and mints permanent page ids from that, so
/// this only proposes — the operator sees the runs and applies them. Operators repeat a Title
/// across a document's sheets, which is the signal read here, together with DocType and DocDate.
/// </summary>
public static class DocumentRunProposer
{
    /// <summary>The DocNo each row should carry; a row already typed keeps its own value.</summary>
    public static IReadOnlyList<string> Propose(IReadOnlyList<DocumentRunRow> rows)
    {
        var result = new string[rows.Count];
        var next = rows
            .Select(r => int.TryParse(r.DocNo, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : 0)
            .DefaultIfEmpty(0)
            .Max() + 1;

        DocumentRunRow? runKey = null;
        var current = 0;
        foreach (var unit in Units(rows))
        {
            var typed = rows[unit[0]].DocNo;
            if (!string.IsNullOrWhiteSpace(typed))
            {
                // A typed number is the operator's own boundary; nothing joins it by guess.
                foreach (var i in unit)
                {
                    result[i] = string.IsNullOrWhiteSpace(rows[i].DocNo) ? typed : rows[i].DocNo!;
                }

                runKey = null;
                continue;
            }

            var key = rows[unit[^1]];
            if (runKey is null || !Continues(runKey, key))
            {
                current = next++;
                runKey = key;
            }
            else if (string.IsNullOrWhiteSpace(runKey.Title))
            {
                runKey = runKey with { Title = key.Title };
            }

            foreach (var i in unit)
            {
                result[i] = current.ToString(CultureInfo.InvariantCulture);
            }
        }

        return result;
    }

    // An annotated sheet is captured as-found (and maybe its note's face) before it is captured
    // clean; the import refuses a whole group if a run boundary falls between them, so they move
    // as one unit, keyed by the clean sheet, which is the document's page.
    private static IEnumerable<List<int>> Units(IReadOnlyList<DocumentRunRow> rows)
    {
        var unit = new List<int>();
        for (var i = 0; i < rows.Count; i++)
        {
            unit.Add(i);
            if (rows[i].NoteState is AnnotatedCaptureSequence.AsFound or AnnotatedCaptureSequence.NoteFace)
            {
                continue;
            }

            yield return unit;
            unit = [];
        }

        if (unit.Count > 0)
        {
            yield return unit;
        }
    }

    private static bool Continues(DocumentRunRow run, DocumentRunRow sheet) =>
        Same(run.DocType, sheet.DocType)
        && Same(run.DocDate, sheet.DocDate)
        && (string.IsNullOrWhiteSpace(sheet.Title) || Same(run.Title, sheet.Title));

    private static bool Same(string? a, string? b) =>
        string.Equals(a?.Trim() ?? "", b?.Trim() ?? "", StringComparison.Ordinal);
}
