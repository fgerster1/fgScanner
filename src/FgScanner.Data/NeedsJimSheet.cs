using System.Globalization;
using System.Text;
using System.Text.Json;
using FgScanner.Core.Capture;
using FgScanner.Core.Station;
using Microsoft.Data.Sqlite;

namespace FgScanner.Data;

/// <summary>
/// <c>needs-jim.txt</c> (SPEC-SCANNER-2026-010 Q-B8): the groups Franz could not finish without
/// the paper, why each is still open — the importer's own refusal text, so Jim and Franz read the
/// same words the portal would — and what to look for in the box.
/// </summary>
public static class NeedsJimSheet
{
    public const string FileName = "needs-jim.txt";

    private const int CommittedState = 2;

    private const int MaxProblemsPerGroup = 20;

    public static string Write(string database, DateTimeOffset at)
    {
        using var connection = SqliteStationDatabase.Open(database, SqliteOpenMode.ReadOnly);
        var groups = new List<(string Id, string Name, string BatchFields)>();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT Id, Name, BatchFieldsJson FROM Groups WHERE State <> @committed ORDER BY Name";
            command.Parameters.AddWithValue("@committed", CommittedState);
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                groups.Add((reader.GetString(0), reader.GetString(1), reader.GetString(2)));
            }
        }

        var date = at.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var sheet = new StringBuilder();
        if (groups.Count == 0)
        {
            return sheet.Append(CultureInfo.InvariantCulture, $"FROM FRANZ — {date}\n\nNothing needs you — every group is finished.\n").ToString();
        }

        sheet.Append(CultureInfo.InvariantCulture, $"WHAT FRANZ NEEDS FROM YOU — {date}\n\n")
            .Append("These groups are still open. Each one needs something only the paper can answer.\n");
        var number = 0;
        foreach (var (id, name, batchFields) in groups)
        {
            var rows = AnnotationRows(connection, id);
            sheet.Append(CultureInfo.InvariantCulture, $"\n{++number}. {name}  ({rows.Count} page{(rows.Count == 1 ? "" : "s")})\n");
            var asked = false;
            if (string.IsNullOrWhiteSpace(Field(batchFields, "Box")))
            {
                sheet.Append("   Which box did these pages come from?\n");
                asked = true;
            }

            var problems = ImportRefusals.Annotations(rows);
            foreach (var problem in problems.Take(MaxProblemsPerGroup))
            {
                sheet.Append(CultureInfo.InvariantCulture, $"   {problem}\n     → {LookFor(problem)}\n");
                asked = true;
            }

            if (problems.Count > MaxProblemsPerGroup)
            {
                sheet.Append(CultureInfo.InvariantCulture, $"   … and {problems.Count - MaxProblemsPerGroup} more like these.\n");
            }

            if (!asked)
            {
                sheet.Append("   Franz left this group open; he will tell you why on Teams.\n");
            }
        }

        return sheet.ToString();
    }

    private static string LookFor(string problem) =>
        problem.Contains("no clean sheet after it", StringComparison.Ordinal)
            ? "Look for a clean copy of this sheet with the sticky note lifted. If the box has none, tell Franz."
            : problem.Contains("names no NoteAuthor", StringComparison.Ordinal)
                ? "Who wrote the sticky note on this sheet? \"I don't know\" is a fine answer."
                : "Tell Franz what this capture is: the sheet as found, the note's own face, or the clean sheet.";

    private static List<AnnotationRow> AnnotationRows(SqliteConnection connection, string groupId)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT p.FileName, d.CustomFieldsJson FROM Pages p JOIN Documents d ON p.DocumentId = d.Id
            WHERE d.GroupId = @group ORDER BY d.Sequence, p.Sequence
            """;
        command.Parameters.AddWithValue("@group", groupId);
        using var reader = command.ExecuteReader();
        var rows = new List<AnnotationRow>();
        while (reader.Read())
        {
            var fields = reader.GetString(1);
            rows.Add(new AnnotationRow(
                reader.GetString(0), Field(fields, "DocNo"), Field(fields, "NoteState"), Field(fields, "NoteAuthor")));
        }

        return rows;
    }

    private static string? Field(string json, string name)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
