using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace FgScanner.Data;

/// <summary>
/// Renders docs/db-schema.md from the live EF model plus the RawSchemaSql constants.
/// A test compares its output with the committed file, so schema and doc cannot drift (PLAN §5.1).
/// </summary>
public static class SchemaDocGenerator
{
    public static string Generate()
    {
        using var db = new FgScannerDbContext(DbBootstrapper.BuildOptions(":memory:"));
        var sb = new StringBuilder();
        sb.AppendLine("# FG Scanner — Database Schema");
        sb.AppendLine();
        sb.AppendLine("<!-- GENERATED FILE — do not edit by hand. -->");
        sb.AppendLine("<!-- Regenerate: set FGSCANNER_UPDATE_SCHEMA_DOC=1 and run `dotnet test --project tests/FgScanner.Data.Tests`. -->");
        sb.AppendLine();
        sb.AppendLine("The database (`%APPDATA%\\FGScanner\\fgscanner.db`) is a first-class deliverable: every value the app");
        sb.AppendLine("produces lives here and can be queried with any SQLite tool. **External tools should query the `v_*`");
        sb.AppendLine("views**, which are kept stable across internal refactors; tables may change between versions (a backup");
        sb.AppendLine("is taken automatically before every schema migration).");
        sb.AppendLine();
        sb.AppendLine("## Tables");

        foreach (var entity in db.Model.GetEntityTypes().OrderBy(e => e.GetTableName(), StringComparer.Ordinal))
        {
            var table = entity.GetTableName();
            if (table is null)
            {
                continue;
            }

            sb.AppendLine();
            sb.AppendLine(CultureInfo.InvariantCulture, $"### {table}");
            sb.AppendLine();
            sb.AppendLine("| Column | Type | Nullable | Notes |");
            sb.AppendLine("|---|---|---|---|");
            var pkNames = entity.FindPrimaryKey()?.Properties.Select(p => p.Name).ToHashSet() ?? [];
            foreach (var property in entity.GetProperties())
            {
                var notes = new List<string>();
                if (pkNames.Contains(property.Name))
                {
                    notes.Add("PK");
                }

                if (property.IsForeignKey())
                {
                    var fk = property.GetContainingForeignKeys().First();
                    notes.Add($"FK → {fk.PrincipalEntityType.GetTableName()}");
                }

                if (property.ClrType.IsEnum || (Nullable.GetUnderlyingType(property.ClrType)?.IsEnum ?? false))
                {
                    var enumType = Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType;
                    notes.Add($"enum: {string.Join(", ", Enum.GetNames(enumType).Select((n, i) => $"{i}={n}"))}");
                }

                sb.AppendLine(
                    CultureInfo.InvariantCulture,
                    $"| {property.Name} | {property.GetColumnType()} | {(property.IsNullable ? "yes" : "no")} | {string.Join("; ", notes)} |");
            }

            var indexes = entity.GetIndexes().ToList();
            if (indexes.Count > 0)
            {
                sb.AppendLine();
                foreach (var index in indexes)
                {
                    sb.AppendLine(
                        CultureInfo.InvariantCulture,
                        $"- Index{(index.IsUnique ? " (unique)" : "")}: {string.Join(", ", index.Properties.Select(p => p.Name))}");
                }
            }
        }

        sb.AppendLine();
        sb.AppendLine("## Raw-SQL objects (FTS + stable views)");
        foreach (var (name, sql) in RawSchemaSql.AllObjects)
        {
            sb.AppendLine();
            sb.AppendLine(CultureInfo.InvariantCulture, $"### {name}");
            sb.AppendLine();
            sb.AppendLine("```sql");
            sb.AppendLine(sql.Trim());
            sb.AppendLine("```");
        }

        return sb.ToString();
    }
}
