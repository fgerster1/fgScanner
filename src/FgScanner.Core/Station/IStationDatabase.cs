namespace FgScanner.Core.Station;

/// <summary>One table of a content fingerprint and the columns it was taken over.</summary>
public sealed record StationContentTable(string Name, IReadOnlyList<string> Columns);

/// <summary>
/// A station's evidence rows, fingerprinted (SPEC-SCANNER-2026-010 §07, amendment 2026-10-16). It
/// records the columns it read, so the other side reads the same ones: a migration adds columns,
/// and the rows Jim captured must still compare equal after it.
/// </summary>
public sealed record StationContent(IReadOnlyList<StationContentTable> Tables, string Sha256)
{
    public bool Equals(StationContent? other) =>
        other is not null
        && Sha256 == other.Sha256
        && Tables.Count == other.Tables.Count
        && Tables.Zip(other.Tables).All(p => p.First.Name == p.Second.Name && p.First.Columns.SequenceEqual(p.Second.Columns));

    public override int GetHashCode() => Sha256.GetHashCode(StringComparison.Ordinal);
}

/// <summary>What station pack and apply need from the database, which Core cannot open itself.</summary>
public interface IStationDatabase
{
    /// <summary>The fingerprint of every evidence row, over the columns this database has.</summary>
    StationContent Content(string database);

    /// <summary>Tables and indexes of <paramref name="incoming"/> that differ from the receiver's own, or from a
    /// fresh database at <paramref name="lastMigration"/> when the receiver has none; null when they match.</summary>
    string? SchemaProblem(string incoming, string? receiver, string? lastMigration);

    /// <summary>The first stored folder path outside <paramref name="evidenceRoot"/> or on a share, as
    /// <c>Table.Column = value</c>; null when every path stays in the station's evidence folder.</summary>
    string? PathOutside(string database, string evidenceRoot);

    /// <summary>The fingerprint over exactly <paramref name="tables"/>' columns.</summary>
    /// <exception cref="InvalidDataException">A named table or column is not in the database.</exception>
    string ContentSha256(string database, IReadOnlyList<StationContentTable> tables);

    /// <summary>
    /// Makes an arriving database this station's own: triggers and views are replaced with the
    /// build's own; the receiver's Settings rows and profile scan settings replace the sender's —
    /// or, for a station with no database yet (<paramref name="receiver"/> null), no Settings are
    /// kept at all; and trash paths are re-rooted to <paramref name="receiverTrash"/>.
    /// </summary>
    void BindToStation(string incoming, string? receiver, string receiverTrash);

    bool IntegrityOk(string database);
}
