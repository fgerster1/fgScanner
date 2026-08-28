# ADR-0005 — `CapturedBy` is null for files this station did not capture

**Status:** accepted, 2026-08-28

## Context

Nothing in FG Scanner recorded who scanned a given page. On an evidence station that is a gap:
`docs/spec-batch-row-metadata.md` item #10 asks for capture identity as a system fact rather than
a typed field, because a value the operator can edit proves nothing about who actually ran the
scanner.

But FG Scanner has more than one way a `Page` row comes into existence. Two are treated as
capture at this station: a normal scan (`GroupService`, the page created while scanning into a
group) and `IndexingService.InsertMissedPageAsync` — used both when the operator locates a page
that was missed and inserts it into an already-scanned group, and for the new page a split
creates. Both put the operator physically at this station, right now, choosing what goes into
the group. A third is not a capture at all: `RetroProcessService` bulk-adopts every image file
already sitting in a folder — files that may have been scanned years ago, on another machine, by
another person, then handed to this station for indexing after the fact.

## Decision

`Page.CapturedBy` is a nullable string, set to `Environment.UserName`:

- **Stamped** at the two capture-equivalent sites: `GroupService.cs:352` (scanning into a group)
  and `IndexingService.cs:475`, inside `InsertMissedPageAsync` (adding a missed page or a split's
  new page).
- **Left null** at `RetroProcessService.cs:231`, where an existing file on disk is adopted into a
  group. The current Windows user did not capture that file — they are indexing it, possibly long
  after the fact — and stamping their name would assert something false about who ran the
  scanner.

Null is distinguishable from an empty string, so "unknown provenance" is a first-class,
queryable state rather than an empty field that looks like a typo or an unanswered form.

`IndexRow` gained `CapturedBy` as an optional trailing parameter (`IndexModels.cs`), the same
pattern used for `OriginalChecksum` in phase 17. `index.json` gained the `capturedBy` key
immediately after `originalChecksum`; the human-facing exports (CSV/XLSX/XML) do not carry it,
following the same split those two keys already established — `sequence`, `pageId`, `checksum`,
`isBlank` and `originalChecksum` are machine facts for `index.json` only, and `capturedBy` is the
same kind of fact.

Both halves of an annotated-sheet capture (as-found and clean) get the same value; no special
handling was needed for that sequence.

## Consequences

- A retro-processed evidence folder has every page's `CapturedBy` null, permanently — there is no
  path that backfills it later, because backfilling would mean guessing, and a guessed captor is
  worse than an absent one on an evidence station.
- Any downstream consumer (JimsStuff import, an eventual audit view) must treat `capturedBy: null`
  as "not recorded," not as "recorded and blank," and must not default it to the importing
  operator's name.
- Both capture-equivalent sites key off `Environment.UserName`, so `CapturedBy` records the
  Windows account under which FG Scanner ran the capture, not an identity FG Scanner verifies
  independently. That is consistent with how `Operator` already works (`$(user)` default) and is
  not a new trust boundary.

## Alternatives rejected

**Stamp the current user at adoption time too, on the theory that some record beats none.** A
fabricated provenance is worse than an absent one: on a station whose output feeds a legal
pipeline, a wrong "who captured this" is a claim someone could rely on, while a null is honestly
"we don't know."

**Prompt the operator for a captor name during retro-processing.** Adds a typed field for
exactly the kind of fact item #10 was written to get *out* of operator hands, and the operator
running retro-process usually cannot know who scanned a file years earlier any better than the
software can.
