using System.Globalization;
using FgScanner.Core.IndexPackages;

namespace FgScanner.App.Views;

/// <summary>
/// One staged answer. The slot identity is (Field) for single-value fields
/// and (Field, Qualifier, Value) for person / (Field, Value) for subject —
/// SPEC-2026-003 §07 as amended 2026-09-30. DecidedAt is stamped when the
/// answer is STAGED — the moment Jim decided — never at export: the
/// portal's dedupe key includes it, and export-time stamps would make a
/// partial-then-full send-back duplicate every earlier row. Contract
/// format, UTC seconds.
/// </summary>
public sealed record StagedAnswer(
    string Field, string? Qualifier, string Value, string DecidedAt = "");

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

    /// <summary>Overridable for deterministic tests and byte-stable exports.</summary>
    public Func<DateTimeOffset> Clock { get; set; } = () => DateTimeOffset.UtcNow;

    /// <summary>The open package's own rules (PackageWriter.AnswerValidator),
    /// run at entry so an answer the writer would refuse is refused NOW,
    /// not days later at export (SPEC-2026-008 AC-5).</summary>
    public PackageWriter.AnswerValidator? Validator { get; set; }

    /// <summary>Set when staging must not happen at all (the saved draft
    /// could not be read, and a save now would overwrite it); the text is
    /// the refusal shown to the operator.</summary>
    public string? BlockedReason { get; set; }

    /// <summary>Bumped on every change, so "is the last export still
    /// current?" is an integer compare, not a serialization.</summary>
    public int Version { get; private set; }

    private string Now() => Clock().UtcDateTime.ToString(
        "yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    public int AnsweredDocumentCount => _byAnchor.Count(kv => kv.Value.Count > 0);

    /// <summary>A copy: callers bind it, and ItemsControl ignores a change
    /// notification that hands back the same list reference.</summary>
    public IReadOnlyList<StagedAnswer> ForDocument(string anchorPageId) =>
        _byAnchor.TryGetValue(anchorPageId, out var list) ? list.ToArray() : [];

    public void Stage(string anchorPageId, string field, string? qualifier, string value)
    {
        if (BlockedReason is { } blocked)
        {
            throw new ArgumentException(blocked);
        }

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

        // The decider is checked at export, where the real name is known.
        Validator?.Validate(new IndexAnswer(
            anchorPageId, field, qualifier, value, "staging", Clock()));

        var list = _byAnchor.TryGetValue(anchorPageId, out var existing)
            ? existing
            : _byAnchor[anchorPageId] = [];
        var replaced = multiValue
            ? list.FindIndex(a => a.Field == field && a.Qualifier == qualifier && a.Value == value)
            : list.FindIndex(a => a.Field == field);
        var entry = new StagedAnswer(field, qualifier, value, Now());
        if (replaced >= 0)
        {
            list[replaced] = entry;
        }
        else
        {
            list.Add(entry);
        }

        Version++;
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

        Version++;
    }

    public void Unstage(string anchorPageId, string field, string? qualifier, string value)
    {
        if (BlockedReason is not null)
        {
            return;
        }

        if (_byAnchor.TryGetValue(anchorPageId, out var list)
            && list.RemoveAll(a =>
                a.Field == field && a.Qualifier == qualifier && a.Value == value) > 0)
        {
            Version++;
            Changed?.Invoke();
        }
    }
}
