# SPEC-2026-002 — Group record editor with sized fields

| | |
|---|---|
| **Status** | Approved |
| **Revision** | B |
| **Tier** | Feature |
| **Author** | Claude, for Franz Gerster |
| **Date** | 2026-09-13 |
| **Project** | FgmakerScanner |
| **Supersedes** | — |

Part of the 2026-09-13 "Fixes and Changes" request. Depends on
[SPEC-2026-001](./SPEC-2026-001-fit-and-scrolling.md) Prompts 1 (Fit) and 2 (scroll host).

---

## 01 · Management summary

**What we are building.** A **record editor** opened from the Groups page, laid out in three parts:
- **Top left:** the page's fields as a form — field name, then the box to type in.
- **Top right:** the page image.
- **Bottom:** the grid of all pages in the group.

All three parts can be resized by dragging, and the editor remembers their sizes for that group.
Two new field settings come with it: a **maximum character length** (1–100) for text fields, and a
**memo** option for long text, whose box the operator can stretch taller and wider.

**Why.** Today every value is typed into a narrow grid cell. Long answers such as Title, Parties or
Notes are hard to read and correct while looking at the page, and nothing stops a value running to
any length.

**What it costs.** Two to four days: 7 prompts across 3 phases. One small database change that only
adds columns. No new dependencies, no recurring cost.

**What could go wrong.** Field settings live inside the Evidence profile machinery that the
JimsStuff pipeline depends on. Done carelessly, a length setting could change what the importer
receives, or be silently wiped by "Build the Evidence profile". This spec keeps the export contract
byte-for-byte unchanged and names the tests that prove it.

**Decided (Round A, 2026-09-13).**
- The editor is a separate modal window.
- In it you can edit values, delete a page to Trash, and add pages with the existing buttons.
- Memo text wraps on screen but stores no line breaks, and is limited to 2000 characters.
- Pane sizes are remembered per group.
- Typing stops at a field's limit, and a paste that would go over is refused with a message.

## 02 · Outcomes

- **Goal** — correct and complete a page's index values while looking at the page, without
  scrolling a grid sideways, on the station's screen.
- **Who benefits** — Jim entering Evidence values, and Franz indexing groups under other profiles.
  Nothing in the editor may depend on the Evidence profile (Franz, Round A: "not only for Jim's stuff").
- **How we will know it worked**
  - All 13 Evidence fields of a selected page can be read and edited in one view at the smallest
    supported screen, with scrolling at most (manual, AC-15).
  - A value past its length is caught before commit, never silently cut (tests AC-5, AC-6).
  - Pane and memo sizes come back the same when the editor is reopened on that group (AC-12).

## 03 · Scope and non-goals

**In scope**
- `RecordEditorWindow`, opened from a new "Record editor…" button on the Groups toolbar.
- Three panes (form, image, grid) with draggable dividers.
- **Form**: one row per row-scope field — name, then input (TextBox, ComboBox for List, DatePicker
  for Date), with the validation message under the input. A "Batch values" block at the top.
- Image pane with zoom, Fit and 100%, reusing SPEC-001's corrected fit.
- Previous and next page (buttons and keys); the grid, form and image stay in step.
- Per §05 Q2: delete the page to Trash, and "Add missed page…" / "Import PDF/images…" from the editor.
- Pane sizes and memo box sizes remembered per group (§05 Q5).
- Field settings: `MaxLength` for Text fields; `Memo` for Text fields.
- The Settings field grid gains "Length" and "Memo" columns; its stale "up to 12" label reads 16.
- The Groups grid honours lengths; memo fields show one line there.
- `.fgprofile` round-trips the new settings.
- Database migration adding two columns.

**Non-goals**
- No new field **type** — memo is a setting on Text, not a type (§08 explains why).
- No change to `manifest.json`, `index.json`, CSV, XLSX or XML output.
- Existing values are never trimmed.
- No record without an image (a record *is* a page).
- No bulk edit across pages (the existing "Apply to all rows" stays where it is).
- No field reordering, no per-field width for non-memo fields, no rich text.
- The Evidence profile's own definitions do not gain lengths.
- No editor on the Scan page.
- No two editors open at once.

## 04 · Current state

- **Stack** — .NET 10 WPF Fluent, CommunityToolkit MVVM, EF Core 10 + SQLite. 545 tests passing
  (Release, `4e3b954`, after SPEC-001 merged).
- **Line numbers** below were read at `605ce9d`, before SPEC-001 merged. They were re-checked at
  `4e3b954`:
  - `RebuildColumns` is now at `GroupsView.xaml.cs:350`.
  - `OpenPageViewer` is at `GroupDetailViewModel.cs:317-336`.
  - The `ProfileService` lines are unchanged.
  - Other `GroupsView.xaml(.cs)` lines have moved — find them by name.
