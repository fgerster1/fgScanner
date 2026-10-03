# SPEC-2026-009 — Prompt pack

Prompts for [SPEC-2026-009-index-screen-fixes.md](./SPEC-2026-009-index-screen-fixes.md)
(Approved 2026-10-03, Rev B). Seven prompts, three phases (§19). Work through them in order;
each stops at a checkpoint — paste the results back before starting the next.

Standing rules for every prompt:
- Failing tests FIRST (§11.1), then the smallest diff that makes them pass.
- Branch `phase-32-index-screen` off `main` (create it if missing; if it exists, continue on it).
- **No contract drift:** nothing under `docs/contract-vendored/`, `src/FgScanner.Core/IndexPackages/`
  `PackageReader`/`PackageWriter` output, or the `IndexExportTests` / `ContractGoldenTests`
  snapshots changes. If a test wants regoldening, the change is wrong — stop and report.
- Never `Clear()` an `ObservableCollection` bound to a `SelectedItem` (CLAUDE.md).
- Close FG Scanner before a Release build (MSB3027 file locks).

---

PROMPT 1 of 7 — Zoom on the Index page image (Phase 1)
Spec: SPEC-2026-009 §03.1, §08-3, AC-8    Depends on: —

Read §04 (Zoom row) and §08-3 before starting. Record the baseline: `dotnet test -c Release`
pass count before any change.

Do only this:
1. In `IndexView.xaml`, replace the `Image` at the page-viewer `Border` (Stretch Uniform /
   DownOnly) with the Groups preview pattern: `ScrollViewer` (Auto scrollbars, Ctrl+wheel
   handler) + `Image Stretch="None"` + `LayoutTransform` `ScaleTransform`.
2. Add a tools row beside the existing page navigation: − · + · Fit · zoom % · "Open full size".
   `AutomationProperties.Name` on each.
3. In `IndexView.xaml.cs`: a `ZoomController` + `FitPolicy` exactly as `GroupsView.xaml.cs`
   uses them (re-fit on page change and on viewport change via `ScrollChanged`, until the user
   zooms). "Open full size" opens `PageViewerWindow` with the selected document's page images
   at the current page.
4. If any arithmetic is added beyond what `ZoomController`/`FitPolicy` already do, it goes in
   a WPF-free class with a failing test first. Prefer adding none.

Do NOT in this prompt: splitters, column widths, labels, the people picker, OCR anything.

CHECKPOINT 1 — paste back:
  · dotnet build -c Release → 0 warnings, 0 errors
  · dotnet test -c Release → all green, count ≥ baseline (state both numbers)
  · dotnet format --verify-no-changes → clean
  · git diff --stat → only IndexView.xaml / IndexView.xaml.cs (+ a test file if step 4 applied)
  · manual (Franz, real window, PKG-0002): +/−/Fit/Ctrl+wheel work; next page re-fits; Open full size pages through

---

PROMPT 2 of 7 — Names instead of ids; key-flag and extra-field fixes (Phase 1)
Spec: SPEC-2026-009 §03.3, §03.6, §07, §08-1, Q5/Q7/Q8, AC-1, 2, 6, 7, 11    Depends on: Prompt 1 (green)

Read §04 (value facts), §07 and §08-1 before starting.

Do only this:
1. Failing tests first:
   - `tests/FgScanner.App.Tests/IndexLabelsTests.cs` (new): person id → display name; subject
     id → label; doc-type id → label; unknown → null. Fixture: hand-built package with two
     "Whitacre, Jason" (P0053, P0433), one accented alias, one unknown id.
   - `IndexViewModelTests`: suggestion rows show labels with the id beside; staged rows likewise;
     `amount`/`expense_category`/`payee` rows have `CanAccept == false` and the info text
     "for information — not asked in this batch"; an unknown person id row shows the bare id +
     "not on the people list" and does not throw.
   - `IndexReviewFixTests`: Accept on `key_flag` `yes` stages `true`; `no`/other stays refused.
2. Implement `IndexLabels` (WPF-free, `src/FgScanner.App/Views/`), `SuggestionRow`,
   `StagedAnswerRow` per §07. Rows keep their `Source`; Accept passes the original
   `SeedSuggestion` (key-flag `yes` → `true` mapping only) to `TryStage`.
