# SPEC-2026-005 — Memo fields and right-sized dropdowns in the record editor

| | |
|---|---|
| **Status** | Approved |
| **Revision** | B |
| **Tier** | Feature |
| **Author** | Claude, for Franz Gerster |
| **Date** | 2026-09-20 |
| **Project** | FgmakerScanner |
| **Supersedes** | — |

---

## 01 · Management summary

- **What we are building** — three display fixes in the record editor, so an operator can
  actually read what is stored: a memo field opens as a readable multi-row box instead of
  a two-line slot, "Memo" becomes a visible choice when setting a field up, and a list
  field's dropdown is only as wide as its longest value instead of stretching across the
  form.
- **Why** — reported after a working session on real evidence. The memo box already wraps
  text, but it opens at roughly 120×40 pixels, so a long note shows two lines and the
  operator must drag a small grip on every field before they can check the data. The
  Memo setting is a checkbox in a crowded grid and reads as an afterthought, so it was
  not found at all — hence the request for "a field type: memo".
- **What it costs** — 5 prompts across 2 phases, half a day to a day. No schema change,
  no new dependency.
- **What could go wrong** — the export contract. `FieldType` is cast positionally to
  `IndexFieldType` and its **name** is written into `manifest.json`, which the JimsStuff
  importer reads. ADR-0009 settled this: memo is a flag on Text, never a type. This spec
  changes only what the operator *sees* in the Type list; what is stored and exported is
  byte-identical, and a Verify snapshot proves it.
- **What we need from you** — three decisions in §05: how the memo box should size itself
  by default, whether Memo joins the Type dropdown, and how far the dropdown-width change
  should reach.

## 02 · Outcomes

- **Goal** — an operator checking a scanned record can read a long note without
  resizing anything, and can scan a form of fields without their eye travelling across a
  half-empty dropdown.
- **Who benefits** — whoever is verifying records against the paper: Jim at the station,
  Franz reviewing a box afterwards.
- **How we will know it worked** — opening a record with a filled `Notes` field shows the
  text on several lines with no dragging; a `DocType` dropdown is about as wide as
  "Correspondence" rather than the width of the form pane.

## 03 · Scope and non-goals

**In scope**

- A default memo size that shows several rows, used when the operator has not dragged one
  before.
- "Memo" presented as a choice when defining a field in Settings, mapping to the existing
  Text + `Memo` flag underneath.
- List dropdowns in the record editor sized to their content.

**Non-goals**

- **No change to the export contract.** `FieldType` keeps its four members and its
  positional cast; `manifest.json` keeps emitting `Text` for a memo field (ADR-0009).
- No line breaks inside memo values. `AcceptsReturn="False"` stays: Enter moves to the
  next field, and no value gains a newline (SPEC-2026-002 §05 Q3).
- No per-field widths for ordinary text fields — an ordinary text box still fills the
  form width whatever its length limit (ADR-0009:46-48, Franz 2026-09-14).
- No change to the 1–100 / 1–2000 character limits.
- No rich text, no formatting, no spell check.

## 04 · Current state

**Where the fields are rendered.** The record editor uses **one** `DataTemplate` for
every field, with all four inputs stacked in the same cell and only the matching one
visible (`src/FgScanner.App/Views/Dialogs/RecordEditorWindow.xaml:19-24`, template at
`:24`). Visibility is driven by `IsPlainText` / `IsMemo` / `IsList` / `IsDate` on
`FormField` (`src/FgScanner.App/Views/RecordEditorViewModel.cs:290, 296, 298, 300`).

**The memo box already does most of what was asked** (`RecordEditorWindow.xaml:45-69`):
`TextWrapping="Wrap"`, `VerticalScrollBarVisibility="Auto"`, a drag grip that is always
visible, and a size persisted per group per field. What it lacks is a **default size**:

```xml
57  MinWidth="120" MinHeight="40" Padding="2,1"
```

There is no `MinLines`, no `Height`, no `Rows` anywhere in the repo. `ApplyMemoSize`
(`RecordEditorWindow.xaml.cs:190-202`) only runs when a stored size exists, so a field
nobody has dragged opens at about two lines. Stored sizes are clamped by
`RecordEditorLayoutStore.cs:28-33` — `MinMemoWidth = 120`, `MinMemoLines = 2`,
`MaxMemoLines = 20` — and `MinMemoWidth` is deliberately tied to the XAML value, with a
test asserting the two agree (`RecordEditorLayoutStoreTests.cs:221-226`).

