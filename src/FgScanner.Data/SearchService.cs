using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace FgScanner.Data;

public sealed record SearchHit(
    Guid PageId,
    Guid DocumentId,
    Guid GroupId,
    string GroupName,
    string FileName,
    int DocumentSequence,
    string Snippet,
    string Source);

/// <summary>
/// Full-text search (PLAN prompt 10, research-5 item 22): FTS5 with snippet() over OCR text,
/// plus substring matches over index field values and AI descriptions. Matched spans are wrapped
/// in <see cref="HighlightStart"/>/<see cref="HighlightEnd"/> for the UI to render.
/// </summary>
public sealed class SearchService(IDbContextFactory<FgScannerDbContext> dbFactory)
{
    // Mathematical angle brackets: visually obvious and absent from real scan text.
    public const char HighlightStart = '⟪';
    public const char HighlightEnd = '⟫';

    private const int SnippetContext = 60;

    public async Task<IReadOnlyList<SearchHit>> SearchAsync(
        string query, int limit = 50, CancellationToken cancellationToken = default)
    {
        query = query.Trim();
        if (query.Length == 0)
        {
            return [];
        }

        var hits = new List<SearchHit>();
        var seenPages = new HashSet<Guid>();

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await FtsSearchAsync(db, query, limit, hits, seenPages, cancellationToken).ConfigureAwait(false);
        if (hits.Count < limit)
        {
            await FieldAndAiSearchAsync(db, query, limit, hits, seenPages, cancellationToken).ConfigureAwait(false);
        }

        return hits;
    }

    private static async Task FtsSearchAsync(
        FgScannerDbContext db, string query, int limit,
        List<SearchHit> hits, HashSet<Guid> seenPages, CancellationToken cancellationToken)
    {
        var connection = db.Database.GetDbConnection();
        await db.Database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var command = connection.CreateCommand();
        await using (command.ConfigureAwait(false))
        {
            command.CommandText = """
                SELECT p.Id, p.DocumentId, d.GroupId, g.Name, p.FileName, d.Sequence,
                       snippet(PagesFts, 0, $hs, $he, ' … ', 12)
                FROM PagesFts
                JOIN Pages p ON p.rowid = PagesFts.rowid
                JOIN Documents d ON d.Id = p.DocumentId
                JOIN Groups g ON g.Id = d.GroupId
                WHERE PagesFts MATCH $query
                ORDER BY rank
                LIMIT $limit
                """;
            AddParameter(command, "$query", ToFtsQuery(query));
            AddParameter(command, "$hs", HighlightStart.ToString());
            AddParameter(command, "$he", HighlightEnd.ToString());
            AddParameter(command, "$limit", limit);

            var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            await using (reader.ConfigureAwait(false))
            {
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    var pageId = reader.GetGuid(0);
                    if (seenPages.Add(pageId))
                    {
                        hits.Add(new SearchHit(
                            pageId, reader.GetGuid(1), reader.GetGuid(2), reader.GetString(3),
                            reader.GetString(4), reader.GetInt32(5), reader.GetString(6), "OCR"));
                    }
                }
            }
        }
    }

    private static async Task FieldAndAiSearchAsync(
        FgScannerDbContext db, string query, int limit,
        List<SearchHit> hits, HashSet<Guid> seenPages, CancellationToken cancellationToken)
    {
        var pattern = "%" + EscapeLike(query) + "%";
        var candidates = await db.Pages
            .Where(p => EF.Functions.Like(p.Document!.CustomFieldsJson, pattern, "\\")
                || (p.AiDescription != null && EF.Functions.Like(p.AiDescription, pattern, "\\")))
            .Select(p => new
            {
                p.Id,
                p.DocumentId,
                p.Document!.GroupId,
                GroupName = p.Document!.Group!.Name,
                p.FileName,
                p.Document!.Sequence,
                p.Document!.CustomFieldsJson,
                p.AiDescription,
            })
            .Take(limit)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        foreach (var page in candidates)
        {
            if (hits.Count >= limit || !seenPages.Add(page.Id))
            {
                continue;
            }

            string? snippet = null;
            var source = "Fields";
            if (FieldSnippet(page.CustomFieldsJson, query) is { } fieldHit)
            {
                (snippet, source) = fieldHit;
            }
            else if (page.AiDescription is { } ai && ai.Contains(query, StringComparison.OrdinalIgnoreCase))
            {
                snippet = MakeSnippet(ai, query);
                source = "AI";
            }

            if (snippet is not null)
            {
                hits.Add(new SearchHit(
                    page.Id, page.DocumentId, page.GroupId, page.GroupName,
                    page.FileName, page.Sequence, snippet, source));
            }
        }
    }

    /// <summary>Each token quoted (implicit AND): user text can never break FTS5 query syntax.</summary>
    private static string ToFtsQuery(string query) => string.Join(
        " ",
        query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(t => "\"" + t.Replace("\"", "\"\"") + "\""));

    private static string EscapeLike(string query) => query
        .Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");

    private static (string Snippet, string Source)? FieldSnippet(string customFieldsJson, string query)
    {
        Dictionary<string, string?>? values;
        try
        {
            values = JsonSerializer.Deserialize<Dictionary<string, string?>>(customFieldsJson);
        }
        catch (JsonException)
        {
            return null;
        }

        if (values is not null)
        {
            foreach (var kv in values)
            {
                if (kv.Value?.Contains(query, StringComparison.OrdinalIgnoreCase) == true)
                {
                    return ($"{kv.Key}: {MakeSnippet(kv.Value, query)}", "Fields");
                }
            }
        }

        return null;
    }

    private static string MakeSnippet(string text, string query)
    {
        var index = text.IndexOf(query, StringComparison.OrdinalIgnoreCase);
        if (index < 0)
        {
            return text.Length <= 2 * SnippetContext ? text : text[..(2 * SnippetContext)] + " …";
        }

        var start = Math.Max(0, index - SnippetContext);
        var end = Math.Min(text.Length, index + query.Length + SnippetContext);
        var builder = new StringBuilder();
        if (start > 0)
        {
            builder.Append("… ");
        }

        builder.Append(text[start..index])
            .Append(HighlightStart).Append(text[index..(index + query.Length)]).Append(HighlightEnd)
            .Append(text[(index + query.Length)..end]);
        if (end < text.Length)
        {
            builder.Append(" …");
        }

        return builder.ToString();
    }

    private static void AddParameter(System.Data.Common.DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }
}
