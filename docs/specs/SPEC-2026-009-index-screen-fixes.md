# SPEC-2026-009 — Index screen fixes: zoom, resizable panes, names, people search, OCR view

| | |
|---|---|
| **Status** | Approved |
| **Revision** | B |
| **Tier** | Feature |
| **Author** | Claude, for Franz Gerster |
| **Date** | 2026-10-03 |
| **Project** | FgmakerScanner |
| **Supersedes** | — |
| **Related** | SPEC-2026-008 (index mode), ADR-0014/0015/0016, JimsStuff SPEC-2026-007 |

---

## 01 · Management summary

- **What we are building** — five usability fixes to FG Scanner's Index screen, the screen
  where Jim answers the portal's questions about each scanned document: zoom on the page
  image, panes that can be dragged wider or narrower, real names instead of `P0433`-style
  codes, a people search that shows results in a table instead of a 1,312-name pull-down,
  and a "View OCR" button on the Groups page and the record editor.
- **Why** — Franz's first walk of the Index screen (2026-10-02/03) found it hard to use on
  real documents: small print can't be read, the panes can't be resized, the AI's suggestions
  name people by code only, and finding one person in 1,312 means scrolling a list. Jim will
  spend hours on this screen per batch; every second per document is paid 100 times a batch.
- **What it costs** — 7 prompts across 3 phases, roughly two to three days of build sessions.
  No recurring cost, no AI calls, no change to the files exchanged with the portal.
- **What could go wrong** — (1) the new people search could quietly change *which* person gets
  recorded (a name typed by Jim must still travel as typed, never snap to a look-alike on the
  list); (2) resizing panes could cut off the answer panel on a small screen. Both get tests.
- **What we need from you** — answers to the questions on the review page: how the people
  search looks, what "View OCR" opens, which screens get the splitters, and three smaller calls.

This is the **first of four specs** split out of Franz's 2026-10-03 request. The others —
portal people merge, "index everything" (existing groups, OCR for all, one search), and
importing text messages and email into the index — are listed in §03 as non-goals here.

## 02 · Outcomes

- **Goal** — Jim can answer a document without leaving the Index screen to read the page,
  decode an id, or hunt for a person.
- **Who benefits** — Jim (indexing operator, Yahoo/webmail user, not technical); Franz when
  he walks or reviews a batch.
- **How we will know it worked**
  - Every suggestion and staged answer on screen shows a readable name/label, never a bare id.
  - Finding a named person among 1,312 takes typing a few letters and one click, not scrolling.
  - The page can be read at 200 % without leaving the screen.
  - Pane widths dragged by Jim are still there after a restart.
  - The OCR text of any captured page is one click away on Groups and in the record editor.

## 03 · Scope and non-goals

**In scope**

1. **Zoom on the Index page image** — −, +, Fit, percentage, Ctrl+wheel, and an "Open full
   size" pop-out with page-through; the same tools and the same `ZoomController`/`FitPolicy`
   the Groups preview and record editor already use.
