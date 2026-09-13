# SPEC-2026-001 — Prompt pack

Prompts for [SPEC-2026-001-fit-and-scrolling.md](./SPEC-2026-001-fit-and-scrolling.md) (Approved
2026-09-13, Revision B). Work through them in order. Each stops at a checkpoint; paste the results
back before starting the next.

**Before any prompt:** close FG Scanner. A copy running from `src\FgScanner.App\bin\Release` locks
its DLLs and every Release build fails with `MSB3027`.

**Gate:** Prompts 1 and 2 can run now. **Prompt 3 waits** until the station's screen resolution and
scale are written into the spec's §05 Q2 (steps in §11.2).

| # | Prompt | Phase | Needs |
|---|---|---|---|
| 1 | Fit is right | 1 | — |
| 2 | Scroll host and window sizing | 2 | Prompt 1 green |
| 3 | Sections scroll | 2 | Prompt 2 green **and** station screen recorded |
| 4 | Dialogs and the page viewer | 2 | Prompt 3 green |
| 5 | Code review — **new session** | — | Prompt 4 green |
| 6 | Docs, manual tests, finish | — | Prompt 5 resolved |

---

```
PROMPT 1 of 6 — Fit is right
Spec: SPEC-2026-001 §04, §05 (Q1, N1–N3), §08 Part 1, §10 AC-1..AC-5, §11.1, §16 R3–R4
Phase: 1    Depends on: nothing

Read SPEC-2026-001 §04, §08 Part 1, §10 and §16 before starting. Read CLAUDE.md.

Setup (safe to re-run):
- `git status` and `git log --oneline -3`. Other Claude sessions edit this repo; if main has
  moved or there are unexpected code changes, stop and report.
- Uncommitted files under docs/ (specs, research, STATUS) are expected. Leave them uncommitted
  and do not stage them in this prompt.
- Create or switch to branch `phase-20-fit-and-scrolling`. If it exists and already contains
  this prompt's commit, report that and stop.

Do only this:
1. Write the failing tests FIRST in tests/FgScanner.App.Tests/PageViewerTests.cs:
   - "Fit uses the layout size so a tall page fills the height": Fit(816, 1056, 950, 700,
     maxScale: 3.125) → Scale == 700/1056 (6 dp).
   - "Fit stops where image pixels would be magnified": Fit(200, 200, 1000, 1000, maxScale: 3.125)
     → 3.125; with maxScale 1 → 1.0.
   - FitPolicy tests: a resize re-fits while not user-zoomed; after In() or Out() it does not;
     Fit() and a page change clear the user-zoomed state.
   Replace `Fitting_never_enlarges_a_page_that_already_fits` with the magnification-cap test, and
   put a one-line comment on why (the old cap was in pixel units, the wrong unit). Keep every
   other existing ZoomController and PageNavigator test.
   Run the tests and confirm the new ones fail for the right reason.
2. ZoomController.Fit gains a `maxScale` parameter:
   scale = clamp(min(vw/cw, vh/ch), Minimum, min(maxScale, Maximum)). Keep the unusable-viewport
   guard. Fix the XML comments: Reset() is actual paper size; Fit may enlarge up to one image
   pixel per screen pixel.
3. Add `FitPolicy` beside ZoomController (WPF-free).
4. GroupsView.xaml.cs FitPreview and PageViewerWindow.xaml.cs FitToViewport pass
   `image.Width, image.Height` (layout units) and `maxScale = image.PixelWidth / image.Width`.
   Keep the viewer's `_fitOnNextLayout` deferral (R4). The preview gets the same deferral, and
   re-fits on PreviewScroller SizeChanged while FitPolicy allows it. Both use FitPolicy.
5. Add a zoom % TextBlock to the preview's −/+/Fit button row in GroupsView.xaml, styled like
   PageViewerWindow.xaml:20, updated in ApplyPreviewZoom.
6. Commit: "Fit the page by its layout size, not its pixel count".

Do NOT in this prompt: add any ScrollViewer, SectionScrollHost, WindowSizing, window MinWidth or
MinHeight, splitter clamps, or dialog changes (those are Prompts 2–4). Do not change image decode
sizes or PreviewImageConverter.

Stop when the checkpoint below is satisfied and report the results.
```

