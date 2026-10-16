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

    /// <summary>The fingerprint over exactly <paramref name="tables"/>' columns.</summary>
    /// <exception cref="InvalidDataException">A named table or column is not in the database.</exception>
    string ContentSha256(string database, IReadOnlyList<StationContentTable> tables);

    /// <summary>
    /// Makes an arriving database this station's own: the receiver's Settings rows and profile
    /// scan settings replace the sender's, and trash paths are re-rooted to
    /// <paramref name="receiverTrash"/>. <paramref name="receiver"/> is null for a station that
    /// has no database yet.
    /// </summary>
    void BindToStation(string incoming, string? receiver, string receiverTrash);

    bool IntegrityOk(string database);
}