3. Bind `IndexView.xaml`'s Suggestions and Staged-answers lists to the rows. Display:
   **name** then `· P0433` at `Opacity="0.7"`. Non-contract rows: greyed, info text, no Accept
   button. Payee shows the person's name too.
4. Rebuild labels whenever the package changes (open, reopen, remove).

Do NOT in this prompt: the people picker (Prompt 4), splitters (Prompt 3), any change to
`AnswerStaging` validation other than nothing — the key-flag mapping lives in the view model's
Accept path, not in staging or the writer.

CHECKPOINT 2 — paste back:
  · dotnet test -c Release → green; new tests listed by name
  · dotnet test -c Release --filter "FullyQualifiedName~IndexExport|FullyQualifiedName~ContractGolden" → green, no snapshot files changed (git status shows none)
  · dotnet format --verify-no-changes → clean
  · manual (Franz, PKG-0002): suggestions read "Whitacre, Jason · P0433", subjects/doc types by label; Accept on a key-document suggestion ticks Key document

---

PROMPT 3 of 7 — Resizable panes, remembered (Phase 2)
Spec: SPEC-2026-009 §03.2, §07, §08-4, Q4/Q9, AC-9    Depends on: Prompt 2 (green)

Read §07 (settings keys) and §08-4 before starting.

Do only this:
1. Failing test first: `tests/FgScanner.App.Tests/PanelSizeTests.cs` (new) over a WPF-free
   helper extracted from `GroupsView.xaml.cs` `ReadLengthAsync`'s parse/clamp rule:
   `"abc"` → default; `"50"` → minimum; invariant-culture `"312.5"` → 312.5; missing → default.