2. **Splitters on the Index screen** — between document list | page image | answer panel; the
   section list on the far left (the main window's nav rail) stays fixed. Widths remembered.
3. **Names instead of ids** — in the Suggestions list and the "Staged answers" list:
   people (`person`, and `payee` while it is shown) by display name, subjects by label, doc
   types by label. The id stays visible beside the name.
4. **People search with a grid** — replaces the editable 1,312-entry pull-down: a search box
   that filters a table (name · id · kind · also known as · roles) as Jim types. The typed-name
   path (ADR-0016) and "no autocomplete" are preserved.
5. **View OCR on Groups and in the record editor** — a button that opens a resizable window
   with the selected page's OCR (shape decided in Q2/Q3).
6. **Two defects found during reconnaissance** (§16 R1, R2): Accept on a key-document
   suggestion is always refused, and suggestions for fields outside the five show an Accept
   button that can only fail.

**Non-goals (each is a later spec or a deliberate no)**

- **No portal-side change and no change to the index-package contract** — nothing in
  `docs/contract-vendored/`, `PackageReader` or `PackageWriter` output changes.
- **No OCR on the Index screen** — the package carries images only (`SeedPage(PageId, Image)`);
  showing OCR there needs a contract change. → the "index everything" spec.
- **No moving existing Groups records into the Index screen, no OCR-everything, no unified
  search** (Franz's items 6–7). → "Index everything" spec (System tier, both repos, waits on
  the portal's phase 5 import).
- **No text-message indexing, no email import** (items 8–9). → their own spec(s); JimsStuff
  SPEC-2026-002 already covers text-message evidence on the portal side.
- **No people merge/edit in the portal** (item 10). → JimsStuff spec, next. The merge engine
  already exists there (`persons.merge_persons`, `/api/people/merge`); only the screen is missing.
- **No editing of the person list from FG Scanner** — the register stays the single source of
  names; FG Scanner only picks from it or sends a typed name as a proposal.
- **No change to the Index flag rollout** — `Feature.IndexMode` stays OFF in published releases
  until the portal can import results (ADR-0014). These fixes ship behind that flag.

## 04 · Current state

**Stack and conventions** — per `docs/specs/_project-brief.md` (WPF Fluent, CommunityToolkit
MVVM, xunit.v3 MTP, headless view-model tests; numeric UI logic lives in WPF-free classes such
as `ZoomController`, `FitPolicy`, `PageNavigator`).

**Files involved**

| Area | File | What is there now |
|---|---|---|
| Index screen | `src/FgScanner.App/Views/IndexView.xaml` | Fixed `Grid` columns `280 / * / 300` (lines 84–88), no splitters. Image is `Stretch="Uniform" StretchDirection="DownOnly"` (119–123): it only ever shrinks — no zoom at all. |
| Suggestions | `IndexView.xaml:138–163` | Binds `SeedSuggestion` directly: shows `Field → Value`, so `person → P0386`, `subject → property`, `doc_type → letter`. |
| Staged answers | `IndexView.xaml:223–242` | Same: `person mentioned P0433`. |
| People picker | `IndexView.xaml:200–205`, `IndexViewModel.cs:186, 360–410` | Editable `ComboBox`, `IsTextSearchEnabled="False"` (ADR-0016), `ItemsSource = Package.People` (1,312 in PKG-0002). Add: a picked entry whose text still equals its `DisplayName` → id; otherwise the typed text is matched against aliases (`NormaliseName`) and only an unmatched name travels as typed. |
| Staging | `src/FgScanner.App/Views/AnswerStaging.cs:65–119` | `Stage()` refuses unknown fields and runs `PackageWriter.AnswerValidator` at entry (`IndexViewModel.cs:943`). |
| Package model | `src/FgScanner.Core/IndexPackages/IndexPackageModels.cs` | `PackagePerson(Id, DisplayName, Kind, Roles, Aliases)`, `PackageSubject(Id, Label, ParentId, Active)`, `PackageDocType(Id, Label, Family, Active)`, `SeedSuggestion(SuggestionId, Field, Qualifier, Value, ReasonQuote)`, `SeedPage(PageId, Image)` — no OCR. |
| Zoom (reuse) | `Views/ZoomController.cs`, `Views/FitPolicy.cs`, `Views/ImageLayout.cs` | Scale 0.05–8.0, step ×1.25, Fit that never divides by an unlaid viewport. Used by `GroupsView.xaml.cs:214–334`, `Dialogs/RecordEditorWindow`, `Dialogs/PageViewerWindow`. |
| Splitters (reuse) | `GroupsView.xaml:302–305, 353–357, 390–394`; `GroupsView.xaml.cs:50–87` | `GridSplitter` + `DragCompleted → SavePanelSizes()`; widths restored from app settings with a fallback and a minimum (`ReadLengthAsync`). |
| Section minimum | `ShellWindow.xaml.cs:226–235` | `["Index"] = (980, 520)` — below that the section scrolls rather than squeezes (SPEC-2026-001). |
| OCR storage | `Data/Entities.cs:262–269`; `Ocr/OcrPipeline.cs:18–87` | `Page.OcrStatus` ∈ {`No`, `Pending`, `Yes`, `Failed`}, `Page.OcrMeanConfidence`, `Page.OcrText` (plain, FTS5-indexed). The pipeline also writes `<imagebase>.md` beside the image (YAML front matter + geometric Markdown). |
| OCR on Groups today | `GroupsView.xaml:418–437` | **An "OCR text" box already exists** — read-only `SelectedRow.OcrText`, bottom of the right column, below the preview + folder panel; on a normal window it gets very little height. There is **no** OCR in `RecordEditorWindow`. |

**Enum and value facts the questions rely on** (PKG-0002, read 2026-10-03):

- Suggestion fields and counts: `person` 704 · `subject` 530 · `doc_type` 112 · `key_flag` 72 ·
  `date` 59 · `expense_category` 21 · `amount` 20 · `payee` 1. The contract's answer fields are
  only the five: `doc_type`, `date`, `person`, `subject`, `key_flag`
  (`IndexAnswerVocabulary.Fields`).
- `key_flag` suggestions carry value **`yes`**; the contract's key-flag answer is **`true`**
  (`IndexAnswerVocabulary.KeyFlagTrue`).
- `payee` values are person ids (`P0893`); `expense_category` and `amount` are free text.
- People: 1,312 entries; kinds seen include `person` and `organisation`.

**Not examined** — the portal's suggestion generator (why it emits `yes` and extra fields);
`PageViewerWindow` internals beyond its constructor; screen-reader output of the WPF
`DataGrid`; behaviour at 150 %+ display scaling (the dev screen is 168 DPI, see the
`wpf-tests-change-gdi-dpi` memory).

## 05 · Questions for Franz

**Answered 2026-10-03 in review round A** ([page](https://claude.ai/artifact/3p8B2jFk6PVUKemnb6CxhH),
doc `SPEC-2026-009-rA`, verdict **approve**). Franz took the recommended option on all ten
items and left no notes; each answer is marked **→** below. Q2's side-question (whether the
existing Groups OCR box went unseen or is too small) was not answered — the viewer is built
either way and the existing box stays.

**Blocking**

1. **Q1 — Where should the people search live?** (a) inline in the answer panel: search box
   with a short results table under it (~8 rows), Add beside it — *recommended*: no extra
   window, keyboard flows top to bottom; costs vertical room in a 300 px panel, which is why
   the panel becomes resizable. **→ (a) inline.** (b) a "Find person…" button opening a dialog with a big
   search + grid — more room and columns; one extra window per person added.
2. **Q2 — What should "View OCR" open?** (a) a window with the page image on the left and its
   OCR on the right, both scrolling — *recommended*: you can check the text against the page;
   (b) text only. Note the Groups page **already** shows OCR text in a small box under the
   preview — was the problem that you didn't see it, or that it's too small? **→ (a) image + OCR side by side.**
3. **Q3 — Which OCR text?** (a) the `.md` file beside the image (keeps headings, tables and
   layout; it is the file the rest of the pipeline uses) with the database's plain text as
   fallback when no `.md` exists — *recommended*; (b) the plain text only. **→ (a) `.md`, plain-text fallback.**
4. **Q4 — Splitters on which screens?** (a) Index screen only (what was asked); (b) Index **and**
   the Groups page's group list (Groups today has a splitter only between grid and preview; its
   270 px group list is fixed) — *recommended*, it's the same one-line pattern and the same
   complaint will come up there. **→ (b) Index + Groups group list.**

**Non-blocking (proceeding on the assumption unless corrected)**

5. **Q5 — How a name shows** — proceeding as `Whitacre, Jason · P0433` (name first, id small
   and grey) so two people with the same name stay distinguishable. **→ confirmed.**
6. **Q6 — What the search matches** — proceeding as: display name **and** every alias,
   anywhere in the text (not just the start), ignoring case and accents; "Whit" finds
   "Whitacre, Jason" and "Jason Whitacre". Results sorted by name. **→ confirmed.**
7. **Q7 — Key-document suggestion** — proceeding as: Accept on a `key_flag` suggestion of `yes`
   stages the contract's `true` (today it is always refused). Anything other than `yes`/`true`
   stays refused. **→ confirmed (fix).**
8. **Q8 — Suggestions for fields the contract does not carry** (`amount`, `expense_category`,
   `payee`; 42 in PKG-0002) — proceeding as: show them greyed with "for information — not
   asked in this batch" and **no** Accept button, rather than hiding them (the reason quote can
   still help Jim read the page). **→ confirmed (greyed).**