**The dropdown stretches because nothing stops it** (`RecordEditorWindow.xaml:71-75`):
the `ComboBox` sets no `Width`, no `MaxWidth` and no `HorizontalAlignment`, so it fills
the `*` column, and the form `StackPanel` is explicitly bound to at least the viewport
width (`:131-134`). This is shipped, intended behaviour: SPEC-2026-002 listed
"no per-field width for non-memo fields" as a non-goal (`SPEC-2026-002:83`), so changing
it is a deliberate reversal, not a bug fix.

**Memo is set up as a checkbox**, in the Settings field grid beside a Length column
(`SettingsView.xaml:80-101`, the `Memo` column at `:101`), both disabled for non-Text
fields. The Type dropdown offers `Text / Date / Number / List` — the four members of
`FieldType` (`src/FgScanner.Data/Entities.cs:3-9`).

**Why memo cannot be a real type** — `FieldDefinition.ToIndexFieldDef`
(`Entities.cs:158-173`) casts `(IndexFieldType)Type` positionally onto a member-for-member
twin (`src/FgScanner.Core/Index/IndexModels.cs:11-17`), and `ManifestBuilder` writes the
type's **name** into `manifest.json`. ADR-0009:12-18 records the decision and :57-58 adds
"Nobody should later 'tidy' `Memo` into a `FieldType`."

**Three surfaces, not one.** The record editor template, the Groups page panels
(`GroupsView.xaml:111-162` batch, `:163-219` pending — `WrapPanel`, fixed `Width="160"`,
`DataTrigger`-based), and the row grid built in C#
(`src/FgScanner.App/Views/EntryGridColumns.cs:15`, list columns star-sized at `:61-68`,
memo shown as one trimmed line at `:81-84`). `PendingFieldEditor`
(`GroupDetailViewModel.cs:813`) has **no `IsMemo`**, so the Groups panels cannot render a
memo differently even when the field is one.

**Not examined** — the page-preview pane, printing, and the `.fgprofile` import path
beyond how it degrades an out-of-range length.

## 05 · Questions for Franz