```
CHECKPOINT 1 — paste back:
  · dotnet build -c Release → "0 Warning(s)" "0 Error(s)"
  · dotnet test -c Release → Passed, total ≥ 516 plus the new tests, 0 failed
  · dotnet format --verify-no-changes → exit 0
  · git diff --stat main → only ZoomController.cs (+ FitPolicy), GroupsView.xaml(.cs),
    PageViewerWindow.xaml.cs, PageViewerTests.cs
  · Manual (Franz): open a 300-DPI group. The preview shows the whole page and the zoom label
    reads well above 21%. Double-click: the viewer shows the whole page. Press 100%: the page
    appears at paper size.
```

---

```
PROMPT 2 of 6 — Scroll host and window sizing
Spec: SPEC-2026-001 §08 Part 2, §10 AC-6..AC-7, §11.1, §12, §16 R5, R11
Phase: 2    Depends on: Prompt 1 (green)

Read SPEC-2026-001 §08 Part 2, §12 and §16 before starting.

Setup: on branch `phase-20-fit-and-scrolling`, Prompt 1's commit present, tree otherwise clean
apart from docs/. If this prompt's commit already exists, report and stop.

Do only this:
1. Write the failing tests FIRST in tests/FgScanner.App.Tests/WindowSizingTests.cs:
   - FitToWorkArea returns the request unchanged and centred when it fits.
   - 1200×760 in a 1093×614 work area → no larger than the work area, centred inside it.
   - A work area offset from (0,0) (second monitor) → result stays inside it.
   - The splitter clamp: a saved preview width of 900 in 1000 available leaves the grid its 240
     plus the 6 splitter (result ≤ 754), and never goes below the 200 minimum.
2. Add `WindowSizing` (WPF-free; src/FgScanner.App/Views/WindowSizing.cs) with FitToWorkArea and
   the splitter clamp.
3. Add `SectionScrollHost` (src/FgScanner.App/Views/Controls/SectionScrollHost.cs):
   - A ContentControl whose template is a ScrollViewer with both bars Auto.
   - MinContentWidth and MinContentHeight dependency properties. Height may be unbounded
     (double.NaN), for Settings.
   - Content is sized from the host's own ActualWidth/ActualHeight, not the ScrollViewer's
     Viewport*, so a scroll bar appearing never changes the size input (R11).
   - Content never goes below the minimums.
   - Comment why: star rows and DataGrid virtualization need a finite size; a plain ScrollViewer
     gives infinite size.
4. ShellWindow: MinWidth="800" MinHeight="560". On SourceInitialized, apply
   WindowSizing.FitToWorkArea to the 1200×760 request, using the work area of the monitor the
   window opens on.
5. GroupsView.xaml.cs splitter restore uses the new clamp against the space actually available
   at load (R5).
6. Commit: "Add a scroll host that only scrolls below a minimum, and fit the window to the screen".

Do NOT in this prompt: put any section inside SectionScrollHost, set per-section minimums, cap
the Groups top area, or touch dialogs or PageViewerWindow sizing. Those are Prompts 3 and 4.

Stop when the checkpoint below is satisfied and report the results.
```

```
CHECKPOINT 2 — paste back:
  · dotnet build -c Release → 0 warnings, 0 errors
  · dotnet test -c Release → all passed, count up by the new WindowSizing tests
  · dotnet format --verify-no-changes → exit 0
  · git diff --stat HEAD~1 → WindowSizing.cs, Controls/SectionScrollHost.cs, ShellWindow.xaml(.cs),
    GroupsView.xaml.cs, WindowSizingTests.cs
  · Manual (Franz): the app opens fully on screen, and cannot be dragged smaller than 800×560.
```

