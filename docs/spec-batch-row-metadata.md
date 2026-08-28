# Batch and Row Metadata — Phase 19

**Status:** approved design, not yet built. Implements `docs/STATUS-AND-REMAINING-WORK.md` §6
items **#7** (batch-level fields stamped on every row) and **#10** (operator identity per row),
which that document names as the suggested next phase. Both sized **S**.

**Context in one paragraph:** the evidence capture station asks the operator for `Box` and
`Operator` on every single page. Both are constant for a whole box, and `EvidenceProfile` already
carries them as `Sticky` — but sticky only chains a value forward onto *new* documents, so the
first page still has to be typed and a later correction has to be re-typed onto every row it
already touched. This phase gives a field a **scope**: a batch-scoped field holds one value for
the whole group, entered once and stamped onto every exported row. Separately, it records **who
captured each page** as a system fact rather than a typed one, because on an evidence station
provenance that the operator can edit proves nothing.

---

## Decisions that bind this phase

1. **Scope is #7 + #10 only.** Not #4 rescan-in-place, not #9 tags, not #25 audit log.
2. **One group is one box.** A batch field is therefore a group-level value; there is no
   "applies from this page onward" notion.
3. **`capturedBy` is a system fact** — recorded at capture time, never editable.
4. **One source of truth.** A batch value lives only on the `Group`. Correcting `Box` from 12 to
   13 changes every row at once, because no row holds a copy that could drift.
5. **Search indexes batch values.** Otherwise `Box 12` silently stops matching.
6. **Required batch fields validate once per group**, not once per row.

## What this phase must not break

`CLAUDE.md` freezes three external contracts that the JimsStuff importer parses. This phase is
**additive against all three**:

- `index.json` row keys gain `capturedBy`. Nothing is renamed.
- `manifest.json` gains `scope` on each field entry. `evidenceExport` is untouched.
- The thirteen Evidence field names are unchanged. `Box` and `Operator` change *scope*, not name.

CSV, XLSX and XML column sets are unchanged: batch fields still appear as an ordinary column on
every row, which is exactly what #7 asked for.

---

## 1. Data model

One migration, `AddFieldScopeAndGroupBatchFields`. Two additive columns, no backfill:

| Change | Where | Default |
|---|---|---|
| `FieldScope { Row = 0, Batch = 1 }` | new enum, `Entities.cs` | — |
| `FieldDefinition.Scope` | `Entities.cs` | `Row` |
| `Group.BatchFieldsJson` | `Entities.cs` | `"{}"` |
| `EvidenceFieldSpec.Scope` | `Core/Evidence/EvidenceProfile.cs` | `Row` |

`Group.BatchFieldsJson` is a JSON object keyed by field name — deliberately the same shape as
`Document.CustomFieldsJson`, so one merge helper reads both.

Every existing field migrates as `Row`, so behaviour is unchanged until a field is deliberately
marked `Batch`.

`SchemaDocGenerator` renders `docs/db-schema.md` from the **live EF model**, so it needs no code
change — only a regenerate (`FGSCANNER_UPDATE_SCHEMA_DOC=1`).

`RawSchemaSql` needs one change: `v_index` exposes `d.CustomFieldsJson AS CustomFields`, and
`docs/db-schema.md` tells external tools to query the `v_*` views. After this phase that column
alone no longer holds `Box` or `Operator`, so the view must also expose the group's batch values
or it silently misleads. `v_pages` is left alone for `CapturedBy`, following the precedent set
when phase 17 added `OriginalChecksum` without extending the view.

There are two field shapes in this codebase and both need `Scope`: `FieldDefinition` is the
stored row, `EvidenceFieldSpec` is the contract as code. `ProfileService.EnsureEvidenceProfileAsync`
maps one to the other and must carry `Scope` across.

## 2. The merge seam

A new helper, `FgScanner.Core/Index/BatchFieldMerge.cs`, takes the schema's fields, the group's
batch values and one document's values, and returns the effective values for that row:

- **Batch-scoped names always resolve from the group.** Pending values and sticky chaining do not
  apply to them — for any given row, a batch field has exactly one source.