- **Field model** — `FieldDefinition` (`src/FgScanner.Data/Entities.cs:114-143`): Id, SchemaId,
  Order, Name, Type, Required, Sticky, Scope, DefaultValue, ListChoicesJson. **No length, width or
  multiline property exists.**
  - `FieldType { Text, Date, Number, List }` (`Entities.cs:3-9`) mirrors
    `IndexFieldType` (`src/FgScanner.Core/Index/IndexModels.cs:11-17`). The two are cast
    positionally: `DocumentRow.cs:117`, `ProfileService.cs:86`.
  - `IndexFieldDef(Name, Type, Required, Scope)` (`IndexModels.cs:30-31`).
- **Schema versions** — immutable. `ProfileService.SaveSchemaAsync` (`ProfileService.cs:198-255`)
  enforces `MaxFields = 16` and unique names, returns the current version when `Unchanged`, and
  otherwise mints Version+1. `Unchanged` (`:262-278`) compares Name, Type, Required, Sticky, Scope,
  DefaultValue and ListChoicesJson.
- **Evidence repair** — `EnsureEvidenceProfileAsync` (`ProfileService.cs:70-99`) rebuilds all 13
  fields from `EvidenceProfile.Fields` (`src/FgScanner.Core/Evidence/EvidenceProfile.cs:30-73`) and
  calls `SaveSchemaAsync`. Anything not in that code is reset on repair.
- **.fgprofile** — `FormatVersion` 2 (`ProfileService.cs:320-338,346-351`). Import refuses anything
  but 1 or 2 (`:365-369`), so an older build refuses a newer file.
- **Export contract** — `ManifestBuilder` writes each field's Name, Type (lower-case), Required and
  Scope (`src/FgScanner.Core/Index/Writers.cs:242-248`). The JimsStuff importer carries `fields`
  through verbatim (`JimsStuff/pipeline/import_fgscanner.py:264`) and splits Parties on `;`
  (`:170-171`). A grep found no handling of line breaks or field types.
- **Validation** — `FieldValidator.Validate(IndexFieldDef, value, choices)`
  (`src/FgScanner.Core/Index/FieldValidator.cs:9-26`) covers required, date, number and list only.
  `RowValues` validates per cell via `INotifyDataErrorInfo` and skips batch fields
  (`src/FgScanner.App/Views/DocumentRow.cs:104-130`).
- **Persisting a value** — `RowValues` indexer set → `ValueChanged` → `PersistRowAsync` →
  `MergeFieldValuesAsync` (`GroupDetailViewModel.cs:190,608-633`; `IndexingService.cs:75-109`),
  then re-export if the group is committed.
- **Groups page** — `GroupsView.xaml`:
  - top panel (`:84-261`): batch and pending value cards 160 px wide (`:115,167`) — the only
    label-and-input editors today — and the two toolbars
  - grid (`:272-300`) with columns built in code-behind `RebuildColumns`
    (`GroupsView.xaml.cs:275-339`); batch columns read-only
  - preview pane (`:307-384`); splitters saved to the global keys `Session.PreviewPanelWidth/Height`
    (`GroupsView.xaml.cs:15-16`)
- **Modal precedent** — `OpenPageViewer` shows `PageViewerWindow` modally and re-selects the grid row
  by `DocumentId` on close (`GroupDetailViewModel.cs:316-336`).
- **Settings** — field grid at `SettingsView.xaml:43-87`; `FieldRow` ↔ `FieldDefinition` at
  `SettingsViewModel.cs:729-789`. The label at `SettingsView.xaml:43` still says "up to 12".
- **Migrations** — latest `20260828162943_AddFieldScopeAndGroupBatchFields`. Startup migrates
  automatically after writing `fgscanner.db.bak-<version>`, which STATUS observed working on
  2026-08-27.
- **Not examined** — `IndexingService.ValidateAsync` body; how `GroupDetailViewModel` reloads
  `Rows` after OCR and edits; the CSV/XLSX/XML writers' handling of line breaks; the JimsStuff
  portal's display of multi-line values; `SettingsViewModel`'s profile load path.

## 05 · Questions for Franz

