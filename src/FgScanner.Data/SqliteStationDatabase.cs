using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using FgScanner.Core.Station;
using Microsoft.Data.Sqlite;

namespace FgScanner.Data;

/// <summary>
/// Station pack/apply's view of an FG Scanner database (SPEC-SCANNER-2026-010 §07, amendment
/// 2026-10-16), in raw SQL rather than EF: it must read databases of older schemas without
/// migrating them, and it must never change the station it is fingerprinting.
/// </summary>
public sealed class SqliteStationDatabase : IStationDatabase
{
    /// <summary>The evidence tables — everything a scan, an edit, a move or a trash changes.</summary>
    internal static readonly string[] ContentTables =
        ["Groups", "Documents", "Pages", "TrashItems", "Profiles", "IndexSchemas", "FieldDefinitions"];

    /// <summary>Values each station keeps for itself; apply overwrites or rebinds them, so they
    /// would make every return look like a freeze violation.</summary>
    internal static readonly HashSet<(string Table, string Column)> StationOwned =
        [("Profiles", "ScanSettingsJson"), ("TrashItems", "TrashFolderPath")];

    public StationContent Content(string database)
    {
        using var connection = Open(database, SqliteOpenMode.ReadOnly);
        var tables = ContentTables
            .Select(t => new StationContentTable(t, [.. Columns(connection, t)
                .Where(c => !StationOwned.Contains((t, c)))
                .Order(StringComparer.Ordinal)]))
            .Where(t => t.Columns.Count > 0)
            .ToList();
        return new StationContent(tables, Fingerprint(connection, tables));
    }

    public string ContentSha256(string database, IReadOnlyList<StationContentTable> tables)
    {
        using var connection = Open(database, SqliteOpenMode.ReadOnly);
        foreach (var table in tables)
        {
            var present = Columns(connection, table.Name).ToHashSet(StringComparer.Ordinal);
            if (table.Columns.FirstOrDefault(c => !present.Contains(c)) is { } missing)
            {
                throw new InvalidDataException($"{table.Name}.{missing} is not in this database");
            }
        }

        return Fingerprint(connection, tables);
    }

    public void BindToStation(string incoming, string? receiver, string receiverTrash)
    {
        using var connection = Open(incoming, SqliteOpenMode.ReadWrite);
        using var transaction = connection.BeginTransaction();
        if (receiver is not null)
        {
            using var attach = connection.CreateCommand();
            attach.CommandText = "ATTACH DATABASE @path AS receiver";
            attach.Parameters.AddWithValue("@path", receiver);
            attach.ExecuteNonQuery();
            Execute(connection, transaction, """
                DELETE FROM main.Settings;
                INSERT INTO main.Settings SELECT * FROM receiver.Settings;
                UPDATE main.Profiles SET ScanSettingsJson =
                    (SELECT r.ScanSettingsJson FROM receiver.Profiles r WHERE r.Id = main.Profiles.Id)
                WHERE Id IN (SELECT Id FROM receiver.Profiles);
                """);
        }

        // A trash folder path names a user profile; keep its last segment (the item's folder) and
        // root it in this station's trash, where apply puts the files.
        var rebound = new List<(string Id, string Path)>();
        using (var read = connection.CreateCommand())
        {
            read.Transaction = transaction;
            read.CommandText = "SELECT Id, TrashFolderPath FROM main.TrashItems";
            using var reader = read.ExecuteReader();
            while (reader.Read())
            {
                var folder = reader.GetString(1).TrimEnd('\\', '/');
                var name = folder[(folder.LastIndexOfAny(['\\', '/']) + 1)..];
                var path = Path.Combine(receiverTrash, name);

                // The arriving database is untrusted; a segment like ".." would root the item
                // outside this station's trash, where restore and purge act on what is there.
                if (name.Length == 0 || name is "." or ".." || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
                    || !Path.GetFullPath(path).StartsWith(
                        Path.TrimEndingDirectorySeparator(Path.GetFullPath(receiverTrash)) + Path.DirectorySeparatorChar,
                        StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException($"a trash item names a folder outside the trash: {reader.GetString(1)}");
                }

                rebound.Add((reader.GetString(0), path));
            }
        }

        foreach (var (id, path) in rebound)
        {
            using var update = connection.CreateCommand();
            update.Transaction = transaction;
            update.CommandText = "UPDATE main.TrashItems SET TrashFolderPath = @path WHERE Id = @id";
            update.Parameters.AddWithValue("@path", path);
            update.Parameters.AddWithValue("@id", id);
            update.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    public bool IntegrityOk(string database)
    {
        using var connection = Open(database, SqliteOpenMode.ReadOnly);
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA integrity_check";
        return command.ExecuteScalar() as string == "ok";
    }

    internal static SqliteConnection Open(string database, SqliteOpenMode mode)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = database,
            Mode = mode,
            Pooling = false,
        }.ToString());
        connection.Open();
        return connection;
    }

    private static void Execute(SqliteConnection connection, SqliteTransaction transaction, string sql)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    internal static List<string> Columns(SqliteConnection connection, string table)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT name FROM pragma_table_info(@table)";
        command.Parameters.AddWithValue("@table", table);
        using var reader = command.ExecuteReader();
        var columns = new List<string>();
        while (reader.Read())
        {
            columns.Add(reader.GetString(0));
        }

        return columns;
    }

    /// <summary>
    /// SHA-256 over every row in id order, each value tagged with its storage type so that the
    /// text "1" and the integer 1 never hash alike.
    /// </summary>
    private static string Fingerprint(SqliteConnection connection, IReadOnlyList<StationContentTable> tables)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var table in tables)
        {
            Append(hash, $"\u001dT{table.Name}\u001e{string.Join("\u001f", table.Columns)}");
            using var command = connection.CreateCommand();
            command.CommandText =
                $"SELECT {string.Join(", ", table.Columns.Select(c => $"\"{c}\""))} FROM \"{table.Name}\" ORDER BY \"Id\"";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var row = new StringBuilder("\u001e");
                for (var i = 0; i < reader.FieldCount; i++)
                {
                    row.Append(reader.GetValue(i) switch
                    {
                        DBNull => "N",
                        long l => "I" + l.ToString(CultureInfo.InvariantCulture),
                        double d => "R" + d.ToString("R", CultureInfo.InvariantCulture),
                        byte[] b => "B" + Convert.ToHexString(b),
                        var other => "T" + Convert.ToString(other, CultureInfo.InvariantCulture),
                    }).Append('\u001f');
                }

                Append(hash, row.ToString());
            }
        }

        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    private static void Append(IncrementalHash hash, string text) => hash.AppendData(Encoding.UTF8.GetBytes(text));
}