- **A batch field's `DefaultValue` seeds the group value at two moments.** At group creation it
  seeds directly: the default is token-expanded once and becomes the group value. At a schema
  upgrade that flips a field from `Row` to `Batch`, `SeedNewlyBatchFieldsAsync` (called from
  `UpgradeSchemaVersionAsync`, before the version pointer moves) seeds it again, in this order:
  (1) a non-empty value already in the group's batch bag wins and short-circuits; (2) otherwise
  the first non-empty value found among the group's documents, ordered by `Sequence`, is carried
  up — one group is one box, so those per-row values should already agree, and a disagreement is
  corrected once in the Batch values panel rather than losing every value; (3) otherwise the
  default is token-expanded exactly as at creation. Neither moment re-applies per row afterward,
  and the upgrade path reads documents only through a projection and writes none — the seed lands
  solely in the group's bag. This is what keeps `Operator`'s `$(user)` useful: the operator
  confirms a pre-filled name once per box instead of typing it. `$(counter)` is meaningless at
  group scope and expands against the group's first sequence.
- **Row-scoped names resolve from the document**, exactly as today.
- **A stale copy of a now-batch field left in a document's JSON is ignored, never read.** This is
  what makes "one source of truth" structural rather than a convention: flipping a field to
  `Batch` cannot leave a row quietly displaying its old private value.

Two readers share it: `IndexingService.BuildExportDataAsync` and the App's row grid
(`GroupDetailViewModel`). Both need the same thing — one dictionary of effective values for a
row — so both call `Effective` and get it.

`IndexingService.ValidateAsync` and `SearchService` implement the batch/row split themselves,
independently, at their own layer, rather than calling `Effective`:

- **Validation** needs to know *which* fields are batch-scoped in order to route each one to a
  group-level error instead of a per-row one — `Effective`'s merged dictionary throws that
  distinction away, which is exactly what validation needs to keep. It filters the schema by
  `Scope == Batch` for the once-per-group pass and by `Scope == Row` for the per-document pass.
- **Search** runs at the SQL layer, not in memory: it `LIKE`-matches `CustomFieldsJson` and
  `BatchFieldsJson` directly in the query (`EF.Functions.Like`) and, for a hit, reads whichever
  JSON blob matched to build the snippet, falling back from the document's JSON to the group's.
  An in-memory merge helper has no seam to attach to at that layer.

Both are the same rule as `Effective` — batch-scoped names come from the group, row-scoped names
come from the document — reimplemented on purpose because their layer cannot use the shared
helper. This is a known cost of the design, not a defect: keeping the two independent
implementations in agreement with `Effective`'s rule is a manual discipline, not a structural
guarantee, for these two call sites.

**No writer changes.** `Writers.cs` builds columns from `data.Fields` and reads
`row.CustomValues` (lines 33, 41, 204-209). Merging once inside `BuildExportDataAsync` puts batch
values on every row in all four export formats without touching a single writer.

**Search.** Batch values no longer live in `CustomFieldsJson`, so without a change, searching
`Box 12` silently returns nothing.

Field search is **not** FTS. `PagesFts` indexes OCR text only (`SearchService.FtsSearchAsync`);
field values are found by `FieldAndAiSearchAsync`, a `LIKE` over `Document.CustomFieldsJson`
(`SearchService.cs:111`). So the fix is to widen that query with an `OR` against the group's
`BatchFieldsJson`, and to build the snippet by checking the document's JSON first and the
group's second.

This is simpler than a re-index and worth stating: **nothing is re-indexed.** The query reads the
group row live, so correcting a batch value is searchable immediately, and the
"FTS rows must be UPDATEd, not delete+inserted" constraint in `docs/roadmap-v0.2.md` §9 does not
apply to this phase.

## 3. Group-level validation

`ValidateAsync` validates every field against every document, so today one missing `Box` produces
one identical error per row — two hundred of them for a two-hundred-page box. A batch-scoped
required field is validated **once**, against the group value.

`GroupValidation` gains a group-level error list alongside its per-document `DocumentValidation`
items, and the App's validation display follows. `Box` is the field that exercises this: it is
the profile's only required batch field.

## 4. `capturedBy` (item #10)

`Page.CapturedBy`, a nullable string, set from `Environment.UserName`:

- **Stamped** at the two genuine capture sites — `GroupService.cs:319` and
  `IndexingService.cs:427`.
- **Left null** at `RetroProcessService.cs:231`. Retro-processing adopts files that someone else
  scanned, possibly years ago on another machine; stamping the current Windows user as their
  captor would be a fabrication. Null means unknown provenance and is distinguishable from an
  empty string.

`IndexRow` gains an optional `CapturedBy` parameter — the same way `OriginalChecksum` was added
in phase 17 — and `IndexPayload.Build` emits `capturedBy` immediately after `originalChecksum`.

**JSON only.** This follows the established split: `sequence`, `pageId`, `checksum`, `isBlank`
and `originalChecksum` are machine facts that appear in `index.json` and not in the human-facing
formats. `capturedBy` is the same kind of fact.

Both captures of an annotated sheet get the same value; the sequence needs no special handling.

