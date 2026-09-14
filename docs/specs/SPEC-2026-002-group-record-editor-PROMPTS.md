# SPEC-2026-002 — Prompt pack

Prompts for [SPEC-2026-002-group-record-editor.md](./SPEC-2026-002-group-record-editor.md) (Approved
2026-09-13, Revision B). Work through them in order. Each stops at a checkpoint; paste the results
back before starting the next.

**Before any prompt:** close FG Scanner. A copy running from `src\FgScanner.App\bin\Release` locks
its DLLs and every Release build fails with `MSB3027`.

**Gates**
- **Prompt 1 waits** until SPEC-2026-003 is merged to main. Both specs change shortcut handling
  (§16 R12) and edit the same docs.
- **Prompt 4 waits** on one answer from Franz. His Round A note on N4 may mean a text box's
  on-screen width should follow its length, which §03 currently rules out. Record the answer in §05
  before building the form.

**Branch:** `phase-22-record-editor`.

**Everywhere:** the editor serves every profile, not only Evidence (Franz, Round A). No new code may
branch on the Evidence profile or its field names.

| # | Prompt | Phase | Needs |
|---|---|---|---|
| 1 | Fields carry a length and a memo flag | 1 | SPEC-003 merged |
| 2 | Settings and the grid | 2 | Prompt 1 green |
| 3 | Editor view model and layout store | 3 | Prompt 2 green |
| 4 | Editor window | 3 | Prompt 3 green **and** the N4 answer recorded |
| 5 | Delete, add, reloads and close | 3 | Prompt 4 green |
| 6 | Code review — **new session** | — | Prompt 5 green |
| 7 | Docs, ADR, manual tests, finish | — | Prompt 6 resolved |

---