---

```
PROMPT 3 of 6 — Sections scroll
Spec: SPEC-2026-001 §05 Q2 (station screen), N5, §08 Part 2, §10 AC-9, AC-11, AC-12, §16 R1, R2, R6, R10
Phase: 2    Depends on: Prompt 2 (green) AND the station's screen recorded in §05 Q2

GATE — check first: open SPEC-2026-001 §05 Q2. If the line "Station display: ______ × ______"
still has blanks, STOP and tell Franz the station's resolution and scale are needed (§11.2 has
the steps). Do not guess a screen size.

Read SPEC-2026-001 §08 Part 2 and §16 before starting.

Setup: on branch `phase-20-fit-and-scrolling`, Prompts 1–2 committed. If this prompt's commit
exists, report and stop.

Do only this:
1. Work out the usable layout size from the recorded station screen: resolution ÷ scale, minus
   taskbar and window chrome. Write the numbers into SPEC-2026-001 §08 Part 2 "Minimums",
   replacing the provisional figures, so the spec and the code agree.
2. Wrap each section's root in SectionScrollHost with those minimums:
   - GroupsView — minimum no larger than the usable size; must still hold 270 + 12 + 240 + 6 + 200.
   - ScanView — replaces nothing; the left panel keeps its own vertical ScrollViewer.
   - SearchView, TrashView.
   - SettingsView — replace its existing ScrollViewer. Width minimum only, height unbounded.
     Its wrapping TextBlocks must still wrap (R6).
3. GroupsView top area (the docked-top StackPanel, GroupsView.xaml:84-261): put it in a
   ScrollViewer (vertical Auto) with MaxHeight = 45% of the detail panel's ActualHeight.
4. Confirm by reading the XAML that the Groups DataGrid and the Scan thumbnail ListBox still get
   a finite height from their parents (R1, R10), and say so in the report.
5. Commit: "Let every section scroll below its minimum size".

Do NOT in this prompt: change dialogs or PageViewerWindow (Prompt 4), redesign the Groups
toolbars, or remember window size.

Stop when the checkpoint below is satisfied and report the results.
```

```
CHECKPOINT 3 — paste back:
  · the minimums written into §08, and the station values they came from
  · dotnet build -c Release → 0 warnings, 0 errors
  · dotnet test -c Release → all passed, no count decrease
  · dotnet format --verify-no-changes → exit 0
  · Manual (Franz), dev machine set to the station's resolution and scale (§11.2):
      - Scan, Groups, Search, Trash, Settings: every button reachable by scrolling (AC-9)
      - a large group scrolls smoothly (AC-11)
      - Ctrl+wheel zooms the preview; plain wheel scrolls (AC-12)
      - drag the window edge slowly across the minimum: scroll bars do not flicker (R11)
```

---

```
PROMPT 4 of 6 — Dialogs and the page viewer
Spec: SPEC-2026-001 §08 Part 2 (Dialogs), §10 AC-10, §12, §16 R7
Phase: 2    Depends on: Prompt 3 (green)

Read SPEC-2026-001 §08 Part 2 and §16 R7 before starting.

Setup: branch `phase-20-fit-and-scrolling`, Prompts 1–3 committed. If this prompt's commit
exists, report and stop.

Do only this:
1. For each fixed dialog in src/FgScanner.App/Views/Dialogs — Batch, Adjust, ExportImages,
   ExportPdf, Input, GroupPicker, Reprocess, DeleteGroup, FirstRun:
   - move the body into a ScrollViewer (vertical Auto) and keep the button row outside it;
   - set MaxHeight to the work area height (via WindowSizing) so SizeToContent cannot exceed
     the screen.
   Read each dialog's XAML and code-behind before editing; keep existing behaviour.
2. DuplicateReviewDialog: MinWidth and MinHeight so its buttons always show; clamp to the work area.
3. PageViewerWindow: on SourceInitialized apply WindowSizing.FitToWorkArea to 1000×820, so Close
   is always visible. Keep Prompt 1's fit deferral working after the resize.
4. FirstRunDialog may show before the shell exists; use the primary work area then (§12).
5. Commit: "Keep dialog buttons on screen and let dialog bodies scroll".

Do NOT in this prompt: change any dialog's fields, wording or logic, or touch sections.

Stop when the checkpoint below is satisfied and report the results.
```

