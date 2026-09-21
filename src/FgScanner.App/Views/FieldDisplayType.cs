using FgScanner.Data;

namespace FgScanner.App.Views;

/// <summary>
/// What the Settings field editor offers in its Type list. This is a SCREEN concept, not a stored
/// one: it exists because operators look for "memo" among the types, not among the checkboxes.
///
/// Memo is still Text plus <see cref="FieldDefinition.Memo"/> underneath. <c>FieldType</c> keeps
/// its four members, because it casts positionally to <c>IndexFieldType</c> and the type's NAME is
/// written into <c>manifest.json</c>, which the JimsStuff importer parses — a fifth member would be
/// a contract change for something that only decides how a value is typed on screen (ADR-0009).
/// Nobody should later "tidy" this into <c>FieldType</c>; that is the same trap under a new name.
/// </summary>
public enum FieldDisplayType
{
    Text,
    Memo,
    Date,
    Number,
    List,
}

/// <summary>
/// The two directions of that mapping. Neither has a fall-through: a type outside the four is a
/// programming error, and absorbing it would rewrite a stored type — the name manifest.json hands
/// the importer — into whichever member the catch-all happened to name.
/// </summary>
public static class FieldDisplayTypes
{
    /// <summary>What the Type list offers, in the order it offers it.</summary>
    public static IReadOnlyList<FieldDisplayType> All { get; } =
        Array.AsReadOnly(Enum.GetValues<FieldDisplayType>());

    /// <summary>What to show for a stored definition: Text with the memo flag reads as Memo.</summary>
    public static FieldDisplayType From(FieldType type, bool memo) => type switch
    {
        FieldType.Text => memo ? FieldDisplayType.Memo : FieldDisplayType.Text,
        FieldType.Date => FieldDisplayType.Date,
        FieldType.Number => FieldDisplayType.Number,
        FieldType.List => FieldDisplayType.List,
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "No Type list entry for this stored field type."),
    };

    /// <summary>What to store: the type, and whether the memo flag goes with it.</summary>
    public static (FieldType Type, bool Memo) ToStored(FieldDisplayType shown) => shown switch
    {
        FieldDisplayType.Text => (FieldType.Text, false),
        FieldDisplayType.Memo => (FieldType.Text, true),
        FieldDisplayType.Date => (FieldType.Date, false),
        FieldDisplayType.Number => (FieldType.Number, false),
        FieldDisplayType.List => (FieldType.List, false),
        _ => throw new ArgumentOutOfRangeException(nameof(shown), shown, "No stored field type for this Type list entry."),
    };
}
