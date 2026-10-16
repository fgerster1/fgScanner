using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using FgScanner.Core.Station;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

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
            var present = Columns(connection, Identifier(table.Name)).ToHashSet(StringComparer.Ordinal);
            if (table.Columns.FirstOrDefault(c => !present.Contains(c)) is { } missing)
            {
                throw new InvalidDataException($"{table.Name}.{missing} is not in this database");
            }
        }

        return Fingerprint(connection, tables);
    }

    public void BindToStation(string incoming, string? receiver, string receiverTrash)
    {
        // The receiver is read on a read-only connection of its own: binding must never be able
        // to change the station it is binding to.
        var settings = new List<(string Key, string Value)>();
        var scanSettings = new List<(string Id, string Json)>();
        if (receiver is not null)
        {
            using var own = Open(receiver, SqliteOpenMode.ReadOnly);
            using (var read = own.CreateCommand())
            {
                read.CommandText = "SELECT Key, Value FROM Settings";
                using var reader = read.ExecuteReader();
                while (reader.Read())
                {
                    settings.Add((reader.GetString(0), reader.GetString(1)));
                }
            }

            using (var read = own.CreateCommand())
            {
                read.CommandText = "SELECT Id, ScanSettingsJson FROM Profiles";
                using var reader = read.ExecuteReader();
                while (reader.Read())
                {
                    scanSettings.Add((reader.GetString(0), reader.GetString(1)));
                }
            }
        }

        using var connection = Open(incoming, SqliteOpenMode.ReadWrite);
        using var transaction = connection.BeginTransaction();
        RebuildTriggersAndViews(connection, transaction);

        // Nothing in a pack configures the machine that opens it (Revision D, D1): the sender's
        // Settings include the command line a commit runs. A receiver keeps its own; an empty
        // station starts on defaults.
        Execute(connection, transaction, "DELETE FROM main.Settings;");
        foreach (var (key, value) in settings)
        {
            using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = "INSERT INTO main.Settings (Key, Value) VALUES (@key, @value)";
            insert.Parameters.AddWithValue("@key", key);
            insert.Parameters.AddWithValue("@value", value);
            insert.ExecuteNonQuery();
        }

        foreach (var (id, json) in scanSettings)
        {
            using var update = connection.CreateCommand();
            update.Transaction = transaction;
            update.CommandText = "UPDATE main.Profiles SET ScanSettingsJson = @json WHERE Id = @id";
            update.Parameters.AddWithValue("@json", json);
            update.Parameters.AddWithValue("@id", id);
            update.ExecuteNonQuery();
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
                var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(receiverTrash)) + Path.DirectorySeparatorChar;
                var path = Path.GetFullPath(Path.Combine(receiverTrash, name));

                // The arriving database is untrusted; a segment like ".." — or "...", which
                // Windows trims to nothing — would root the item outside this station's trash or
                // at the trash itself, where restore and purge act on everything there.
                if (name.Length == 0 || name != name.TrimEnd('.', ' ') || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
                    || !path.StartsWith(root, StringComparison.OrdinalIgnoreCase) || path.Length <= root.Length)
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

    public string? SchemaProblem(string incoming, string? receiver, string? lastMigration)
    {
        string reference;
        string? scratch = null;
        if (receiver is not null)
        {
            reference = receiver;
        }
        else
        {
            scratch = Path.Combine(Path.GetTempPath(), "fgscanner-schema-" + Guid.NewGuid().ToString("N") + ".db");
            try
            {
                using var context = new FgScannerDbContext(DbBootstrapper.BuildOptions(scratch));
                if (lastMigration is null || !context.Database.GetMigrations().Contains(lastMigration, StringComparer.Ordinal))
                {
                    return $"the update's database is at {lastMigration ?? "no version"}, which is not a version this FG Scanner knows";
                }

                context.GetService<IMigrator>().Migrate(lastMigration);
            }
            finally
            {
                SqliteConnection.ClearAllPools();
            }

            reference = scratch;
        }

        try
        {
            var expected = TablesAndIndexes(reference);
            var actual = TablesAndIndexes(incoming);
            foreach (var (key, sql) in actual.OrderBy(p => p.Key.Name, StringComparer.Ordinal))
            {
                if (!expected.TryGetValue(key, out var wanted))
                {
                    return $"the update's database has a {key.Type} this version does not: {key.Name}";
                }

                if (wanted != sql)
                {
                    return $"the update's database has a different {key.Type}: {key.Name}";
                }
            }

            return expected.Keys.FirstOrDefault(k => !actual.ContainsKey(k)) is { Name: not null } lacking
                ? $"the update's database lacks the {lacking.Type} {lacking.Name}"
                : null;
        }
        finally
        {
            if (scratch is not null && File.Exists(scratch))
            {
                File.Delete(scratch);
            }
        }
    }

    public string? PathOutside(string database, string evidenceRoot)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(evidenceRoot));
        using var connection = Open(database, SqliteOpenMode.ReadOnly);
        foreach (var (table, column, rootItself) in new[]
                 {
                     ("Profiles", "BaseDirectory", true), ("Groups", "DirectoryPath", false),
                     ("TrashItems", "GroupDirectoryPath", false),
                 })
        {
            if (!Columns(connection, table).Contains(column, StringComparer.Ordinal))
            {
                continue;
            }

            using var command = connection.CreateCommand();
            command.CommandText = $"SELECT \"{column}\" FROM \"{table}\"";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                if (reader.IsDBNull(0) || reader.GetString(0).Length == 0)
                {
                    continue;
                }

                var value = reader.GetString(0);
                if (!Inside(value, root, rootItself))
                {
                    return $"{table}.{column} = {value}";
                }
            }
        }

        return null;
    }

    private static bool Inside(string path, string root, bool rootItself)
    {
        string full;
        try
        {
            full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }

        return !full.StartsWith(@"\\", StringComparison.Ordinal)
            && ((rootItself && full.Equals(root, StringComparison.OrdinalIgnoreCase))
                || full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Tables and indexes by kind and name, with their SQL in one-space form. Triggers and
    /// views are left out: binding replaces them with the build's own (Revision D, D2).</summary>
    private static Dictionary<(string Type, string Name), string> TablesAndIndexes(string database)
    {
        using var connection = Open(database, SqliteOpenMode.ReadOnly);
        using var command = connection.CreateCommand();
        // The literal prefix, not LIKE: there '_' is a wildcard, so "sqliteX…" would be skipped.
        // SQLite reserves "sqlite_" in any case for its own objects, so nothing else can carry it.
        command.CommandText = "SELECT type, name, sql FROM sqlite_master WHERE type IN ('table', 'index') AND lower(substr(name, 1, 7)) <> 'sqlite_'";
        using var reader = command.ExecuteReader();
        var objects = new Dictionary<(string, string), string>();
        while (reader.Read())
        {
            objects[(reader.GetString(0), reader.GetString(1))] = reader.IsDBNull(2)
                ? ""
                : string.Join(" ", reader.GetString(2).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        }

        return objects;
    }

    /// <summary>
    /// Triggers run on every future capture and views feed external tools, so none is taken from
    /// a pack: whatever arrived is dropped and the build's own are created — the same definitions
    /// a fresh database gets (Revision D, D2). A station's stale view is brought up to date with it.
    /// </summary>
    private static void RebuildTriggersAndViews(SqliteConnection connection, SqliteTransaction transaction)
    {
        var arrived = new List<(string Type, string Name)>();
        using (var read = connection.CreateCommand())
        {
            read.Transaction = transaction;
            read.CommandText = "SELECT type, name FROM main.sqlite_master WHERE type IN ('trigger', 'view')";
            using var reader = read.ExecuteReader();
            while (reader.Read())
            {
                arrived.Add((reader.GetString(0), reader.GetString(1)));
            }
        }

        foreach (var (type, name) in arrived)
        {
            Execute(connection, transaction, $"DROP {(type == "view" ? "VIEW" : "TRIGGER")} IF EXISTS main.\"{Identifier(name)}\";");
        }

        foreach (var sql in new[]
                 {
                     RawSchemaSql.CreateFtsTriggers, RawSchemaSql.CreateViewIndex, RawSchemaSql.CreateViewPages,
                     RawSchemaSql.CreateViewOcrText,
                 })
        {
            Execute(connection, transaction, sql);
        }
    }

    /// <summary>Table and column names come from an untrusted database or manifest and are spliced
    /// into SQL; only plain identifiers are accepted (Revision D, P10-7).</summary>
    internal static string Identifier(string name) =>
        System.Text.RegularExpressions.Regex.IsMatch(name, "^[A-Za-z_][A-Za-z0-9_]*$")
            ? name
            : throw new InvalidDataException($"\"{name}\" is not a name FG Scanner uses");

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

        // A database may arrive from a pack: never let its schema's functions run with trust.
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA trusted_schema = OFF";
        command.ExecuteNonQuery();
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
                $"SELECT {string.Join(", ", table.Columns.Select(c => $"\"{Identifier(c)}\""))} FROM \"{Identifier(table.Name)}\" ORDER BY \"Id\"";
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