```
PROMPT 1 of 7 — Fields carry a length and a memo flag
Spec: SPEC-2026-002 §04, §05 (Q3, Q4, N1–N5, N7), §07, §10 AC-1..AC-5, AC-8..AC-10, AC-14, §11.1, §16 R2, R3, R7, R9, R10
Phase: 1    Depends on: SPEC-2026-003 merged to main

Read SPEC-2026-002 §04, §07, §08 and §16 before starting. Read CLAUDE.md (evidence contract).

Setup (safe to re-run):
- `git status` and `git log --oneline -5`. If SPEC-2026-003's merge is not on main, stop and report.
  Other Claude sessions edit this repo; if main holds code you did not expect, stop and report.
- Create or switch to branch `phase-22-record-editor`.
- If docs/specs changes for this spec are uncommitted, commit them first.
- If this prompt's commit already exists, report that and stop.

Do only this:
1. Pin today's export output FIRST. In tests/FgScanner.Core.Tests/IndexExporterTests.cs, add a Verify
   snapshot test "manifest fields ignore length and memo" (AC-10). It must pass on today's code;
   commit the .verified file with it.
2. Write the failing tests from §11.1:
   - FieldLengthTests.cs: AC-1..AC-4
   - FieldValidatorTests.cs: AC-5
   - EvidenceProfileSeedTests.cs: AC-8
   - ProfileImportExportTests.cs: AC-9
   - MigrationFixtureTests.cs: AC-14
   Run them and confirm each fails for the right reason.
3. FieldDefinition gains `int? MaxLength` and `bool Memo`, with the doc comments §07 names.
   Add migration `AddFieldLengthAndMemo` the same way the existing migrations were made; read
   20260828162943_AddFieldScopeAndGroupBatchFields first. Its Up must contain only two AddColumn
   calls.
4. ProfileService:
   - SaveSchemaAsync copies both. It clears them on non-Text fields (N1) and enforces 1–100, or
     1–2000 when Memo, with a readable message.
   - Unchanged compares both.
   - EnsureEvidenceProfileAsync carries both forward by field name from the latest schema (N5), and
     still mints no version when the profile is intact.
5. Pass MaxLength to validation:
   - IndexFieldDef gains a trailing `int? MaxLength = null`.
   - Add a `FieldDefinition.ToIndexFieldDef()` helper and use it at every place that builds
     IndexFieldDef by hand (R9). Grep `new IndexFieldDef(` and account for every hit.
   - FieldValidator adds the length rule: "{Name} is {n} characters; the limit is {MaxLength}."
     Add one comment that length counts UTF-16 characters.
6. .fgprofile:
   - FgProfileField gains MaxLength and Memo.
   - Write FormatVersion 3 only when some field uses either; otherwise write 2 (N7).
   - Import accepts 1–3. An out-of-range length in a file becomes null and never fails the import.
7. Commit: "Let text fields carry a length and a memo flag".

Do NOT in this prompt:
- touch Settings, the Groups grid or any window (Prompts 2–5)
- write length or memo into manifest.json, index.json, CSV, XLSX or XML (N4)
- add a FieldType.Memo (§08 says why)
- change MaxFields
- trim any stored value (N2)

Stop when the checkpoint below is satisfied and report the results.
```

```
CHECKPOINT 1 — paste back:
  · dotnet build -c Release → 0 Warning(s), 0 Error(s)
  · dotnet test -c Release → total ≥ the count after SPEC-003 plus the new tests, 0 failed
  · dotnet format --verify-no-changes → exit 0
  · the migration's Up method, pasted → two AddColumn calls, nothing else
  · `git grep -n "new IndexFieldDef("` → every hit accounted for
  · git log -p on the manifest .verified file → added once in step 1, unchanged since
```

---

```
PROMPT 2 of 7 — Settings and the grid
Spec: SPEC-2026-002 §03, §08 (Length enforcement, Grid), §09, §10 AC-7, AC-17, AC-18, §12, §16 R4, R5, R11
Phase: 2    Depends on: Prompt 1 (green)

Read SPEC-2026-002 §08 and §16 R4–R5 before starting.

Setup: on branch `phase-22-record-editor` with Prompt 1 committed. If this prompt's commits already
exist, report that and stop.

Do only this:
1. Pure move first:
   - Extract RebuildColumns (GroupsView.xaml.cs:350) into
     `EntryGridColumns.Build(DataGrid, IReadOnlyList<FieldDefinition>)`, changing no behaviour.
   - Keep batch columns read-only, required "*" in headers, list combos and column order.
   - Run the full suite, then commit: "Move entry grid column building into a shared builder".
2. Write the failing tests FIRST.
   tests/FgScanner.App.Tests/TextLengthGuardTests.cs, against a pure decision function:
   - A typed character past the limit is refused.
   - A paste that would reach 150 against 100 is refused with a message naming both numbers, and
     the text is unchanged.
   - A paste that fits is accepted.
   - Replacing a selection counts the selected characters out.
   - With no limit, everything is allowed.
   FieldRow round-trip of Length and Memo (AC-17).
3. TextLengthGuard attached behaviour:
   - Handle PreviewTextInput and DataObject.Pasting.
   - Never set TextBox.MaxLength; it silently cuts pasted text (R4).
   - The refusal message reaches the owning view's status line.
4. Grid:
   - Text columns get the guard.
   - Memo columns show one line with an ellipsis. Display only — the value is untouched.
5. Settings:
   - FieldRow gains Length (int?) and Memo (bool) columns, disabled for non-Text types.
   - SettingsViewModel maps both ways.
   - The label at SettingsView.xaml:43 reads "up to 16".
   - A range error from SaveSchemaAsync shows the way save errors show today.
6. Commit: "Set field length and memo in Settings, and guard them in the grid".

Do NOT in this prompt:
- build the editor window, its view model or the layout store (Prompts 3–5)
- change column order or any existing column behaviour (R5)

Stop when the checkpoint below is satisfied and report the results.
```

```
CHECKPOINT 2 — paste back:
  · git log --oneline -2 → the move commit, then the feature commit
  · dotnet build -c Release, dotnet test -c Release, dotnet format --verify-no-changes → all green
  · `git grep -n "MaxLength=" src/FgScanner.App` → no TextBox.MaxLength use
  · Manual (Franz), on a non-Evidence profile:
      - Give one Text field Length 10 and another Memo → save → a new version is made.
      - In Groups, press "Use latest field layout".
      - Typing an 11th character is refused.
      - Pasting 20 characters is refused, with a message.
      - The memo column shows one line.
      - A Date field's Length box is disabled.
```

---

```
PROMPT 3 of 7 — Editor view model and layout store
Spec: SPEC-2026-002 §05 (Q5, N6), §06 A5, §07 (UI layout), §08 (RecordEditorViewModel, Form, Layout, Reloads), §10 AC-6, AC-11, AC-12, AC-13, §12, §16 R6
Phase: 3    Depends on: Prompt 2 (green)

Read SPEC-2026-002 §07, §08 and §12 before starting. Read how the existing GroupDetailViewModel
tests observe persistence.

Setup: on branch `phase-22-record-editor` with Prompts 1–2 committed. If this prompt's commit
already exists, report that and stop.

Do only this:
1. Write the failing tests FIRST.
   tests/FgScanner.App.Tests/RecordEditorLayoutStoreTests.cs (AC-12):
   - Saves under key `RecordEditor.Layout.{groupId}`.
   - A group not seen before returns `RecordEditor.Layout.Last`.
   - A saved layout larger than the given window is clamped.
   - Corrupt JSON gives the defaults and logs a warning.
   - Memo sizes are kept by field name. Width is clamped to the pane width; height to 2–20 lines.
   tests/FgScanner.App.Tests/RecordEditorViewModelTests.cs:
   - "form edit persists through the row path" — exactly one merge carrying the edited key (AC-11)
   - "over-length stored value is flagged, never trimmed" (AC-6)
   - "batch field in form writes to the group" — shows on every row (AC-13)
   - reloading Rows re-selects the same page by DocumentId (A5)
   - an empty group gives the "No pages in this group yet" state
   - FormField flags per field type
2. RecordEditorLayoutStore (src/FgScanner.App/Services/) over AppSettingsService. Clamp and fallback
   logic is WPF-free.
3. RecordEditorViewModel (src/FgScanner.App/Views/):
   - Built from GroupDetailViewModel; shares its Rows, BatchFields, SelectedRow and commands.
   - The form binds the same RowValues the grid binds. No copied values.
   - FormField items, with Next and Previous commands.
4. GroupDetailViewModel.OpenRecordEditorCommand builds the view model. It opens the window through
   a seam, like the viewer's, so tests show no window.
5. Commit: "Add the record editor's view model and layout store".

Do NOT in this prompt:
- write XAML, the window or the Groups button (Prompt 4)
- change delete, add or undo behaviour (Prompt 5)
- add AI suggestions (N9)
- reference the Evidence profile

Stop when the checkpoint below is satisfied and report the results.
```

```
CHECKPOINT 3 — paste back:
  · dotnet build -c Release, dotnet test -c Release, dotnet format --verify-no-changes → all green
  · git diff --stat HEAD~1 → the store, the view model, GroupDetailViewModel.cs and the two test files
  · `git grep -n "Evidence" -- src/FgScanner.App/Views/RecordEditor* src/FgScanner.App/Services/RecordEditor*` → no hits
```

---

```
PROMPT 4 of 7 — Editor window
Spec: SPEC-2026-002 §03, §05 (Q1, Q3, Q6, Q7, N8, N9), §08 (Form, Memo box, Layout, Image pane, Window), §09, §10 AC-15, §16 R11, R12
Phase: 3    Depends on: Prompt 3 (green) AND the N4 answer recorded in §05

GATE — check first: SPEC-2026-002 §05 "Franz's notes" must record Franz's answer on whether a text
box's on-screen width follows its length. If it still says "Open", STOP and ask Franz. If the answer
is yes, the spec needs revising before this prompt runs.

Read SPEC-2026-002 §08, §09 and §16 before starting. Read SPEC-2026-001's GroupsView preview code.
WPF `Image Stretch="None"` lays out in DPI-scaled units, so zoom math uses ImageLayout, never
PixelWidth.

Setup: on branch `phase-22-record-editor` with Prompts 1–3 committed. If this prompt's commit already
exists, report that and stop.

Do only this:
1. RecordEditorWindow (Views/Dialogs, beside PageViewerWindow):
   - Outer grid rows: top `*` | 6 splitter | grid `*`.
   - Top columns: form `*` | 6 splitter | image `*`.
   - Splitter DragCompleted and closing save through the layout store. Restore on load, clamped.
2. Form:
   - An ItemsControl of FormField items. Label in an Auto column sharing a SharedSizeGroup, input in
     `*`.
   - TextBox, ComboBox or DatePicker by type.
   - Required fields get "*". The validation message sits under the input in IndianRed.
   - AutomationProperties.LabeledBy on every input.
   - TextLengthGuard on text inputs.
   - Batch fields in a block at the top (N6).
   - The form pane scrolls both ways.
3. Memo box:
   - A wrapping TextBox with AcceptsReturn false (Q3). Enter moves focus to the next field, which
     needs a KeyDown handler.
   - It sits inside a ResizableBox with an always-visible Thumb grip at the bottom-right.
   - Width is clamped to the form pane's viewport; height to 2–20 lines.
   - Sizes are saved through the store.
4. Image pane:
   - ScrollViewer + Image + ZoomController/FitPolicy/ImageLayout, with −, +, Fit, 100% and a zoom %
     label, the same as the Groups preview.
   - A missing file shows "Image file not found" and its path.
5. Grid pane:
   - EntryGridColumns.Build.
   - Row virtualization kept, which needs a finite height.
6. Window:
   - Modal, owned by the main window.
   - WindowSizing.FitToWorkArea, with minimum 900×600.
   - Its own KeyBindings (R12): Ctrl+PageDown, Ctrl+PageUp, Delete, Ctrl+Z, Ctrl+Y.
   - Tab order: form → zoom buttons → grid.
7. Add a "Record editor…" button on the Groups toolbar, bound to OpenRecordEditorCommand.
8. Commit: "Add the record editor window".

Do NOT in this prompt:
- change delete, add, re-select on close or reload handling (Prompt 5)
- give non-memo fields their own widths (unless the gate answer revised the spec)
- add rich text
- allow a second editor at once

Stop when the checkpoint below is satisfied and report the results.
```

```
CHECKPOINT 4 — paste back:
  · dotnet build -c Release, dotnet test -c Release, dotnet format --verify-no-changes → all green
  · Manual (Franz), at 1280×1024, on an Evidence group:
      - Select a page and open the editor → all 13 fields can be read, with scrolling at most.
      - Drag both dividers. A memo grip cannot drag wider than its pane.
      - Close and reopen → the sizes come back. A different group keeps its own.
      - Ctrl+PageDown → the form, image and grid move together.
      - An over-long paste is refused with a message.
  · The same open, resize and reopen checks on a non-Evidence group with a memo field
```

---

```
PROMPT 5 of 7 — Delete, add, reloads and close
Spec: SPEC-2026-002 §05 Q2, §08 (Reloads, Window), §10 AC-16, §12, §16 R6
Phase: 3    Depends on: Prompt 4 (green)

Read SPEC-2026-002 §08 and §12 before starting.

Setup: on branch `phase-22-record-editor` with Prompts 1–4 committed. If this prompt's commit already
exists, report that and stop.

Do only this:
1. Write the failing tests FIRST, in RecordEditorViewModelTests.cs:
   - Delete sends the page to Trash through DeleteSelectedCommand, and the editor moves to the next
     page.
   - Deleting the last page leaves the editor open on the empty state.
   - After Add missed page or Import reloads Rows, the editor stays on the same page by DocumentId.
   - On close, Groups selects the page the editor was on.
2. The editor toolbar:
   - Delete page (the existing confirmation and Trash path)
   - Add missed page…
   - Import PDF/images…
   - Undo, Redo
   - Previous and Next, with "Page 3 of 40" as text
3. When Rows is replaced while the editor is open (for example OCR finishing), re-select the same
   page by DocumentId.
4. On close, GroupDetailViewModel selects the page the editor was on, as OpenPageViewer does.
5. Commit: "Wire delete, add and reloads into the record editor".

Do NOT in this prompt:
- add blank records with no image (Q2)
- add bulk edit
- change the Scan page

Stop when the checkpoint below is satisfied and report the results.
```

```
CHECKPOINT 5 — paste back:
  · dotnet build -c Release, dotnet test -c Release, dotnet format --verify-no-changes → all green
  · Manual (Franz):
      - Delete a page in the editor on a committed group → it is in Trash, and index.json no longer
        lists it (AC-16).
      - Import 2 images → the editor stays on its page.
      - Close → the Groups grid is on that page.
```

---

```
PROMPT 6 of 7 — Code review  [START A NEW SESSION FOR THIS]
Spec: SPEC-2026-002    Depends on: Prompt 5 (green)

The session that wrote the code remembers what it meant and reads intent into the code. A fresh
session sees what is actually there. Do not run this in the session that did Prompts 1–5.

On branch `phase-22-record-editor`, run /code-review max over `git diff main...HEAD`.

Then check compliance with SPEC-2026-002, which is the primary criterion:
- Does the code do what §10 AC-1..AC-18 say, and only that? Name any criterion with no test or
  manual row behind it.
- Walk §16 R1–R12: is each mitigation actually in the code?
- Is anything built that §03 lists as a non-goal?
- The contract checks, one line of evidence each:
  - the manifest snapshot has not changed since Prompt 1 step 1
  - there is no FieldType.Memo
  - TextBox.MaxLength is never set
  - Evidence repair keeps lengths
  - nothing exports length or memo
  - no new code branches on the Evidence profile
  - NoteState's sticky rule (CLAUDE.md) is untouched
- Check migration safety against a 0.4.0-shaped database.
Report every drift. Each is either a bug in the code or a spec that needs revising.

Resolve every correctness finding, with a failing test first wherever one can be written. For
anything you decline to change, add one line to SPEC-2026-002 §22 saying what and why.

Not web-facing: no security prompt (§13).
```

```
CHECKPOINT 6 — paste back:
  · the /code-review max findings and what happened to each
  · the compliance table: AC → test or manual row → present yes/no
  · the contract checks with evidence
  · dotnet build -c Release, dotnet test -c Release, dotnet format --verify-no-changes → all green
```

---

```
PROMPT 7 of 7 — Docs, ADR, manual tests, finish
Spec: SPEC-2026-002 §17, §18, §21, §22    Depends on: Prompt 6 (resolved)

Read SPEC-2026-002 §17, §18 and §21.

Do only this:
1. docs/adr/0009-field-length-and-memo.md: memo is a flag and not a type, and nothing is exported.
   0006–0008 are reserved by the Quick Scan plan. If 0009 is taken by now, use the next free number
   and update §18.
2. CLAUDE.md — one line in the evidence section: field length and memo are layout and validation
   settings, not part of the export contract; see the ADR.
3. docs/manual-tests.md — a "Record editor" block covering the Prompt 2, 4 and 5 manual steps, on an
   Evidence group and on a non-Evidence group.
4. docs/user-guide.md — a Record editor section, plus field length and memo in the profile section.
5. docs/FEATURE-PARITY.md — add a row.
   build/installer/evidence-setup-walkthrough.txt — mention the editor as optional in its Groups part.
6. Memory: memo is a Text flag because FieldType casts positionally to IndexFieldType and its name is
   exported, so nobody should "tidy" it into a type.
7. Tick the §21 boxes that are actually true, with evidence. Leave the station and rollback boxes
   unticked unless done. docs/specs/README.md → "Built — manual checks pending".
8. Commit: "Document the record editor and field lengths".
9. Ask Franz:
   - before opening a PR and merging (CI must be green)
   - whether to bump <Version> and build the installer for the station — this is the first database
     migration since 0.4.0, so the station needs §17's backup check

Do NOT in this prompt: change code, bump <Version> or build the installer without Franz's yes.
```

```
CHECKPOINT 7 — paste back:
  · git log --oneline main..HEAD
  · the §21 checklist as ticked
  · Manual (Franz), §17 rollback check on the dev machine:
      1. Copy %APPDATA%\FGScanner\fgscanner.db somewhere safe.
      2. Start the new build once, and confirm a fresh fgscanner.db.bak- file appears.
      3. Start 0.4.0 → its groups open.
      4. Put the copy from step 1 back.
  · Franz's answers on the merge and on a station release
```