## 5. User interface

**Schema editor** (`SettingsViewModel.FieldRow`): a Batch checkbox beside Sticky, carried through
`From` and `ToDefinition`. Batch and Sticky are **mutually exclusive** — sticky means "chain this
row's value to the next row," which is meaningless for a value the group owns. Ticking Batch
disables Sticky.

**Group view:** a Batch fields panel, entered once and editable while the group is open. The row
grid shows batch columns read-only, so the operator can see the stamped value but cannot diverge
one row from the group. Exact siting is left to the implementation plan.

## 6. The Evidence profile

`EvidenceProfile.cs` changes two of its thirteen fields:

| Field | Before | After |
|---|---|---|
| `Box` | `Required: true, Sticky: true` | `Required: true, Scope: Batch` |
| `Operator` | `Sticky: true, DefaultValue: "$(user)"` | `Scope: Batch, DefaultValue: "$(user)"` |

Both lose `Sticky` because §5 makes the two exclusive. These are the two fields #7 was written
for, and marking them in code means the operator never ticks a box in the schema editor — the
walkthrough needs no new step, which is the point of the item.

**Left alone deliberately:** `NoteAuthor`, `NoteBasis` and `NoteWhen` stay sticky. They are sticky
because a box of one person's notes is the same three answers repeated, but they can legitimately
differ from sheet to sheet, and batch scope would make them group-constant and uneditable per row.
`DocNo` stays sticky: it is a per-document number and is never group-constant.

**`ProfileService.Unchanged` must compare `Scope`.** It already compares `Required` and `Sticky`
because they change behaviour; `Scope` changes behaviour more than either. Omitting it would make
"Build the Evidence profile" silently decline to apply the scope change when repairing a profile.

**One-time schema version bump.** The first press of "Build the Evidence profile" after this phase
genuinely changes `Box` and `Operator`, so it mints a new schema version — correct, and what the
comparison is for. CLAUDE.md's "re-seeding an intact profile mints no schema version" stays true:
pressing it twice after the change is still a no-op, which
`EvidenceProfileSeedTests.Seeding_twice_does_not_mint_a_second_schema_version` pins.

**Consequence to state plainly:** schema versions are immutable and groups pin one. Evidence
groups created before this phase keep row-scoped `Box` and `Operator`; only groups created after
the re-seed get batch behaviour. Nothing migrates them, and nothing should.

## 7. Profile file and manifest

`.fgprofile` goes to **FormatVersion 2**. `ProfileService` rejects anything but version 1 today;
the new reader accepts 1 (scope defaults to `Row`) and 2, and the writer emits 2. Profiles already
exported onto the hand-off USB stick stay readable.

`ManifestBuilder` adds `scope` to each entry in `Fields`, so an importer can tell which columns
are batch-constant for the whole folder rather than inferring it from repeated values.

## 8. Testing

- **Core:** `BatchFieldMerge` — batch resolves from the group; a stale document copy is ignored;
  row-scoped fields are untouched; pending values and sticky do not apply to batch fields.
- **Data:** a batch value appears on every exported row; correcting `Box` 12→13 changes all rows
  with no per-row write; a missing required batch value yields one group error, not N; a batch
  field's default seeds the group value once at creation and does not re-apply per row.
- **Schema:** a `Scope` flip mints a new version; groups pinned to the old version keep the old
  behaviour.
- **Profile:** `.fgprofile` round-trips at v1 (scope defaults to `Row`) and v2.
- **Search:** a batch value is findable by `LIKE`; correcting it is findable immediately, with no
  re-index step.

Two existing tests this phase must update, both owned by the evidence work:

- `EvidenceProfileTests.Authorship_fields_are_sticky_because_a_box_is_one_answer_repeated`
  asserts sticky is `[DocNo, Operator, Box, NoteAuthor, NoteBasis, NoteWhen]`; it becomes
  `[DocNo, NoteAuthor, NoteBasis, NoteWhen]`. Add a companion pinning scope as `[Box, Operator]`,
  so the batch contract is asserted as directly as the sticky one is.
- `EvidenceProfileSeedTests.Seeding_preserves_the_sticky_and_required_flags` iterates the specs
  and adapts on its own; extend it to assert `Scope` round-trips through the seed.

Two known snapshot casualties: Verify snapshots need re-approval for the `capturedBy` key, and
`CommitHookServiceTests` asserts the webhook payload shape — the same test that needed updating
for the added row in phase 16.

## 9. Out of scope

#4 rescan-in-place, #9 tags, #25 audit log. No Bates support, in this phase or any other.
`Feature.PreserveOriginals` is untouched.