**Answered 2026-09-13, Round A** — [review page](https://claude.ai/code/artifact/b2c902c4-c72e-4530-816f-fdc3512cddc4),
db doc `review/SPEC-2026-002-rA`, verdict **approve**. Every blocking question took option (a), and
every call N1–N9 was agreed.

| # | Answer |
|---|---|
| Q1 | (a) Separate modal window |
| Q2 | (a) Edit, delete to Trash, add with the existing buttons |
| Q3 | (a) Wraps on screen; Enter moves to the next field; no line breaks stored |
| Q4 | (a) Memo 1–2000 characters |
| Q5 | (a) Per group; a group not opened before starts from the last layout |
| Q6 | (a) "The size" is the draggable divider |
| Q7 | (a) Typing stops at the limit; an over-long paste is refused with a message |

Franz's notes, verbatim:
- **General** — "This is not only for Jim's stuff, but I will also be scanning other information."
  Applied to §02, AC-15 and §11.2: the editor works for any profile, and the manual checks run on a
  non-Evidence group too.
- **N4** — "This is for screen editing, so the actual character length does matter; viewing the
  material is what matters." Read as agreement that lengths serve on-screen editing and stay out of
  the export files. **Answered 2026-09-14 (Franz):** "No — length and width should be allowed to be
  different; they should be independent of each other." A text box's on-screen width does not follow
  its character length. §03 stands: ordinary boxes fill the form width, and memo boxes are resized
  by dragging. The Prompt 4 gate is cleared.

The questions as asked are kept below for the record.

**Blocking**

1. **How does the record editor open?**
   - (a) **A separate full-size window from a Groups button, modal like the page viewer.** *(recommended)*
   - (b) A view switch inside the Groups page that replaces the grid and preview.
   - (c) A separate window you can leave open beside Groups.
   *Why it matters:* (a) is one editing surface at a time; the Groups grid reloads when you close
   it — the pattern the viewer already uses. (b) needs no new window but has less room, because
   the Groups toolbars keep their height. (c) lets two screens edit the same page at once. The last
   write per field wins, but each screen shows stale values until reloaded.
2. **What does "CRUD" cover here?**
   - (a) **Edit values, delete the page (to Trash), and add pages with the existing "Add missed page…" and "Import" buttons.** *(recommended)*
   - (b) Edit values only.
   - (c) Also create a blank record with no image.
   *Why it matters:* a record is a page. A blank record has no image and no checksum, which
   `index.json`'s contract cannot represent, so (c) means changing the evidence export.
3. **Can memo text contain line breaks?**
   - (a) **No — text wraps on screen, Enter moves to the next field, no line breaks stored.** *(recommended until the importer is checked)*
   - (b) Yes — Enter starts a new line, stored in the value.
   *Why it matters:* line breaks flow into CSV, XLSX, XML and `index.json`. The importer passes
   fields through untouched and nothing shows it handles them. Many CSV consumers break on them.
   (b) is fine if JimsStuff confirms.
4. **How long can a memo be?**
   - (a) **1–2000 characters.** *(recommended)*
   - (b) The same 1–100 as other text.
   - (c) No limit.
5. **Which pane sizes are remembered?**
   - (a) **Per group; a group you have not opened before starts from the last layout you used.** *(recommended)*
   - (b) One layout for every group.
   - (c) Only while the window is open.
6. **What did "the size" mean** in "fields on the left, the size, and the image on the right"?
   - (a) **The draggable divider between the fields and the image.** *(my reading)*
   - (b) Show the image's size information (pixels, DPI).
   - (c) Something else — please say in the note.
7. **What happens when someone types or pastes past the limit?**
   - (a) **Typing stops at the limit; a paste that would go over is refused with a message saying how long it is.** *(recommended)*
   - (b) The text is allowed and shown in red as invalid until shortened (blocks Commit).
   *Why it matters:* WPF's built-in `TextBox.MaxLength` **silently cuts pasted text**. On evidence,
   a quietly truncated title is data loss nobody sees, so either answer avoids that property.

**Non-blocking** — proceeding on these unless corrected.

1. Length applies to **Text fields only**. Date, Number and List already have their own rules; on
   those types the length is cleared on save.
2. **Shortening a length never trims existing values.** Longer values show as invalid, like any
   other invalid value, until someone corrects them.
3. Changing length or memo **creates a new field-layout version**, like any field edit. Existing
   groups keep theirs until "Use latest field layout" is pressed.
4. Lengths and memo **are not written** to `manifest.json` or the index files.
5. **"Build the Evidence profile" keeps** lengths and memo the operator set on fields whose names
   match, instead of wiping them.
6. **Batch fields appear at the top of the form and are editable there**. They write to the group,
   exactly like the Batch values panel.
7. `.fgprofile` files are written as **version 3 only when some field uses length or memo**,
   otherwise version 2, so older stations can still import ordinary profiles.
8. Keyboard: **Ctrl+PageDown / Ctrl+PageUp** for next and previous page. The editor has its own
   Delete, Ctrl+Z and Ctrl+Y bindings, because a modal window does not receive the main window's
   shortcuts.
9. Follow the app's WPF Fluent theme. The FG Maker "Organic" kit is for web apps and does not apply.

## 06 · Assumptions

| # | Assumption | If wrong |
|---|---|---|
| A1 | ≤ 16 fields fit a scrollable form usably | A two-column form; Prompt 4 changes |
| A2 | Title, Parties and Notes are the fields that want memo | None — memo is per field and optional |
| A3 | Adding nullable/defaulted columns is safe for 0.4.0 databases and the automatic backup runs | Migration needs a manual backup step on the station |
| A4 | The JimsStuff importer tolerates nothing new because nothing new is exported (§05 N4) | If lengths *should* be exported, that is an importer change and a separate agreement |
| A5 | Row reloads replace `DocumentRow` instances, and the editor can re-select by `DocumentId` | Editor needs a different refresh hook; Prompt 5 changes |

## 07 · Data model

**`FieldDefinition`** (`src/FgScanner.Data/Entities.cs`) — two new properties:

| Property | Type | Default | Rules |
|---|---|---|---|
| `MaxLength` | `int?` | `null` (no limit — today's behaviour) | Text only. 1–100 when `Memo` is false; 1–2000 when true (§05 Q4). Cleared on non-Text types |
| `Memo` | `bool` | `false` | Text only. Cleared on non-Text types |

- **Doc comments on both properties** state: display and validation only; not part of the export
  contract; why memo is not a `FieldType`.
- **Migration** `AddFieldLengthAndMemo` — adds `MaxLength INTEGER NULL` and
  `Memo INTEGER NOT NULL DEFAULT 0` to `FieldDefinitions`. No backfill: every existing field reads
  as no limit, not memo.
- **`ProfileService`**
  - `SaveSchemaAsync` copies both and validates the ranges at the boundary (throws with a readable
    message).
  - `Unchanged` compares both.
  - `EnsureEvidenceProfileAsync` carries both forward from the latest schema for matching field
    names (§05 N5).
- **`IndexFieldDef`** gains `int? MaxLength = null` as a trailing defaulted parameter.
  `ManifestBuilder` names its properties explicitly (`Writers.cs:242-248`), so output is unchanged.
- **`FieldValidator`** adds: "{Name} is {n} characters; the limit is {MaxLength}." Length counts
  UTF-16 characters (`string.Length`), documented beside the rule.
- **`.fgprofile`** — `FgProfileField` gains init-props `MaxLength` and `Memo`. Written as version 3
  only when used (§05 N7). Import accepts 1–3. An out-of-range length in a file is dropped to null,
  never a failed import (file boundary).
- **Not schema — UI layout** in `AppSettingsService` (key-value `Settings` table):
  - key `RecordEditor.Layout.{groupId}` → JSON
    `{"formWidth":…, "topHeight":…, "memo":{"<FieldName>":{"width":…,"height":…}}}`
  - key `RecordEditor.Layout.Last` → the same shape, the starting point for an unseen group
  - unreadable JSON falls back to defaults with a warning log
  - stale keys for deleted groups are harmless; cleaning them up is a non-goal

## 08 · Architecture and approach

- **`RecordEditorViewModel`** (`src/FgScanner.App/Views/RecordEditorViewModel.cs`).
  - Built by a new `GroupDetailViewModel.OpenRecordEditorCommand`.
  - It is handed the detail VM's existing `Rows`, `BatchFields`, `SelectedRow` and commands
    (`DeleteSelectedCommand`, `AddMissedPageCommand`, `ImportFilesCommand`, `UndoCommand`,
    `RedoCommand`).
  - **The form binds to the same `RowValues` instance the grid binds to**, so persistence,
    validation and committed-group re-export are the existing path, not a copy of it.
- **Form** — an `ItemsControl` of `FormField` view items (Field, IsText, IsMemo, IsList, IsDate,
  Choices, MaxLength).
  - Each row is a two-column `Grid`: the label in an `Auto` column shared by `SharedSizeGroup`,
    then the input in `*`.
  - The form pane sits in a `ScrollViewer` with both bars Auto.
- **Length enforcement** (§05 Q7a) — a small attached behaviour `TextLengthGuard` rejects typed
  input past the limit and **refuses** an over-long paste with a status message. It never truncates
  and never uses `TextBox.MaxLength`. Its decision function is pure and tested.
- **Memo box** — multi-line wrapping `TextBox` inside a `ResizableBox`, a `Thumb` grip at the
  bottom-right.
  - Width is clamped to the form pane's viewport width ("width only what fits in the quadrant").
  - Height is clamped to 2–20 lines.
  - Sizes go through the layout store.
- **Layout** — outer `Grid` rows: top `*` | splitter 6 | grid `*`. Top `Grid` columns: form `*` |
  splitter 6 | image `*`.
  - `RecordEditorLayoutStore` (`src/FgScanner.App/Services/`) loads, clamps and saves over
    `AppSettingsService`.
  - Its clamp and fallback logic is WPF-free and unit-tested.
  - Saved on splitter `DragCompleted` and on close.
- **Image pane** — `ScrollViewer` + `Image` + `ZoomController`/`FitPolicy`, identical to the Groups
  preview after SPEC-001.
- **Grid** — extract `RebuildColumns` from `GroupsView.xaml.cs:275-339` into
  `EntryGridColumns.Build(DataGrid, IReadOnlyList<FieldDefinition>)`. Groups and the editor then
  share one column builder, adding the length guard and one-line memo display.
- **Reloads** — when `Rows` is replaced (after OCR, edits or deletes), the editor re-selects by
  `DocumentId`, the same approach as `GroupDetailViewModel.cs:332-335`.
- **Window** — modal, owner = main window. On close, the Groups grid selects the page the editor
  was on. Sized with SPEC-001's `WindowSizing.FitToWorkArea`, minimum 900×600.

**Alternatives rejected**
- **DataGrid RowDetails form** — no resizable image pane beside it, and the form scrolls away with
  its row.
- **A new shell section** — loses the group context and duplicates group selection.
- **A new `FieldType.Memo`** — breaks the positional casts `(IndexFieldType)field.Type`
  (`DocumentRow.cs:117`, `ProfileService.cs:86`) and would put a new `"type"` string into
  `manifest.json` (`Writers.cs:245`), which is external contract.
- **`TextBox.MaxLength`** — silently truncates paste (§05 Q7).

**Data access.** None new. The layout keys are a few hundred rows at most; key-value is the right
store.

**System evolution.** The planned project skill `fgscanner-wpf-section` (Quick Scan plan §7.2) should
record the shared `EntryGridColumns` builder and the form row pattern, so Quick Scan and later
screens reuse them rather than re-deriving them.

## 08a · AI opportunity assessment

| | |
|---|---|
| **What it would do** | Suggest Title, Parties and DocDate in the form from the page's OCR text |
| **Why AI beats deterministic code here** | Titles and parties appear in no fixed position or wording; regexes fail on handwritten notes and letters. Gemini is already integrated (BYO key) |
| **Model and rough cost per operation** | A Gemini Flash-class model, roughly a few hundredths of a cent per page from existing OCR text (no image upload) |
| **What happens when it is wrong** | Suggestions only, never auto-applied. But on evidence, a suggestion anchors the operator; the Evidence profile's own comments say nothing may pressure a value toward a guess (`EvidenceProfile.cs:65-66`) |
| **Recommendation** | **Defer** to its own spec (PLAN §7 #12 "AI extraction" already lists it), with an evaluation plan and an Evidence-profile opt-out. It needs this form to exist first |

**Learning loop** — not applicable while deferred. When built: store accepted and rejected
suggestions per field; write on operator action; retrieve as few-shot examples per profile; drop a
pattern after repeated rejection.

## 09 · Design system compliance

The app's WPF Fluent theme governs (§05 N9).

- **Labels** — default text in a shared `Auto` column. Required fields append "*", matching the
  grid header convention (`GroupsView.xaml.cs:315-336`).
- **Errors** — `IndianRed` text under the input, matching the grid's red cell border
  (`GroupsView.xaml:292`).
- **Resize grip** — always visible, not hover-only.

**Accessibility**
- Tab order runs down the form, then the image zoom buttons, then the grid.
- Every input is labelled by `AutomationProperties.LabeledBy` pointing at its field-name text.
- Focus rings are Fluent defaults.
- The page position ("Page 3 of 40") is text, not colour.

## 10 · Acceptance criteria

- **AC-1** — Length and memo save and reload on a field definition.
  *Proven by:* `tests/FgScanner.Data.Tests/FieldLengthTests.cs` → "saves and reloads length and memo"
- **AC-2** — Changing only a length mints a new layout version; saving the identical layout again
  does not.
  *Proven by:* `FieldLengthTests.cs` → "length change mints a version; identical save does not"
- **AC-3** — Save refuses length 0, 101 (non-memo) and 2001 (memo) with a readable message.
  *Proven by:* `FieldLengthTests.cs` → "rejects out-of-range lengths"
- **AC-4** — Length and memo on a non-Text field are cleared on save.
  *Proven by:* `FieldLengthTests.cs` → "non-text fields drop length and memo"
- **AC-5** — A value over the limit fails validation with its length named; exactly at the limit
  passes.
  *Proven by:* `tests/FgScanner.Core.Tests/FieldValidatorTests.cs` → "over-length text is invalid; at the limit is valid"
- **AC-6** — An existing over-length value loads unchanged and shows invalid.
  *Proven by:* `tests/FgScanner.App.Tests/RecordEditorViewModelTests.cs` → "over-length stored value is flagged, never trimmed"
- **AC-7** — The paste guard refuses an over-long paste and leaves the text unchanged; typing
  stops at the limit.
  *Proven by:* `tests/FgScanner.App.Tests/TextLengthGuardTests.cs`
- **AC-8** — "Build the Evidence profile" keeps operator lengths and memo on matching fields, and
  mints no version when intact.
  *Proven by:* `tests/FgScanner.Data.Tests/EvidenceProfileSeedTests.cs` → "repair keeps operator lengths"
- **AC-9** — `.fgprofile` round-trips length and memo as version 3; a profile using neither
  writes version 2; a version-2 file imports with no limits.
  *Proven by:* `tests/FgScanner.Data.Tests/ProfileImportExportTests.cs`
- **AC-10** — `manifest.json` field entries are identical before and after a field gains a length.
  *Proven by:* `tests/FgScanner.Core.Tests/IndexExporterTests.cs` (Verify snapshot) → "manifest fields ignore length and memo"
- **AC-11** — Editing a value in the form persists through `MergeFieldValuesAsync` once and
  updates the grid's cell.
  *Proven by:* `RecordEditorViewModelTests.cs` → "form edit persists through the row path"
- **AC-12** — The layout store saves per group, restores clamped to the window, falls back to the
  last layout for an unseen group, and survives corrupt JSON.
  *Proven by:* `tests/FgScanner.App.Tests/RecordEditorLayoutStoreTests.cs`
- **AC-13** — A batch field edited in the form writes to the group and shows on every row.
  *Proven by:* `RecordEditorViewModelTests.cs` → "batch field in form writes to the group"
- **AC-14** — The migration applies to a 0.4.0-shaped database; existing fields read null/false.
  *Proven by:* `tests/FgScanner.Data.Tests/MigrationFixtureTests.cs` → "field length migration is additive"
- **AC-15** — On an Evidence group and on a group under another profile, the editor opens on the
  selected page. All three panes resize. A memo box cannot be
  dragged wider than its pane. Sizes return on reopen for that group, and a different group keeps
  its own.
  *Proven by:* manual — Franz, `docs/manual-tests.md` § Record editor
- **AC-16** — Delete in the editor sends the page to Trash and re-exports a committed group.
  *Proven by:* existing delete tests plus manual row
- **AC-17** — Settings shows Length and Memo columns, and the label reads "up to 16".
  *Proven by:* `tests/FgScanner.App.Tests` → `FieldRow` round-trip test; manual look
- **AC-18** — Full suite green; the `EntryGridColumns` extraction keeps existing grid tests green.
  *Proven by:* `dotnet test -c Release`

## 11 · Test strategy

**11.1 — The failing tests to write first**

| Feature | Test file | The failing assertion |
|---|---|---|
| Persist length/memo | `tests/FgScanner.Data.Tests/FieldLengthTests.cs` | reloaded `MaxLength == 40`, `Memo == true` |
| Version on change | `FieldLengthTests.cs` | version increments when only length changes |
| Range guard | `FieldLengthTests.cs` | `SaveSchemaAsync` throws for 101 |
| Validator | `tests/FgScanner.Core.Tests/FieldValidatorTests.cs` | 41 chars against 40 returns the message |
| Evidence repair | `tests/FgScanner.Data.Tests/EvidenceProfileSeedTests.cs` | Notes keeps `Memo == true` after repair |
| .fgprofile | `tests/FgScanner.Data.Tests/ProfileImportExportTests.cs` | exported JSON has `"FormatVersion": 3` and `MaxLength` |
| Manifest unchanged | `tests/FgScanner.Core.Tests/IndexExporterTests.cs` (Verify) | snapshot matches the pre-change file |
| Paste guard | `tests/FgScanner.App.Tests/TextLengthGuardTests.cs` | 150-char paste into 100 → refused, text unchanged |
| Layout store | `tests/FgScanner.App.Tests/RecordEditorLayoutStoreTests.cs` | unseen group returns the last layout |
| Form persistence | `tests/FgScanner.App.Tests/RecordEditorViewModelTests.cs` | one merge call with the edited key |
| Migration | `tests/FgScanner.Data.Tests/MigrationFixtureTests.cs` | columns exist; old rows null/false |

**11.2 — Test data.** The Data tests' existing temp SQLite databases. The migration test migrates to
`AddFieldScopeAndGroupBatchFields` first, inserts a field, then applies the new migration. The
manifest snapshot comes from the existing export test fixtures. Manually:
1. Build the Evidence profile.
2. Give Title a length of 80 and turn on memo for Notes.
3. Scan or import a few pages.
4. Repeat the open, resize and reopen checks on a group under an ordinary non-Evidence profile that
   has a memo field (Franz, Round A).

**11.3 — Verification suite.** Close FG Scanner first (a running Release copy locks the build). Then
`dotnet build -c Release`, `dotnet test -c Release`, `dotnet format --verify-no-changes`. Manual:
`docs/manual-tests.md` § Record editor.

## 12 · Edge cases and failure modes

| Case | Expected behaviour | Where handled |
|---|---|---|
| Value exactly at the limit | Valid | `FieldValidator` |
| Emoji or accented text | Counted as UTF-16 characters (an emoji may count 2); documented | `FieldValidator` |
| Paste over the limit | Refused with message; nothing cut (§05 Q7a) | `TextLengthGuard` |
| Length shortened below existing values | Values kept, flagged invalid, Commit blocked until fixed | validator + existing commit gate |
| Memo flag on a batch field | Memo box in the form's Batch block | form template |
| Group on an older layout without lengths | No limits shown; banner offers the latest layout as today | existing schema notice |
| Group with no pages | Form shows "No pages in this group yet"; add buttons enabled | editor VM |
| Page image missing on disk | Image pane says "Image file not found" with the path; form still editable | image pane |
| Rows reloaded while the editor is open (OCR finishing) | Editor re-selects the same page by `DocumentId` | editor VM (A5) |
| Delete the last page | Editor stays open, empty state | editor VM |
| Layout JSON corrupt | Defaults used, warning logged, JSON rewritten on next save | layout store |
| Saved layout bigger than this screen | Clamped | layout store |
| Committed group edited | Re-export after each edit, as today | existing `PersistRowAsync` |
| `.fgprofile` v3 opened by 0.4.0 | 0.4.0 refuses with its existing message; §05 N7 limits this to profiles using the feature | `ProfileService` |

## 13 · Security and configuration

Not web-facing. Input is validated at two boundaries: `SaveSchemaAsync` checks length ranges, and
`.fgprofile` import is file input, where bad values degrade to null. No dependencies, no environment
variables, no secrets.

## 14 · Observability

- **Logging** — Serilog warning when layout JSON cannot be read (group id, not contents). Existing
  error logging covers persistence failures in `PersistRowAsync`.
- **A silent failure would look like** a value typed in the form that never reaches the database.
  AC-11 prevents it structurally: the form uses the grid's own row object, and the grid updating
  live is visible proof.
- **A silent contract break would look like** new keys in `manifest.json`. AC-10's snapshot fails
  the build.

## 15 · Performance and scale

At most 16 fields per form; groups of a few hundred to a few thousand pages. The grid keeps row
virtualization (SPEC-001 R1). The layout store is one key read on open and one write on close or
drag. Not a performance question; revisit only if a profile ever exceeds `MaxFields`.

## 16 · Regression risk

| # | What could break | Why | Evidence (file:line) | Mitigation |
|---|---|---|---|---|
| R1 | Every group shows "Use latest field layout" after a length change | Any field change mints a version | `ProfileService.cs:221-231`, `GroupDetailViewModel.cs:102-110` | Intended (§05 N3); status line and banner already explain it |
| R2 | "Build the Evidence profile" silently wipes lengths and mints a version | Repair rebuilds fields purely from code | `ProfileService.cs:82-97` | Carry length and memo forward by name; AC-8 |
| R3 | JimsStuff importer receives a new type or key | A `Memo` type or exported length would change manifest fields | `Writers.cs:242-248`, `import_fgscanner.py:264` | Memo is a flag; nothing exported; AC-10 snapshot |
| R4 | Pasted evidence text silently truncated | `TextBox.MaxLength` cuts paste without telling anyone | WPF behaviour | Never use `MaxLength`; `TextLengthGuard`; AC-7 |
| R5 | Groups grid columns change (batch read-only, required *, list combos) | `RebuildColumns` moves into a shared builder | `GroupsView.xaml.cs:275-339` | Move without behaviour change first, existing `BatchFieldColumnsTests` green, then add |
| R6 | Editor edits a stale row after a reload | Reloads replace `DocumentRow` instances | `GroupDetailViewModel.cs:332-335` pattern | Re-select by `DocumentId`; edge case table |
| R7 | Older stations refuse newer `.fgprofile` files | Import accepts only 1 and 2 | `ProfileService.cs:365-369` | Write v3 only when used (§05 N7); AC-9 |
| R8 | Line breaks break CSV consumers or the importer | Memo newlines flow into every export | `Writers.cs`, `import_fgscanner.py:264` | §05 Q3 default (a): no line breaks stored |
| R9 | A validator caller ignores the new length | Six places build `IndexFieldDef` by hand and would need to pass `MaxLength` | `DocumentRow.cs:117`, `GroupDetailViewModel.cs:164`, `IndexingService.cs:168,310,328,359` | Trailing defaulted parameter keeps them compiling. Prompt 1 passes `MaxLength` at all six, or adds a `FieldDefinition.ToIndexFieldDef()` helper and uses it everywhere — the batch/row rule already drifted this way (ADR-0004) |
| R10 | Station database migration fails mid-way | First migration since 0.4.0 | `Migrations/` | Automatic `fgscanner.db.bak-<version>`; §17 checks it exists before first launch |
| R11 | Settings field grid gets wider than a small screen | Two more columns | `SettingsView.xaml:44-81` | SPEC-001 scroll host (dependency) |
| R12 | Shortcuts do nothing in the editor | Main-window key bindings don't reach a modal window | `ShellWindow.xaml.cs:62-91` | Editor declares its own Delete/Ctrl+Z/Ctrl+Y (§05 N8) |

## 17 · Migration and rollback

- **Forward** — the new build migrates on first launch and writes `fgscanner.db.bak-<version>`
  first. On the station:
  1. Close FG Scanner.
  2. Install the new version.
  3. Start FG Scanner once.
  4. Open `%APPDATA%\FGScanner\` and check that a `fgscanner.db.bak-` file with today's time is there.
- **Backward** — install 0.4.0 again. 0.4.0 does not read the two new columns, so its groups work.
  Lengths and memo are ignored until the newer build returns. For a clean rollback, restore the
  `.bak` file:
  1. Close FG Scanner.
  2. Rename `fgscanner.db` to `fgscanner.db.new`.
  3. Rename the `.bak` file to `fgscanner.db`.
- **Point of no return** — none. The migration is additive, and the backup is taken before it runs.
- **Backup** — automatic (above), verified by step 4. Check once on the dev machine, by opening a
  migrated database with the 0.4.0 build, before taking the build to the station.

## 18 · Documentation updates

- **CLAUDE.md** — one line in the evidence section: field length and memo are layout and validation
  settings, not part of the export contract; see ADR-0009.
- **`docs/adr/0009-field-length-and-memo.md`** — why memo is a flag and not a type, and why nothing
  is exported. 0006–0008 are reserved by the Quick Scan plan.
- **`docs/manual-tests.md`** — "Record editor" block.
- **`docs/user-guide.md`** — Record editor section; field length and memo in the profile section.
- **`docs/FEATURE-PARITY.md`** — row for the record editor.
- **`build/installer/evidence-setup-walkthrough.txt`** — no change required; mention the editor as
  optional in its Groups part.
- **Memory** — record "memo is a Text flag because `FieldType` casts positionally to
  `IndexFieldType` and its name is exported", so nobody later "tidies" it into a type.

## 19 · Phase plan

### Phase 1 — Fields can carry a length and a memo flag
- **Objective** — data model, migration, validation, `.fgprofile`, Evidence repair.
- **Delivers** — AC-1 to AC-5, AC-8, AC-9, AC-10, AC-14.
- **Not in this phase** — any UI.
- **Done when** — tests pass, the manifest snapshot is unchanged, and the migration applies.

### Phase 2 — Settings and grid know about it
- **Objective** — the operator can set length and memo, and the Groups grid honours them.
- **Delivers** — AC-7, AC-17, and the shared `EntryGridColumns` (AC-18 for the grid).
- **Not in this phase** — the record editor window.
- **Done when** — Settings round-trips and the grid guards paste.

### Phase 3 — The record editor
- **Objective** — the three-pane window.
- **Delivers** — AC-6, AC-11, AC-12, AC-13, AC-15, AC-16.
- **Not in this phase** — AI suggestions, bulk edit, Scan-page editing.
- **Done when** — the manual block passes on a 13-field Evidence group at the smallest screen.

## 20 · Prompt pack

See [SPEC-2026-002-group-record-editor-PROMPTS.md](./SPEC-2026-002-group-record-editor-PROMPTS.md) —
7 prompts, written 2026-09-13 on approval:
1. Schema, migration and validation
2. Settings and grid
3. Editor view model and layout store
4. Editor window
5. Wiring, delete and add
6. Code review (fresh session)
7. Docs, ADR and manual tests

## 21 · Definition of done

- [ ] All acceptance criteria met
- [ ] Failing tests written first, now passing
- [ ] Full suite green
- [ ] `/code-review max` run, findings resolved or accepted in writing
- [ ] Security review — _not applicable, not web-facing_
- [ ] Documentation updated per §18
- [ ] Installed on the station; migration backup confirmed; manual block walked
- [ ] Rollback checked once on the dev machine (0.4.0 opens a migrated database)

## 22 · Sign-off

| | |
|---|---|
| **Review round answered** | ☑ date: 2026-09-13 · Round A: https://claude.ai/code/artifact/b2c902c4-c72e-4530-816f-fdc3512cddc4 (db doc `review/SPEC-2026-002-rA`) |
| **Franz approved** | ☑ date: 2026-09-13 (Round A, verdict approve) |
| **Manual checks walked** | ☑ date: 2026-09-16 · Franz, dev machine, on the build at `3f907f9`. The Prompt 4 block (form, dividers, memo grip, sizes remembered per group, Ctrl+PageUp/PageDown, over-long paste refused, form edit reaching the grid, Delete inside a text field editing text rather than deleting the page, tab order, zoom) and the Prompt 5 block (delete to Trash with `index.json` updated, landing on the next page, import keeping the page, selection on close, empty state) — all passed, nothing failed. AC-15 and AC-16 evidenced. |
| **Built** | ☐ date: |
| **Verified in production** | ☐ date: |