> **Answered 2026-09-20, Round A part 1** — [review page](https://claude.ai/artifact/CCZuGgTi2dm7YK4wqDDbdx),
> db doc `review/SPEC-2026-004-005-rA-p1`, verdict **approve**. Every item took option
> (a). No notes.
>
> | | |
> |---|---|
> | Q1 | (a) A **fixed default of about four rows**, full form width. ADR-0009's independence of width and length stands, unamended. |
> | Q2 | (a) **Yes — "Memo" joins the Type dropdown**, storing Text plus the flag. The checkbox column goes. |
> | N1 | (a) Record editor only. |
> | N2 | (a) Groups side panels left as they are; recorded as a follow-up. |

**Blocking**

1. **How should a memo box size itself when nobody has dragged it?** — *why it matters:*
   a fixed default (say 4 rows, full form width) is predictable and one line of XAML.
   Sizing from the field's character limit — a 2000-character `Notes` opening taller than
   a 200-character `Basis` — reads better but contradicts ADR-0009:46-48, where you
   settled that a field's width and its character length are independent. If you want
   length to drive the default height now, that ADR needs amending, which this spec can
   do explicitly.
2. **Does "Memo" join the Type dropdown?** — *why it matters:* you asked for a memo field
   type. It can be presented as a fifth choice that stores Text + the memo flag, so the
   export is untouched and ADR-0009 holds. The cost is that the Type list no longer maps
   one-to-one onto `FieldType`, so anyone reading the code later must be told why — an ADR
   amendment and a comment. Leaving it as the checkbox costs nothing but keeps the setting
   where you did not find it.

**Non-blocking**

1. **How far does the dropdown-width change reach?** — *proceeding as if:* the record
   editor only. The Groups page panels are already a fixed 160px, and the row grid's
   star-sized combo column is a grid layout question rather than a form one. Widening the
   change to all three is more churn for the same complaint.
2. **Should the Groups page panels learn to render a memo as a memo?** — *proceeding as
   if:* out of scope here, noted as a follow-up. `PendingFieldEditor` would need `IsMemo`
   and a third copy of the memo box; the request was about the record editor.

## 06 · Assumptions

| # | Assumption | If wrong |
|---|---|---|
| 1 | A stored (dragged) memo size still wins over the new default | Operators lose sizes they set; AC-3 pins it |
| 2 | WPF sizes a `ComboBox` with `HorizontalAlignment="Left"` to its widest item, including the drop button | The dropdown shrinks to nothing on an empty list; AC-5 covers the empty case |
| 3 | The Settings Type list is bound to an enumeration the view model owns, so a fifth display choice is a view-model change, not an `Entities.cs` change | Q2 becomes a schema-touching change and must be re-costed |
| 4 | No consumer parses the Settings Type text; it is display only | A tool reading the UI list would break — none found |

## 07 · Data model

**No schema change. No migration. No backfill.** `FieldDefinition.MaxLength` and
`FieldDefinition.Memo` already exist (`Entities.cs:149, 156`) with their Text-only rule
applied in `ToIndexFieldDef` (`:167-173`).

If §05 Q2 is yes, the mapping lives **only** in the Settings view model:

| Shown in Type | Stored `FieldType` | Stored `Memo` | Exported type name |
|---|---|---|---|
| Text | `Text` | false | `Text` |
| **Memo** | `Text` | **true** | `Text` |
| Date | `Date` | false | `Date` |
| Number | `Number` | false | `Number` |
| List | `List` | false | `List` |

Documented in code as a doc comment on the mapping in `SettingsViewModel`, pointing at
ADR-0009, and as an amendment to ADR-0009 itself.

## 08 · Architecture and approach

**Memo default size.** Add the default to the XAML (`RecordEditorWindow.xaml:57`) and let
the existing restore path keep overriding it, since `ApplyMemoSize` runs only when a
stored size exists. If the default is expressed in rows, it is converted with the
existing line↔pixel helpers (`RecordEditorWindow.xaml.cs:253-260`) so one definition of a
"line" is used. `MinMemoLines` in `RecordEditorLayoutStore.cs:31` stays at 2 — it is the
floor for a *dragged* size, not the default — and `MinMemoWidth` must continue to agree
with the XAML `MinWidth` (`RecordEditorLayoutStoreTests.cs:221-226`).

**Dropdown width.** `HorizontalAlignment="Left"` on the `ComboBox`
(`RecordEditorWindow.xaml:71`). WPF then measures the widest item. No code change; the
form's `StackPanel` keeps its viewport-width binding so the *column* is unchanged and
every other input still fills the form.

**Memo in the Type list.** A display-level list in `SettingsViewModel` with a two-way
mapping to (`FieldType`, `Memo`). Choosing Memo sets Text + flag; choosing anything else
clears the flag, which is what `ProfileService.Sizing` already enforces on save
(`ProfileService.cs:312-313`). The Memo checkbox column either disappears or becomes a
read-only mirror — §05 Q2's answer decides, and leaving both would give two controls for
one truth.

**Alternatives considered.** *A reusable `MemoBox` control* — SPEC-2026-002 planned one
(`:293`) and it was never built; three surfaces would benefit, but building it now is
larger than the complaint and would touch the Groups panels this spec excludes. Noted as
a follow-up. *Adding `FieldType.Memo`* — rejected, ADR-0009. *Sizing every input to
content* — rejected, ADR-0009:46-48.

**Data access.** Nothing. No query changes.

**System evolution.** Nothing for `~/.claude/skills/`.

## 08a · AI opportunity assessment

_Not applicable — this is layout. A model has nothing useful to contribute to how wide a
combo box is, and introducing one would make a form's appearance non-deterministic._

Worth noting for a future spec: summarising a long memo into the one-line grid cell
(`EntryGridColumns.cs:99-106` currently trims) is a genuine AI opportunity, but on
evidence records a generated summary sitting beside a verbatim value invites someone to
read the summary as the record. Not recommended here.

## 09 · Design system compliance

The app's own WPF Fluent theme governs.

- The memo grip stays always-visible rather than hover-only — "a grip nobody can see is
  not a handle" (`RecordEditorWindow.xaml:63`).
- A left-aligned dropdown keeps its label alignment through the shared `FormLabel`
  size group (`:27`), so the form's left edge does not become ragged.
- Accessibility: `AutomationProperties.LabeledBy` is already set on every input and must
  survive the changes; keyboard reachability is unchanged; Enter still moves to the next
  field from a memo (`RecordEditorWindow.xaml.cs:234-241`).

## 10 · Acceptance criteria

> **AC-1** — Given a memo field with a stored value of 300 characters and no saved size,
> when the record editor opens, the box shows the value on at least four lines without
> the operator resizing anything.
> *Proven by:* `manual` — Franz, on the `Notes` field of a record in the copy of Jim's
> data (a rendered pixel height cannot be asserted headlessly)

> **AC-2** — The default memo size is inside the stored-size clamp, so a dragged size can
> never be smaller than the default is tall.
> *Proven by:* `tests/FgScanner.App.Tests/RecordEditorLayoutStoreTests.cs` → "the default
> memo size satisfies the clamp"

> **AC-3** — Given a memo the operator has dragged to 6 lines, when the editor is
> reopened, it is 6 lines — the default does not overwrite it.
> *Proven by:* same file → existing "memo sizes are kept by field name", extended

> **AC-4** — Given a list field whose longest choice is "Correspondence", the dropdown is
> approximately that wide and not the width of the form pane.
> *Proven by:* `manual` — Franz, on `DocType` in the Evidence profile

> **AC-5** — A list field with no choices, and one with a very long choice, both render
> without clipping the label or forcing a horizontal scrollbar on the form.
> *Proven by:* `manual`, same pass

> **AC-6** — Choosing "Memo" in Settings stores `FieldType.Text` with `Memo = true`;
> choosing "Text" stores `Memo = false`; and a field already stored as Text + memo shows
> as "Memo" when Settings reopens.
> *Proven by:* `tests/FgScanner.App.Tests/FieldRowTests.cs` → "the Memo type choice maps
> onto Text plus the flag"

> **AC-7** — The export is unchanged: a profile containing a memo field produces
> `manifest.json` with `"type": "Text"` for it, byte-identical to the current snapshot.
> *Proven by:* `tests/FgScanner.Core.Tests/IndexExporterTests.cs` → existing Verify
> snapshots, unmodified

> **AC-8** — A memo value still contains no line breaks after editing.
> *Proven by:* `tests/FgScanner.App.Tests/RecordEditorViewModelTests.cs` → existing memo
> cases, extended with a newline-refusal assertion

## 11 · Test strategy

**11.1 — The failing tests to write first**

| Feature | Test file | The failing assertion |
|---|---|---|
| Default memo size is legal | `tests/FgScanner.App.Tests/RecordEditorLayoutStoreTests.cs` | The default, passed through `ClampMemo`, comes back unchanged |
| Stored size still wins | same | A stored 6-line size survives a load once a default exists |
| Memo type mapping | `tests/FgScanner.App.Tests/FieldRowTests.cs` | "Memo" → (`Text`, `Memo = true`); "Date" → (`Date`, `Memo = false`) |
| Export untouched | `tests/FgScanner.Core.Tests/IndexExporterTests.cs` | Verify snapshots match without being re-accepted |
| No newlines | `tests/FgScanner.App.Tests/RecordEditorViewModelTests.cs` | A value written through a memo field contains no `\n` |

**11.2 — Test data.** The existing in-memory fixtures; the Evidence profile seed
(`EvidenceProfileSeedTests.cs:125-133` already asserts `Notes.Memo` survives a repair).
Verify snapshots stay as they are — if one needs re-accepting, that is the export
contract moving and the work is wrong.

**11.3 — Verification suite**

- `dotnet build -c Release`, `dotnet test -c Release` (≥ 692), `dotnet format --verify-no-changes`.
- Manual pass on `D:\Evidence-Scans`: open a record in a group with `Notes` filled; check
  AC-1, AC-4, AC-5 by eye; confirm a dragged size still persists between openings.

## 12 · Edge cases and failure modes

| Case | Expected behaviour | Where handled |
|---|---|---|
| Memo with an empty value | Opens at the default size, not collapsed | XAML default |
| Memo dragged smaller than the default | Honoured down to the 2-line floor | `RecordEditorLayoutStore.cs:105-113` |
| Memo wider than the form pane after the pane is narrowed | Clamped to the pane on restore | `:243-256` (existing) |
| List with zero choices | Renders at a sensible minimum, not zero-width | AC-5 |
| List whose stored value is not among the choices | Value survives, nothing written — existing null filter | `RecordEditorViewModel.cs:333-350` |
| A very long single choice | Dropdown may reach the pane width; the form scrolls horizontally as it already can | `RecordEditorWindow.xaml:129-130` |
| Field switched from Memo to Date in Settings | `Memo` and `MaxLength` are dropped on save | `ProfileService.cs:312-313` |
| An existing profile hand-edited to have `Memo` on a Number field | Ignored at render and at export | `FormField.cs:290-304`; `Entities.cs:167-173` |

## 13 · Security and configuration

_Not applicable — presentation only. No new input surface, no new dependency, no new
configuration, no path handling, no network._

## 14 · Observability

Layout has no runtime failure worth logging, with one exception: a stored memo size that
fails to parse or falls outside the clamp is already coerced silently. This spec adds a
Debug-level log line naming the field when a stored size is clamped, so "my box keeps
resizing itself" is diagnosable from the log rather than by guesswork.

A silent failure here is visible by definition — the operator is looking at it.

## 15 · Performance and scale

Not a performance question: at most 16 fields per schema
(`ProfileService.MaxFields = 16`), one editor window at a time. Revisit only if the field
cap is raised far beyond 16 or if the form gains virtualisation.

## 16 · Regression risk

| # | What could break | Why | Evidence (file:line) | Mitigation |
|---|---|---|---|---|
| R1 | The export contract moves | A `FieldType.Memo` member would shift the positional cast and change the name in `manifest.json`, which the JimsStuff importer parses | `Entities.cs:167-173`; `IndexModels.cs:11-17`; ADR-0009:12-18 | No enum member is added; AC-7 pins the snapshots |
| R2 | `MinMemoWidth` and the XAML `MinWidth` drift apart | A test asserts they agree, and the store would then clamp against a stale number | `RecordEditorLayoutStoreTests.cs:221-226`; `RecordEditorLayoutStore.cs:28-33` | Change both or neither; the test stays |
| R3 | Dragged sizes stop persisting | The default could be applied after the restore instead of before | `RecordEditorWindow.xaml.cs:190-202` | AC-3 pins a stored size beating the default |
| R4 | Two controls for one truth in Settings | If Memo joins the Type list and the checkbox column stays, they can disagree | `SettingsView.xaml:101` | Q2's answer removes one of them |
| R5 | Long list values force a horizontal scrollbar | Left alignment lets a wide item size the control | `RecordEditorWindow.xaml:129-134` | Cap the dropdown at the pane width, as the memo box already is (`xaml.cs:247`) |
| R6 | The Groups panels still show a memo as a 160px one-liner | They are a separate template with no `IsMemo` | `GroupsView.xaml:122`; `GroupDetailViewModel.cs:813` | Out of scope by §05 N2 — stated, not forgotten |

## 17 · Migration and rollback

_Not applicable — no data change._ Rollback is reinstalling the previous installer.
Memo sizes stored by an older version remain readable; the clamp is unchanged.

## 18 · Documentation updates

- **docs/adr/0009-field-length-and-memo.md** — amend, do not replace: record that Memo may
  be *presented* as a type while remaining a flag, and that the default memo height is
  (or is not) derived from the character limit, per §05 Q1.
- **docs/user-guide.md** — the "Length and memo (Text fields)" section gains the new
  default behaviour and, if Q2 is yes, describes Memo as a choice in the Type list.
- **docs/manual-tests.md** — add AC-1, AC-4, AC-5 as manual rows, since they cannot be
  automated.
- **CLAUDE.md** — the evidence-work section already says memo is a flag, not a
  `FieldType`; extend that line to add "even when the Settings screen shows it as one".
- **Memory** — update `fg-scanner-memo-is-a-flag` so a later session does not read a Memo
  entry in the Type dropdown as evidence that the enum gained a member.

## 19 · Phase plan

### Phase 1 — Reading a record
- **Objective** — the operator can read a long memo and scan a form without resizing.
- **Delivers** — the memo default size; the left-aligned dropdown capped at the pane
  width; the tests in §11.1 for both.
- **Not in this phase** — anything in Settings.
- **Done when** — AC-1..AC-5 and AC-8 pass.

### Phase 2 — Setting a field up
- **Objective** — the memo setting is findable.
- **Delivers** — Memo in the Type list, the two-way mapping onto (`Text`, `Memo`), removal
  of the now-duplicate Memo checkbox column, and the ADR-0009 amendment (§05 Q2a).
- **Not in this phase** — the Groups page panels, a shared memo control.
- **Done when** — AC-6 and AC-7 pass.

## 20 · Prompt pack

[SPEC-2026-005-memo-and-field-widths-PROMPTS.md](./SPEC-2026-005-memo-and-field-widths-PROMPTS.md)
— written once this spec is approved (§22).

## 21 · Definition of done

- [ ] All acceptance criteria met
- [ ] Failing tests written first, now passing
- [ ] Full suite green (≥ 692)
- [ ] `/code-review max` run, findings resolved or accepted in writing
- [ ] Security review — _not applicable_
- [ ] Documentation updated per §18
- [ ] Verified by eye on the copy of Jim's data
- [ ] Rollback tested or explicitly waived by Franz

## 22 · Sign-off

| | |
|---|---|
| **Review round answered** | ☑ 2026-09-20 — [Round A, part 1 of 3](https://claude.ai/artifact/CCZuGgTi2dm7YK4wqDDbdx) · db doc `review/SPEC-2026-004-005-rA-p1` |
| **Franz approved** | ☑ 2026-09-20 (verdict `approve`, all items option (a)) |
| **Built** | ☐ date: |
| **Verified in production** | ☐ date: |