2. Extract that helper (no behaviour change for Groups' preview sizes) and use it everywhere below.
3. `IndexView.xaml`: columns `DocList(280, Min 200) | 6 | Image(*, Min 300) | 6 |
   Answers(340, Min 300)`, two `GridSplitter`s (`ResizeBehavior="PreviousAndNext"`,
   `KeyboardIncrement="20"`, tooltip "Drag to resize"). Save `Index.DocumentListWidth` /
   `Index.AnswerPanelWidth` on `DragCompleted` and `Unloaded`; restore on `Loaded`, clamped so
   the image keeps its minimum.
4. `GroupsView.xaml`: make the 270 group-list column resizable the same way
   (`Groups.GroupListWidth`, Min 200). Leave the existing grid|preview splitter untouched.
5. Leave the section minimum `["Index"] = (980, 520)` as is (mins sum 812); nav rail untouched.

Do NOT in this prompt: the people picker, OCR, any change to the Groups preview row/column logic
beyond the extraction.

CHECKPOINT 3 — paste back:
  · dotnet test -c Release → green; PanelSizeTests listed
  · dotnet format --verify-no-changes → clean
  · manual (Franz): drag each Index splitter and the Groups list splitter; restart the app → widths kept; Groups preview size also still kept; at 1280×1024 the answer panel is not cut off

---

PROMPT 4 of 7 — People search with a results grid (Phase 2)
Spec: SPEC-2026-009 §03.4, §08-2, §12, Q1/Q6, AC-3, 4, 5    Depends on: Prompt 3 (green)

Read §08-2 and §16 R3–R5 before starting. ADR-0016 is the law here.

Do only this:
1. Failing tests first:
   - `PersonSearchTests.cs` (new): `Filter("whit")` contains P0053 and P0433 and alias hits;
     `Filter("jason whitacre")` finds P0433 by alias; accent- and case-insensitive; empty text →
     empty; > 200 hits → 200 sorted by display name + the remaining count.
   - `IndexViewModelTests`: selecting the 2nd Whitacre row + Add stages `P0433`; editing the
     search text after selecting clears the selection; Add with no selection stages the typed
     text through today's typed-name path unchanged.
   - extend `IndexTypedNameAndUndatedTests`: "Zelda Walkthrough" staged as typed; "?" and
     "P1234" refused in words — same assertions as before, through the new control's VM surface.
2. Implement `PersonSearch` (WPF-free, normalise once per package). Results collection updated by
   add/remove diff — never `Clear()`.
3. Replace the editable `ComboBox` in the People row with: search `TextBox`
   ("Type part of a name"), a read-only results grid (~8 rows tall; columns Name · Id · Kind ·
   Also known as · Roles), then the existing role `ComboBox` and Add. Typing never selects a row;
   Enter in the search box with no selection does not pick the first row. Keep the hint text.
4. Tab order: search → grid → role → Add. `AutomationProperties.Name` on all.

Do NOT in this prompt: any change to `TypedNameProblem`, alias normalisation used for the typed
path, staging, or the writer. No autocomplete of any kind.

CHECKPOINT 4 — paste back:
  · dotnet test -c Release → green; new/extended tests listed
  · dotnet format --verify-no-changes → clean
  · manual (Franz, PKG-0002, real window — headless tests cannot see this): type "Whit" slowly → text never changes, no row selected; pick 2nd Whitacre → P0433 staged; type "Zelda Walkthrough", Add → staged as typed; switch documents back and forth → no crash, nothing re-staged

---

PROMPT 5 of 7 — View OCR on Groups and the record editor (Phase 3)
Spec: SPEC-2026-009 §03.5, §08-5, Q2/Q3, AC-10    Depends on: Prompt 4 (green)

Read §04 (OCR rows) and §08-5 before starting.

Do only this:
1. Failing test first: `OcrTextSourceTests.cs` (new) for a WPF-free resolver:
   `.md` with `OcrPipeline` front matter → body only; no `.md` → `Page.OcrText`; neither →
   the status sentence for `No` / `Pending` / `Failed` (exact wording in §08-5); unreadable
   `.md` → falls back to `OcrText`. Use temp folders; real front matter shape from `OcrPipeline`.
2. Implement the resolver and `Dialogs/OcrViewerWindow`: page image left (zoom pattern from
   Prompt 1), OCR text right (read-only, selectable, monospace, scrolls), Previous/Next page,
   resizable with a splitter between the two.
3. Add a "View OCR" button to the Groups preview tools and to the record editor's zoom tools, for
   the selected page. The existing Groups "OCR text" box stays.
4. Never write, temp-copy or log OCR content; a warning log names the path only.

Do NOT in this prompt: OCR on the Index screen; editing OCR; re-running OCR from the viewer.

CHECKPOINT 5 — paste back:
  · dotnet test -c Release → green; OcrTextSourceTests listed
  · dotnet format --verify-no-changes → clean
  · manual (Franz): Groups → a page with OCR → View OCR shows image + text; a page never OCRed shows the sentence; same from the record editor

---

PROMPT 6 of 7 — Code review  [START A NEW SESSION FOR THIS]
Spec: SPEC-2026-009    Depends on: Prompt 5 (green)

Run `/code-review max` over the changes made in Prompts 1–5 (`git diff main...phase-32-index-screen`).

Then check compliance with the spec, which is the primary criterion: does the code do what
SPEC-2026-009 §10 says, and only that? Check specifically §16 R3–R6 (typed names, stale
selection, collection clearing, export drift). Report any drift — it is either a bug in the code
or a spec that needs revising, and both need saying out loud.

Resolve every correctness finding. For anything you decline to change, write one line in the
spec's §22 saying what and why. (No security review prompt: not web-facing, no new surface — §13.)

CHECKPOINT 6 — paste back:
  · the findings list with outcome per finding
  · dotnet test -c Release → green after fixes
  · dotnet build -c Release → 0 warnings

---

PROMPT 7 of 7 — Manual walk and documentation (Phase 3)
Spec: SPEC-2026-009 §18, §21    Depends on: Prompt 6 (green)

Do only this:
1. `docs/manual-tests.md`: add "Index screen (SPEC-2026-009)" — numbered steps covering
   checkpoints 1–5's manual lines.
2. `docs/FEATURE-PARITY.md`: Index zoom, splitters, label display, people search; View OCR.
3. `CLAUDE.md`, Index section, one line: the people search never selects on typing or Enter; a
   grid row is the only way to pick, and no selection means "send as typed" (ADR-0016).
4. `docs/adr/0016-*.md`: a short "carried into the search grid (SPEC-2026-009)" note.
5. Spec: Status → Built, §22 Built date. Merge to `main` only after Franz walks the manual
   section on the real window and says so.

Do NOT in this prompt: code changes (a defect found in the walk is a new small prompt);
publishing a release; turning on `Feature.IndexMode` in any release.

CHECKPOINT 7 — paste back:
  · git diff --stat → docs only
  · dotnet test -c Release → green (unchanged count from checkpoint 6)
  · Franz's walk result, line by line