```
CHECKPOINT 4 — paste back:
  · dotnet build -c Release → 0 warnings, 0 errors
  · dotnet test -c Release → all passed
  · dotnet format --verify-no-changes → exit 0
  · git diff --stat HEAD~1 → only files under Views/Dialogs/ (+ WindowSizing if extended)
  · Manual (Franz), at the station's resolution and scale: open each of the 10 dialogs and the
    page viewer — buttons visible without scrolling, body scrolls when needed (AC-10)
```

---

```
PROMPT 5 of 6 — Code review  [START A NEW SESSION FOR THIS]
Spec: SPEC-2026-001    Depends on: Prompt 4 (green)

The session that wrote the code remembers what it meant and reads intent into the code. A fresh
session sees what is actually there. Do not run this in the session that did Prompts 1–4.

On branch `phase-20-fit-and-scrolling`, run /code-review max over `git diff main...HEAD`.

Then check compliance with SPEC-2026-001, the primary criterion:
- Does the code do what §10 AC-1..AC-13 say, and only that? Name any acceptance criterion with
  no test or manual row behind it.
- Walk §16 R1–R11: is each mitigation actually present in the code?
- Is anything built that §03 lists as a non-goal?
Report every drift — it is either a bug in the code or a spec that needs revising.

Resolve every correctness finding with a failing test first where one can be written. For
anything you decline to change, add one line to SPEC-2026-001 §22 saying what and why.

Not web-facing: no security review prompt (§13).
```

```
CHECKPOINT 5 — paste back:
  · the /code-review max findings list and what happened to each
  · the compliance table: AC → test or manual row → present yes/no
  · dotnet build -c Release, dotnet test -c Release, dotnet format --verify-no-changes → all green
```

---

```
PROMPT 6 of 6 — Docs, manual tests, finish
Spec: SPEC-2026-001 §18, §21, §22    Depends on: Prompt 5 (resolved)

Read SPEC-2026-001 §18 and §21.

Do only this:
1. docs/manual-tests.md — add a "Fit and small screens" block: the §11.2 steps for simulating
   the station screen; one row per section (AC-9); one row per dialog plus the page viewer
   (AC-10); rows for AC-8, AC-11, AC-12 and the R11 flicker check.
2. docs/user-guide.md — one line where the preview and viewer are described: "Fit shows the
   whole page; 100% is actual paper size."
3. docs/specs/README.md — status Built once §21 is ticked.
4. Tick the §21 boxes that are actually true, with evidence. Leave "Installed on the station and
   the manual matrix walked there" unticked unless it has happened.
5. Memory: add the Fit trap to the project memory. WPF `Image Stretch="None"` lays out in
   DPI-scaled units, so zoom math must use BitmapSource.Width, never PixelWidth; every old test
   used pixel numbers, which is why it went unnoticed.
6. Commit docs on the branch: "Document Fit and small-screen checks".
7. Ask Franz before merging to main or pushing: CI must be green first (CLAUDE.md), and
   docs/specs, docs/superpowers/research and STATUS-AND-REMAINING-WORK.md are still uncommitted
   on main — ask whether they go in with this merge.

Do NOT in this prompt: bump <Version>, build the installer, or change code.
```

```
CHECKPOINT 6 — paste back:
  · git log --oneline main..HEAD → the Prompt 1–4 commits, any review fixes, and the docs commit
  · the §21 checklist as ticked
  · Franz's answer on merge / push / committing the other docs
```
