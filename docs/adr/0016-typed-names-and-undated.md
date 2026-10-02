# ADR-0016 — Jim may type a missing person; a page may be answered "undated"

Date: 2026-10-02 · Status: Accepted · Decided by: Franz · Spec: JimsStuff SPEC-2026-007 §12

## Context

Building Jim's runbook (SPEC-2026-007 Prompt 9) found two dead ends in the Index section:

- The person picker sent register ids only, and its hint said "note it for Franz below"
  with no note field below. A person missing from the list could not be recorded at all,
  although the contract and the portal already accept a typed name and hold it as a
  proposal for Franz (the portal's importer never mints a person).
- The date qualifiers were `exact` and `about`. A page with no date could not be
  answered, so it could never count as fully indexed (SPEC-2026-007 C1).

Franz chose to close both in FG Scanner (2026-10-02).

## Decisions

1. **A typed name first tries the list — exactly as the portal would.** The portal
   resolves a typed spelling only through its alias table (unique per normalised
   spelling: punctuation to spaces, spaces collapsed, lower case), never through display
   names. FG Scanner mirrors that against the aliases in the package's `people.json`, and
   stages an id only when the package reproduces the portal's answer for certain: the
   typed name and the spelling are printable ASCII (where Python's and .NET's character
   classes and lower-casing agree) and exactly one listed person holds it. Anything else —
   a display name that is no spelling, accented text, a spelling two people share in the
   package — travels as typed and the portal decides. An id the portal would resolve
   differently is never exported. *(Corrected 2026-10-02, SPEC-2026-007 Prompt 10 review:
   the first version also matched display names, first hit in id order.)*
   A pick from the dropdown is not a typed name: it stages the id picked, even where
   namesakes share a display name.
2. **A typed name must be plain, trimmed, at most 120 characters, and never shaped like
   an id.** The plain-text rule is the decider name's, now shared
   (`IndexAnswerVocabulary.IsPlainText`), because both must survive the contract's byte
   rule. `P0042` is refused because the portal reads it as an id and would refuse the
   whole file when its register has no such person.
3. **"Undated" is the qualifier `undated` with the value `undated`.** It cannot be an
   empty value, because the contract reads an empty value as a withdrawal. The writer
   refuses `undated` mixed with a real date either way. Switching between a dated and an
   undated answer uses the existing qualifier-change path: export withdraws every other
   date slot the portal may hold before the new answer — the seed's, **and any slot an
   earlier export of this draft filled**. *(Corrected 2026-10-02, SPEC-2026-007 Prompt 10
   review: as first built, only the seed's slot was withdrawn, so a qualifier changed
   after an upload left two current dates on the portal.)* The draft remembers every
   answer it has exported for this reason; by the same rule, unticking an exported key
   flag, or removing the chip of an exported doc type or date, stages a withdrawal
   instead of silently unstaging an answer the portal may already hold.
4. **No contract change.** The results schema already allows any person value and any
   qualifier string, and the portal already holds unknown names as proposals and counts a
   date of any qualifier. Reader-first rules are not triggered.

## Consequences

- The person register stays the single source of names: a typed name is a question for
  Franz, never a person, until he answers it on the portal's People page.
- The phase-4 rule "never type a name" (SPEC-2026-008 non-goal) is superseded by this
  decision.
