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

1. **A typed name first tries the list.** It is matched against every display name and
   spelling in the package's `people.json` the way the portal normalises a spelling
   (punctuation to spaces, spaces collapsed, lower case). A match stages that person's
   id. Only an unmatched name travels as typed. A rare normalisation difference only
   means the portal makes the match itself.
2. **A typed name must be plain, trimmed, at most 120 characters, and never shaped like
   an id.** The plain-text rule is the decider name's, now shared
   (`IndexAnswerVocabulary.IsPlainText`), because both must survive the contract's byte
   rule. `P0042` is refused because the portal reads it as an id and would refuse the
   whole file when its register has no such person.
3. **"Undated" is the qualifier `undated` with the value `undated`.** It cannot be an
   empty value, because the contract reads an empty value as a withdrawal. The writer
   refuses `undated` mixed with a real date either way. Switching between a dated and an
   undated answer uses the existing qualifier-change path: export withdraws the portal's
   old date slot before the new answer.
4. **No contract change.** The results schema already allows any person value and any
   qualifier string, and the portal already holds unknown names as proposals and counts a
   date of any qualifier. Reader-first rules are not triggered.

## Consequences

- The person register stays the single source of names: a typed name is a question for
  Franz, never a person, until he answers it on the portal's People page.
- The phase-4 rule "never type a name" (SPEC-2026-008 non-goal) is superseded by this
  decision.