9. **Q9 — Pane widths remembered** across restarts (like Groups) — proceeding as yes. **→ confirmed.**
10. **Q10 — Order of the remaining three specs** — proceeding as: portal people merge next
    (small, under a day), then "index everything", then text messages + email. **→ confirmed.**

## 06 · Assumptions

| # | Assumption | If wrong |
|---|---|---|
| A1 | "The section beside it where the groups are" on the Index screen means the batch's document list (the Index screen has no groups). | Splitter placement in §08 shifts; Q4 covers the Groups page. |
| A2 | Package people lists stay in the low thousands (1,312 today). | Above ~20,000 the in-memory filter needs a precomputed index (§15). |
| A3 | Every suggestion's `person`/`payee` value is an id present in `people.json`. | An unknown id shows as the bare id with "not on the people list" — never a crash (§12). |
| A4 | The `.md` sidecar, when present, is the same page's OCR as `Page.OcrText`. | Q3 fallback order changes. |
| A5 | Jim's screen is at least 1280×1024 (SPEC-2026-001's floor). | Minimum pane widths in §08 need lowering. |

## 07 · Data model

**No database or contract change.** Three app-settings keys are added (key–value `Settings`
table via `AppSettingsService`, the same mechanism as Groups' `PreviewWidthKey`):

| Key | Type | Default | Validation |
|---|---|---|---|
| `Index.DocumentListWidth` | double (DIP), invariant culture | 280 | clamp ≥ 200 |
| `Index.AnswerPanelWidth` | double (DIP) | 340 | clamp ≥ 300 |
| `Groups.GroupListWidth` (Q4 = b) | double (DIP) | 270 | clamp ≥ 200 |

A corrupt or missing value falls back to the default (the existing `ReadLengthAsync` rule).
Documented on the constants in the code-behind, as Groups does today.

New view-model shapes (App layer only, never serialised):

- `SuggestionRow(SeedSuggestion Source, string FieldLabel, string ValueLabel, string? IdText,
  bool CanAccept, string? InfoText)` — what the Suggestions list binds instead of the raw record.
- `StagedAnswerRow(StagedAnswer Source, string ValueLabel, string? IdText)` — likewise.
- `PersonSearch` (WPF-free, in `Views/`): `IReadOnlyList<PackagePerson> Filter(string text)`.

## 08 · Architecture and approach

**1 · Labels (`IndexLabels`, WPF-free).** One class built from the open package:
`Person(id)`, `Subject(id)`, `DocType(id)` → label, or `null` if unknown. `IndexViewModel`
projects `SelectedDocument.Suggestions` into `SuggestionRow`s and `StagedAnswers` into
`StagedAnswerRow`s through it. The **stored** values are untouched: Accept still passes the
original `SeedSuggestion` (with the key-flag mapping of Q7) to `TryStage`. This keeps the
exported file byte-identical to today's for the same clicks.

**2 · People search (`PersonSearch`, WPF-free).** Normalise once per package (lower-case,
strip accents with `NormalizationForm.FormD`, collapse spaces) for each display name and alias;
`Filter` returns people whose any normalised string *contains* the normalised query, sorted by
display name, capped at 200 rows with "N more — keep typing". The view (per Q1) is a `TextBox`
plus a read-only `DataGrid`/`ListView`. **Add rules, unchanged in substance:**
- a row selected in the grid → its id;
- no row selected → the typed text goes through today's typed-name path
  (`IndexViewModel.cs:360–410`: alias match →
  id; no match → typed proposal, `TypedNameProblem` refusals in words).
- Typing never selects a row; Enter in the search box with no selection does **not** pick the
  first row. This is the ADR-0016 guarantee carried into the new control.

**3 · Zoom.** Replace the `Image` at `IndexView.xaml:119–123` with the Groups pattern:
`ScrollViewer` + `Image Stretch="None"` + `LayoutTransform` `ScaleTransform`, a `ZoomController`
+ `FitPolicy` in `IndexView.xaml.cs`, tools row (− + Fit % "Open full size"). Fit on page change
and on viewport resize until the user zooms (the `FitPolicy` rule). "Open full size" reuses
`PageViewerWindow` with the document's page images and current page index.

**4 · Splitters.** `IndexView` grid becomes `DocList(280, Min 200) | 6 | Image(*, Min 300) | 6 |
Answers(340, Min 300)`; `GridSplitter ResizeBehavior="PreviousAndNext"`, `DragCompleted` saves,
`Loaded` restores. The Index section minimum in `ShellWindow.xaml.cs` is recomputed from the
mins (200+6+300+6+300 = 812 → keep 980 so defaults fit). Nav rail untouched.

**5 · View OCR.** New `Dialogs/OcrViewerWindow` (per Q2): image pane reusing the zoom pattern,
text pane read-only monospace `TextBox` (selectable, copyable), Previous/Next page. Source per
Q3: `.md` beside the image → front matter stripped → shown as text; else `Page.OcrText`; else a
sentence by status (`No`: "Not OCRed yet — use OCR pages on the Groups page."; `Pending`:
"OCR is still running for this page."; `Failed`: "OCR failed on this page — try Re-process.").
Opened from a "View OCR" button in Groups' preview tools and in the record editor's zoom tools,
for the selected page. The existing Groups "OCR text" box stays.

**Alternatives considered** — (1) an `AutoCompleteBox`-style picker: rejected, autocomplete is
exactly what ADR-0016 forbids. (2) Rendering the `.md` as formatted Markdown: rejected for now —
no Markdown renderer in the WPF app, and the raw text is what search and the portal see.
(3) Hiding non-contract suggestions: rejected in favour of Q8's read-only display.

**Data access** — not a database question: everything is in memory from the open package
(≤ 1,312 people, 100 documents). **System evolution** — nothing here is a reusable skill.

## 08a · AI opportunity assessment

| | |
|---|---|
| **What it would do** | Rank the people grid by likelihood for *this* page (names found in its OCR first). |
| **Why AI beats deterministic code here** | It doesn't, yet: the package carries no OCR, and the AI's own `person` suggestions are already shown above the picker. |
| **Recommendation** | **Not worth it in this spec.** Revisit in "index everything", when OCR travels with the package — a plain "names that appear in this page's text" filter would then do most of the job without a model. |

No AI is used, so no learning loop is needed.

## 09 · Design system compliance

The app's own WPF Fluent theme applies (project brief); the FG Maker web kits do not. Reuse the
existing Groups/record-editor controls and spacing: `GridSplitter` width 6, tool buttons
`Padding="10,2"`, zoom text opacity 0.7. Ids shown in `Opacity="0.7"` small text.

**Accessibility** — every new control gets `AutomationProperties.Name`; splitters are reachable
by keyboard (Tab to splitter, arrow keys resize — `GridSplitter` default, `KeyboardIncrement`
set to 20); the search box → grid → role → Add order is the tab order; Enter on a grid row
= select; Space toggles nothing. Focus visible on all (Fluent default). Ctrl+wheel only — a bare
wheel keeps scrolling. No new keyboard shortcut is bound on the main window (`ShortcutRouter`
untouched).

## 10 · Acceptance criteria

> **AC-1 (names)** — Given PKG-0002 open, a `person` suggestion `P0386` shows the person's
> display name with `P0386` beside it; subjects and doc types show their labels; staged answers
> likewise. *Proven by:* `IndexLabelsTests` + `IndexViewModelTests` → "suggestion rows show labels".

> **AC-2 (unknown id)** — A suggestion whose id is not in `people.json` shows the bare id and
> "not on the people list"; nothing throws. *Proven by:* `IndexLabelsTests` → "unknown id".

> **AC-3 (search)** — Typing `whit` lists both "Whitacre, Jason" entries (P0053, P0433) and any
> alias containing "whit"; `jason whitacre` finds P0433 via its alias; `é`/`e` match.
> *Proven by:* `PersonSearchTests`.

> **AC-4 (no autocomplete)** — Typing in the search box never selects a row and never changes
> the typed text; Add with no row selected sends the typed text through the typed-name path
> (`Zelda Walkthrough` staged as typed; `P1234` refused in words). *Proven by:*
> `IndexTypedNameAndUndatedTests` (extended) + manual on the real window (headless blind spot).

> **AC-5 (pick)** — Selecting the second "Whitacre, Jason" row and Add stages `P0433`, not
> `P0053`. *Proven by:* `IndexViewModelTests` → "grid pick stages the selected id".

> **AC-6 (key flag)** — Accept on a `key_flag` suggestion `yes` stages `true` and exports;
> any other value is refused in words. *Proven by:* `IndexReviewFixTests` → "key flag yes".

> **AC-7 (non-contract fields)** — `amount`/`expense_category`/`payee` suggestions render with
> `CanAccept = false` and the info text; no Accept button. *Proven by:* `IndexViewModelTests`.

> **AC-8 (zoom)** — +, −, Fit and Ctrl+wheel change the Index image scale within 0.05–8.0; a
> new page re-fits until the user zooms. *Proven by:* existing `ZoomController`/`FitPolicy` tests
> + manual on the real window.

> **AC-9 (splitters)** — Dragging each Index splitter resizes the panes, no pane goes below its
> minimum, and the widths are back after a restart; a corrupt stored width falls back to the
> default. *Proven by:* `PanelSizeTests` (the read/clamp helper, extracted) + manual.

> **AC-10 (View OCR)** — On Groups and in the record editor, View OCR opens the selected page's
> OCR: `.md` text without front matter when present, else `OcrText`, else the status sentence.
> *Proven by:* `OcrTextSourceTests` (WPF-free resolver) + manual.

> **AC-11 (no contract drift)** — For the same staged answers, `PKG-…-results.json` is
> byte-identical to the pre-change writer's (decidedAt fixed by the test clock). *Proven by:*
> existing `IndexExportTests` Verify snapshots, unchanged.

## 11 · Test strategy

**11.1 — Failing tests first**

| Feature | Test file | Failing assertion |
|---|---|---|
| Labels | `tests/FgScanner.App.Tests/IndexLabelsTests.cs` (new) | `Person("P0433")` returns the display name; unknown → null |
| Suggestion rows | `IndexViewModelTests.cs` | `SuggestionRows[0].ValueLabel` is a name, not `P…` |
| Search | `PersonSearchTests.cs` (new) | `Filter("whit")` contains P0053 and P0433; accent-insensitive; cap 200 |
| Grid pick | `IndexViewModelTests.cs` | picking the 2nd Whitacre stages P0433 |
| Typed path | `IndexTypedNameAndUndatedTests.cs` | Add with no selection stages typed text unchanged |
| Key flag | `IndexReviewFixTests.cs` | Accept `key_flag=yes` stages `true` (fails today with a refusal) |
| Non-contract | `IndexViewModelTests.cs` | `amount` row has `CanAccept == false` |
| Panel sizes | `PanelSizeTests.cs` (new) | `"abc"` → default; `50` → clamped to min |
| OCR source | `OcrTextSourceTests.cs` (new) | `.md` with front matter → body only; missing `.md` → `OcrText`; status sentences |

**11.2 — Test data** — a small hand-built `IndexPackage` fixture (two Whitacres with the real
ids, an accented alias, one unknown id, one of each extra field); temp-folder `.md` files with
real `OcrPipeline` front matter. No PKG-0002 content is committed (case material).

**11.3 — Verification** — `dotnet build -c Release` (warnings are errors), `dotnet test -c
Release`, `dotnet format --verify-no-changes`, then a manual pass on the real window with
PKG-0002 per `docs/manual-tests.md` (new section), because splitters, zoom, the grid's keyboard
behaviour and selection write-back are invisible to headless tests.

## 12 · Edge cases and failure modes

| Case | Expected | Where |
|---|---|---|
| Empty search text | Grid shows nothing ("Type part of a name"), not all 1,312 | `PersonSearch` / view |
| Query matches > 200 | First 200 by name + "N more — keep typing" | `PersonSearch` |
| Two people with the same display name | Both rows, distinguished by id, kind, aliases | grid columns |
| Grid selection then Jim edits the search text | Selection cleared — the typed text wins (prevents a stale pick) | view model |
| Person id in suggestion missing from list | Bare id + "not on the people list"; Accept still allowed (the validator decides, words shown) | `IndexLabels`, staging |
| Package closed/reopened | Labels and search rebuilt from the new package; never a stale list | `IndexViewModel` package setter |
| Image file missing | Image pane shows "Page image not found"; zoom disabled | view |
| `.md` unreadable / locked | Fall back to `OcrText`, log a warning | `OcrTextSource` |
| Stored pane width wider than the window | Clamped so the image pane keeps its minimum | restore helper |
| Very small window | Section scrolls (980×520 minimum), panes never below mins | `ShellWindow` |
| ObservableCollection bound to a `SelectedItem` (grid results) | Updated by add/remove diff, **never** `Clear()` (CLAUDE.md rule) | view model |

## 13 · Security and configuration

Desktop, no network, no new secrets, no new environment variable, no new dependency. OCR text
is case material: the viewer never writes, copies to temp, or logs it. Settings keys in §07
hold only numbers.

## 14 · Observability

- Logged (Serilog, Information): none per click. Warning: unreadable `.md` (path only, never
  content), unknown person id in a suggestion (id + package id).
- User-visible: every refusal stays in words in `AnswerError` (unchanged mechanism).
- Silent-failure risk: a label lookup that silently showed the wrong name. Mitigated by always
  showing the id beside the name (Q5) so a mismatch is visible to Jim.

## 15 · Performance and scale

1,312 people × ~2 aliases → ~4,000 strings normalised once per package open (< 50 ms);
per-keystroke contains-scan over 4,000 short strings is sub-millisecond. 100 documents,
≤ ~20 suggestions each. Not a performance question; revisit if a package's people list passes
~20,000 (then precompute a trigram index).

## 16 · Regression risk

| # | What could break | Why | Evidence | Mitigation |
|---|---|---|---|---|
| R1 | **(found, existing defect)** Accepting any key-document suggestion fails | Suggestions carry `yes`; validator demands `true` at staging | PKG-0002 `key_flag` values; `PackageWriter.cs:286`; `AnswerStaging.cs:98` | Q7 mapping + AC-6 |
| R2 | **(found, existing defect)** Accept on `amount`/`expense_category`/`payee` shows `"amount" is not an index answer field this contract carries` | Seed carries 8 fields, contract 5 | `AnswerStaging.cs:72–76` | Q8 + AC-7 |
| R3 | Typed names silently become list people | A new search control with "pick first on Enter" or autocomplete | ADR-0016; `IndexView.xaml:197–205` comment | §08-2 rules, AC-4, manual check |
| R4 | Wrong person exported after a grid pick | Selection kept after the search text changes | `IndexViewModel.cs:360–375` compares text to `DisplayName` | Clear selection on edit; AC-5 |
| R5 | Navigation crash / rebuilt document | Clearing a collection bound to `SelectedItem` | CLAUDE.md hard rule; ADR-0010 | Diff updates; manual pass |
| R6 | Export changes shape | Rows projected for display leak into staging | `AcceptSuggestion` → `TryStage` | Rows keep `Source`; AC-11 snapshots |
| R7 | Answer panel cut off on 1280-wide screens | Wider default panel + splitters | `ShellWindow.xaml.cs:226–235` | Mins sum 812 < 980; manual at 1280×1024 |
| R8 | Groups preview sizes reset | Touching `GroupsView` restore code for Q4 | `GroupsView.xaml.cs:57–87` | Extract, don't rewrite; `PanelSizeTests` |
| R9 | Zoom tests flake on DPI | WPF tests make the process DPI-aware mid-suite | memory `wpf-tests-change-gdi-dpi` | WPF-free zoom/fit logic only in tests |

## 17 · Migration and rollback

No schema or contract change. Forward: install the new build. Backward: reinstall the previous
installer; the three settings keys are ignored by older builds. No point of no return.

## 18 · Documentation updates

- **CLAUDE.md** — one line under the Index section: the people search never selects on typing
  or Enter; a grid row is the only way to pick, and no selection means "send as typed" (ADR-0016).
- **docs/FEATURE-PARITY.md** — Index screen zoom, splitters, label display, people search; View OCR.
- **docs/manual-tests.md** — new "Index screen (SPEC-2026-009)" walk-through.
- **ADR** — none new; ADR-0016 gains a "carried into the search grid" note.
- **Memory** — none expected beyond the spec-status line.

## 19 · Phase plan

### Phase 1 — Read the page and the names
- **Objective** — Jim can read the page and every suggestion.
- **Delivers** — zoom + Open full size on the Index image; `IndexLabels`; suggestion and staged
  rows with names; key-flag mapping; non-contract suggestions read-only.
- **Not in this phase** — splitters, people search, OCR viewer.
- **Done when** — AC-1, 2, 6, 7, 8, 11.

### Phase 2 — Room and the people search
- **Objective** — Jim can size the panes and find a person by typing.
- **Delivers** — Index splitters (+ Groups list per Q4) with remembered widths; `PersonSearch`
  and the grid picker per Q1.
- **Not in this phase** — any change to how a typed name is resolved or exported.
- **Done when** — AC-3, 4, 5, 9.

### Phase 3 — View OCR
- **Objective** — OCR for any captured page is one click away.
- **Delivers** — `OcrTextSource`, `OcrViewerWindow`, buttons on Groups and the record editor.
- **Not in this phase** — OCR on the Index screen; editing OCR text.
- **Done when** — AC-10; manual walk; docs per §18.

## 20 · Prompt pack

[SPEC-2026-009-index-screen-fixes-PROMPTS.md](./SPEC-2026-009-index-screen-fixes-PROMPTS.md). Seven prompts: P1 zoom ·
P2 labels + key flag + read-only extras · P3 splitters · P4 people search · P5 OCR viewer ·
P6 `/code-review max` · P7 manual walk + docs. (No security prompt: not web-facing.)

## 21 · Definition of done

- [ ] All acceptance criteria met
- [ ] Failing tests written first, now passing
- [ ] Full suite green (`dotnet test -c Release`), format gate clean
- [ ] `/code-review max` run, findings resolved or accepted in writing
- [ ] Documentation updated per §18
- [ ] Manual walk on the real window with PKG-0002, by Franz
- [ ] Rollback waived (no schema change) — Franz to confirm

## 22 · Sign-off

| | |
|---|---|
| **Review round answered** | ☑ 2026-10-03 — round A, [review page](https://claude.ai/artifact/3p8B2jFk6PVUKemnb6CxhH) |
| **Franz approved** | ☑ 2026-10-03 (round A verdict: approve) |
| **Built** | ☐ date: |
| **Verified in production** | ☐ date: |
