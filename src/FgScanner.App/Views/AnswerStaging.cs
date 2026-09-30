using System.Globalization;
using FgScanner.Core.IndexPackages;

namespace FgScanner.App.Views;

/// <summary>One staged answer; the slot identity is (Field) for
/// single-value fields and (Field, Qualifier, Value) for person /
/// (Field, Value) for subject — SPEC-2026-003 §07 as amended 2026-09-30.</summary>
public sealed record StagedAnswer(string Field, string? Qualifier, string Value);

/// <summary>
/// The answers Jim has staged but not yet exported, per document
/// (SPEC-2026-008 AC-3). Free of WPF so every rule is testable without a
/// window. Latest edit wins WITHIN a slot; multi-value fields hold one
/// entry per value. A multi-value withdrawal (empty person/subject) is
/// refused here — the contract's empty value cannot name its target, so
/// offering the gesture would stage a lie (§03 non-goal).
/// </summary>
public sealed class AnswerStaging
{
    /// <summary>Single-value fields: the slot is the field alone, so a
    /// later edit replaces — including a date whose qualifier moved.</summary>
    private static readonly string[] SingleValueFields =
        [IndexAnswerVocabulary.DocType, IndexAnswerVocabulary.Date, IndexAnswerVocabulary.KeyFlag];

    private readonly Dictionary<string, List<StagedAnswer>> _byAnchor = new(StringComparer.Ordinal);

    /// <summary>Raised on every change — the draft store's autosave hook.</summary>
    public event Action? Changed;

    public int AnsweredDocumentCount => _byAnchor.Count(kv => kv.Value.Count > 0);

    public IReadOnlyList<StagedAnswer> ForDocument(string anchorPageId) =>
        _byAnchor.TryGetValue(anchorPageId, out var list) ? list : [];

    public void Stage(string anchorPageId, string field, string? qualifier, string value)
    {
        if (!IndexAnswerVocabulary.Fields.Contains(field))
        {
            throw new ArgumentException(
                $"\"{field}\" is not an index answer field this contract carries");
        }

        var multiValue = !SingleValueFields.Contains(field, StringComparer.Ordinal);
        if (multiValue && value.Length == 0)
        {
            throw new ArgumentException(
                $"an empty {field} cannot say WHICH one it withdraws — removal of a " +
                "decided person or subject is the portal's job (phase 5)");
        }

        if (field == IndexAnswerVocabulary.Date && value.Length != 0
            && !DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out _))
        {
            // Refused, never reformatted: silently turning 07/18/2021 into
            // something else is how a wrong date enters a legal index.
            throw new ArgumentException(
                $"a date is yyyy-MM-dd and a real calendar date, got \"{value}\"");
        }

        var list = _byAnchor.TryGetValue(anchorPageId, out var existing)
            ? existing
            : _byAnchor[anchorPageId] = [];
        var replaced = multiValue
            ? list.FindIndex(a => a.Field == field && a.Qualifier == qualifier && a.Value == value)
            : list.FindIndex(a => a.Field == field);
        var entry = new StagedAnswer(field, qualifier, value);
        if (replaced >= 0)
        {
            list[replaced] = entry;
        }
        else
        {
            list.Add(entry);
        }

        Changed?.Invoke();
    }

    /// <summary>Everything staged, for the draft store to persist.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<StagedAnswer>> Snapshot() =>
        _byAnchor.Where(kv => kv.Value.Count > 0)
            .ToDictionary(kv => kv.Key, kv => (IReadOnlyList<StagedAnswer>)kv.Value.ToArray(),
                StringComparer.Ordinal);

    /// <summary>Replaces the staged state from a draft. No Changed event:
    /// restoring what was already saved must not immediately re-save it.</summary>
    public void Restore(IReadOnlyDictionary<string, IReadOnlyList<StagedAnswer>> snapshot)
    {
        _byAnchor.Clear();
        foreach (var (anchor, answers) in snapshot)
        {
            _byAnchor[anchor] = [.. answers];
        }
    }

    public void Unstage(string anchorPageId, string field, string? qualifier, string value)
    {
        if (_byAnchor.TryGetValue(anchorPageId, out var list)
            && list.RemoveAll(a =>
                a.Field == field && a.Qualifier == qualifier && a.Value == value) > 0)
        {
            Changed?.Invoke();
        }
    }
}
