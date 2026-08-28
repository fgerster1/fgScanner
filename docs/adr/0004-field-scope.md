# ADR-0004 — A field can be scoped to the group instead of the row

**Status:** accepted, 2026-08-28

## Context

The evidence capture station asked the operator to answer `Box` and `Operator` on every single
page, even though both are constant for an entire box: one group is one box. `EvidenceProfile`
already marked them `Sticky`, but sticky only chains a value forward onto the *next* new
document — the first page of a box still had to be typed by hand, and a correction (`Box` 12 to
13) had to be retyped onto every row it had already reached. For a two-hundred-page box that is
two hundred edits for one fact, and a missed row is a wrong `Box` value nobody notices until
JimsStuff import.

## Decision

A field now carries a **scope**: `FieldScope { Row, Batch }`, added to `FieldDefinition` (the
stored schema row) and to `EvidenceFieldSpec` (the profile-as-code shape in
`FgScanner.Core/Evidence/EvidenceProfile.cs`), both defaulting to `Row` so every existing field
is unaffected until it is deliberately marked `Batch`.

A batch value lives **only** on `Group.BatchFieldsJson` — the same JSON-object shape as
`Document.CustomFieldsJson`, keyed by field name. There is no per-row copy to drift: a batch
field has exactly one source of truth for the whole group.

`FgScanner.Core/Index/BatchFieldMerge.Effective` resolves what a row shows: batch-scoped names
resolve from the group, row-scoped names resolve from the document, and a stale copy of a
now-batch field left in a document's own JSON is never read. Two callers share it —
`IndexingService.BuildExportDataAsync` (`IndexingService.cs:354`) and the App's entry grid
(`GroupDetailViewModel.cs:174`) — so export and the grid agree by construction: what the operator
sees is what `index.csv` gets.

Two more call sites implement the same batch/row rule independently, at their own layer, rather
than calling `Effective`:

- `IndexingService.ValidateAsync` (`IndexingService.cs:242-293`) needs to know *which* fields are
  batch-scoped so it can route each to a group-level error or a per-document one — `Effective`'s
  merged dictionary throws away exactly the distinction validation needs to keep. It filters the
  schema by `Scope == FieldScope.Batch` for the once-per-group pass (line 259) and by
  `Scope == FieldScope.Row` for the per-document pass (line 276).
- `SearchService.FieldAndAiSearchAsync` (`SearchService.cs:104-157`) never calls
  `BatchFieldMerge` at all. It runs the batch/row check at the SQL layer — an `EF.Functions.Like`
  `OR` across `Document.CustomFieldsJson` and `Document.Group.BatchFieldsJson` in the query
  itself — then, for a hit, builds the snippet by parsing the document's JSON first and the
  group's second (`FieldSnippet`, line 168 on). An in-memory merge helper has no seam to attach
  to at the SQL layer.

This is a known cost of the design, not a solved problem: two readers share `Effective` and are
guaranteed to agree with it by construction; the other two reimplement its rule by hand because
their layer requires it, and keeping those two in agreement with `Effective` is a discipline a
future change must maintain, not a guarantee the type system enforces.

A batch field's `DefaultValue` seeds the group's value once, at group creation, token-expanded
at that moment (`GroupService.cs`, around the `TokenExpander.Expand(field.DefaultValue!,
group.Name, counter: 1)` call). It never re-applies per row. This is what keeps `Operator`'s
`$(user)` useful under batch scope: the operator confirms a pre-filled name once per box instead
of typing it once per page.

Required batch fields validate **once per group**, not once per row: `GroupValidation` gained a
`GroupErrors` list alongside the existing per-document errors, so one missing `Box` is one error,
not one per page.

Search had to widen to match: field search is a `LIKE` over `Document.CustomFieldsJson`
(`SearchService.cs`), not FTS, so a value that moved to `Group.BatchFieldsJson` needed the query
widened with an `OR` against the group row. Nothing is re-indexed — the query reads the group row
live, so correcting a batch value is searchable immediately.

The Evidence profile (`EvidenceProfile.cs`) now marks `Box` (`Required: true`) and `Operator`
(`DefaultValue: "$(user)"`) as `Scope: Batch`. Both lose `Sticky`, because sticky and batch are
mutually exclusive in the schema editor (sticky means "chain this row's value to the next row,"
which is meaningless for a value the group owns). `DocNo`, `NoteAuthor`, `NoteBasis` and
`NoteWhen` stay row-scoped and sticky, unchanged: `DocNo` is a per-document number and is never
group-constant; the `Note*` fields can legitimately differ sheet to sheet within a box, so batch
scope would wrongly make them group-constant.

`ProfileService.Unchanged` now compares `Scope` alongside `Required`, `Sticky`, `DefaultValue`
and `ListChoicesJson` — omitting it would make "Build the Evidence profile" silently decline to
apply the scope change when repairing a profile.

`.fgprofile` moves to `FormatVersion 2`, which carries `Scope` per field; the reader still
accepts version 1 files (scope defaults to `Row`), so profiles already exported to the hand-off
USB stick stay readable. `manifest.json` gains `scope` on each field entry
(`FgScanner.Core/Index/Writers.cs`, `ManifestBuilder.Build`) so an importer can tell which
columns are batch-constant for the whole folder instead of inferring it from repeated values.
`evidenceExport` and every existing key are untouched, and none of the thirteen Evidence field
names changed — only two of them changed scope.

## Consequences

- **A one-time schema version bump.** The first press of "Build the Evidence profile" after this
  change genuinely changes `Box` and `Operator` (scope, and the loss of `Sticky`), so it mints a
  new schema version — that is what `ProfileService.Unchanged` comparing `Scope` is for.
  Pressing the button again afterward is still a no-op, exactly as before
  (`EvidenceProfileSeedTests.Seeding_twice_does_not_mint_a_second_schema_version`).
- **Pre-existing groups are deliberately not migrated.** Schema versions are immutable and each
  group pins the one it was created with. Evidence groups created before this change keep
  row-scoped `Box` and `Operator` and behave exactly as they do today; only groups created after
  the profile is rebuilt get batch behaviour. Nothing migrates an existing group's schema, and
  nothing should — rewriting a committed evidence folder's schema after the fact is not a thing
  this app does.
- CSV, XLSX and XML are unaffected: a batch field still appears as an ordinary column on every
  row, because the merge happens once inside `BuildExportDataAsync`, before any writer runs.
  `index.json` and `manifest.json` change additively only (see ADR-0005 for the related
  `capturedBy` addition).
- A field can be flipped between `Row` and `Batch` in the schema editor at any time; each flip is
  a schema-version change like any other field edit, and groups on the old version keep the old
  behaviour by the same pinning mechanism above.

## Alternatives rejected

**Keep `Sticky` and make it smarter (e.g. "sticky forever until changed").** Still leaves a
per-row copy that can drift — a row edited independently after the sticky value was chained would
silently disagree with the group, which is exactly the bug this ADR closes.

**Store the batch value once but still copy it into every document's `CustomFieldsJson` at
write time.** Reintroduces N copies of one fact; a correction would again have to touch every
row, just automated instead of manual.
