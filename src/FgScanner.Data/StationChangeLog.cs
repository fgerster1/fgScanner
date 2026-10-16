using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using FgScanner.Core.Station;
using Microsoft.Data.Sqlite;

namespace FgScanner.Data;

/// <summary>
/// <c>changes.jsonl</c> (SPEC-SCANNER-2026-010 §07, AC-9): every evidence cell that differs
/// between the database Franz received and the one he returns, one JSON object per line, with
/// his name and the time. It is the record of work done away from the station that captured the
/// pages; <c>capturedBy</c> itself is never rewritten (ADR-0005).
/// </summary>
public static class StationChangeLog
{
    public const string FileName = "changes.jsonl";

    private static readonly JsonWriterOptions Json = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>
    /// Compares the evidence tables over the columns both databases have: a column a migration
    /// added was never Franz's edit, and the station-owned values (settings, scan settings, trash
    /// paths) do not travel, so neither is logged.
    /// </summary>
    public static IReadOnlyList<string> Diff(string received, string processed, string by, DateTimeOffset at)
    {
        var when = StationManifest.FormatSentAt(at);
        using var before = SqliteStationDatabase.Open(received, SqliteOpenMode.ReadOnly);
        using var after = SqliteStationDatabase.Open(processed, SqliteOpenMode.ReadOnly);
        var lines = new List<string>();
        foreach (var table in SqliteStationDatabase.ContentTables)
        {
            var afterColumns = SqliteStationDatabase.Columns(after, table).ToHashSet(StringComparer.Ordinal);
            var columns = SqliteStationDatabase.Columns(before, table)
                .Where(c => afterColumns.Contains(c) && !SqliteStationDatabase.StationOwned.Contains((table, c)))
                .Order(StringComparer.Ordinal)
                .ToList();
            if (!columns.Contains("Id"))
            {
                continue;
            }

            var old = Rows(before, table, columns);
            var current = Rows(after, table, columns);
            foreach (var key in old.Keys.Union(current.Keys).Order(StringComparer.Ordinal))
            {
                var had = old.TryGetValue(key, out var was);
                var has = current.TryGetValue(key, out var now);
                if (had && has)
                {
                    for (var i = 0; i < columns.Count; i++)
                    {
                        if (!Equals(was![i], now![i]))
                        {
                            lines.Add(Line(w =>
                            {
                                Head(w, table, key, "changed");
                                w.WriteString("column", columns[i]);
                                Value(w, "old", was[i]);
                                Value(w, "new", now[i]);
                            }, by, when));
                        }
                    }
                }
                else
                {
                    var row = had ? was! : now!;
                    lines.Add(Line(w =>
                    {
                        Head(w, table, key, had ? "removed" : "added");
                        w.WriteStartObject(had ? "old" : "new");
                        for (var i = 0; i < columns.Count; i++)
                        {
                            Value(w, columns[i], row[i]);
                        }

                        w.WriteEndObject();
                    }, by, when));
                }
            }
        }

        return lines;
    }

    /// <summary>
    /// The change log of a return: the received database is migrated (as a copy, in the temp
    /// folder) to this build's schema first, so every column is compared — including one a
    /// migration added, where Franz may have set values — and what the migration itself filled in
    /// is not counted as his work (Revision D, P9-9).
    /// </summary>
    public static IReadOnlyList<string> DiffSinceReceived(string received, string processed, string by, DateTimeOffset at)
    {
        var copy = Path.Combine(Path.GetTempPath(), "fgscanner-received-" + Guid.NewGuid().ToString("N") + ".db");
        File.Copy(received, copy);
        try
        {
            var backup = DbBootstrapper.MigrateWithBackup(copy, "changelog");
            SqliteConnection.ClearAllPools();
            if (backup is not null)
            {
                File.Delete(backup);
            }

            return Diff(copy, processed, by, at);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            foreach (var file in new[] { copy, copy + "-wal", copy + "-shm" })
            {
                if (File.Exists(file))
                {
                    File.Delete(file);
                }
            }
        }
    }

    public static byte[] ToBytes(IReadOnlyList<string> lines) =>
        Encoding.UTF8.GetBytes(string.Concat(lines.Select(l => l + "\n")));

    private static Dictionary<string, object?[]> Rows(SqliteConnection connection, string table, List<string> columns)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {string.Join(", ", columns.Select(c => $"\"{SqliteStationDatabase.Identifier(c)}\""))} FROM \"{table}\"";
        using var reader = command.ExecuteReader();
        var key = columns.IndexOf("Id");
        var rows = new Dictionary<string, object?[]>(StringComparer.Ordinal);
        while (reader.Read())
        {
            var values = new object?[columns.Count];
            for (var i = 0; i < columns.Count; i++)
            {
                values[i] = reader.GetValue(i) switch
                {
                    DBNull => null,
                    byte[] b => Convert.ToHexString(b),
                    var v => v,
                };
            }

            rows[Convert.ToString(values[key], CultureInfo.InvariantCulture)!] = values;
        }

        return rows;
    }

    private static string Line(Action<Utf8JsonWriter> body, string by, string at)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, Json))
        {
            writer.WriteStartObject();
            body(writer);
            writer.WriteString("by", by);
            writer.WriteString("at", at);
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static void Head(Utf8JsonWriter writer, string table, string key, string change)
    {
        writer.WriteString("table", table);
        writer.WriteString("key", key);
        writer.WriteString("change", change);
    }

    private static void Value(Utf8JsonWriter writer, string name, object? value)
    {
        switch (value)
        {
            case null:
                writer.WriteNull(name);
                break;
            case long l:
                writer.WriteNumber(name, l);
                break;
            case double d:
                writer.WriteNumber(name, d);
                break;
            default:
                writer.WriteString(name, Convert.ToString(value, CultureInfo.InvariantCulture));
                break;
        }
    }
}
