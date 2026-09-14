# SPEC-2026-003 — Prompt pack

Prompts for [SPEC-2026-003-scan-viewer-delete-and-quick-scan-tools.md](./SPEC-2026-003-scan-viewer-delete-and-quick-scan-tools.md)
(Approved 2026-09-13, Revision B). Work through them in order. Each stops at a checkpoint; paste the
results back before starting the next.

**Before any prompt:** close FG Scanner. A copy running from `src\FgScanner.App\bin\Release` locks
its DLLs and every Release build fails with `MSB3027`.

**Order across specs:** build this spec before SPEC-2026-002.
- It fixes a bug that is live on `main`.
- It is the smaller job.
- SPEC-002's last prompt edits the same docs (README, CLAUDE.md, manual tests, user guide).

**Branch:** `phase-21-scan-viewer-delete`.

| # | Prompt | Phase | Needs |
|---|---|---|---|
| 1 | Page keys act only on the screen showing | 1 | — |
| 2 | Open staged scans in the viewer | 1 | Prompt 1 green |
| 3 | Delete staged scans to the Recycle Bin | 1 | Prompt 2 green |
| 4 | Quick Scan plan amendment (docs only) | 2 | Prompt 3 green |
| 5 | Code review — **new session** | — | Prompt 4 done |
| 6 | Docs, manual tests, finish | — | Prompt 5 resolved |

---

```
PROMPT 1 of 6 — Page keys act only on the screen showing
Spec: SPEC-2026-003 §04 (Shortcuts), §05 N5, §08 Part A (Screen-aware shortcuts), §10 AC-8..AC-10, §16 R1
Phase: 1    Depends on: nothing

Read SPEC-2026-003 §04, §08 Part A and §16 before starting. Read CLAUDE.md.

Setup (safe to re-run):
- `git status` and `git log --oneline -3`. Other Claude sessions edit this repo. If main has moved
  past 4e3b954 with code changes you did not expect, stop and report.
- Uncommitted changes under docs/specs/ are expected: SPEC-002 and SPEC-003 approved, both prompt
  packs, and the README.
- Create or switch to branch `phase-21-scan-viewer-delete`.
- Commit the docs/specs changes first as "Approve SPEC-2026-002 and 003 and add their prompt
  packs", unless that commit already exists.
- If this prompt's code commit already exists, report that and stop.

Do only this:
1. Read how shortcuts work today:
   - ShellWindow.xaml.cs: ApplyShortcuts, CommandFor and DetailCommand (:77-145)
   - src/FgScanner.Core/ShortcutMap.cs
   - how ShellViewModel tracks which section is showing
   - how tests/FgScanner.App.Tests/ShellTests.cs builds the shell
2. Prove the bug FIRST, in tests/FgScanner.App.Tests/ShortcutRouterTests.cs.
   The test is "delete on Scan never touches the open group": a group is open in Groups with a
   page selected, the Scan section is showing, and the DeletePage shortcut runs. Groups'
   DeleteSelected must not execute.
   The test must exercise today's routing:
   - If ShellTests can build the window, use ShellWindow's real bindings.
   - Otherwise, first move CommandFor/DetailCommand's decision into `ShortcutRouter` unchanged,
     as its own commit "Move shortcut routing into ShortcutRouter", and test that.
   Run it. It must FAIL because the route reaches Groups; paste the failure. A compile error does
   not count as the red.
3. Add the rest of the table as tests:
   - DeletePage, Undo, Redo, RotateLeft and RotateRight, each on Scan, Groups, Search, Trash and
     Settings.
   - Groups: all five reach the open group, as today.
   - Scan: DeletePage reaches a "Scan: delete staged pages" target and never Groups. That target
     does nothing until Prompt 3 gives it a command. The other four keys reach nothing.
   - Search, Trash and Settings: nothing.
   - Scan, ScanAnnotated, ScanNoteFace, SaveToGroup, Commit and ProfileN are unchanged from every
     section (AC-10).
4. Make the tests pass.
   - `ShortcutRouter` (src/FgScanner.App/Views/ShortcutRouter.cs) is WPF-free: it takes an action
     name and the current section and returns a target.
   - ShellWindow asks the router when a key is pressed, not when bindings are applied, because the
     section changes after binding.
   - One comment on why: window-level bindings fire for sections that are not showing.
5. Commit: "Route page shortcuts only to the screen that is showing".

Do NOT in this prompt:
- add selection, the viewer or delete to the Scan page (Prompts 2–3)
- change ShortcutMap's gestures or the Settings shortcut editor
- change how Commit or the scan keys route: §03 keeps them. Report Commit's behaviour; do not fix it.

Stop when the checkpoint below is satisfied and report the results.
```

```
CHECKPOINT 1 — paste back:
  · the red failure message from step 2
  · dotnet build -c Release → 0 Warning(s), 0 Error(s)
  · dotnet test -c Release → total ≥ 545 plus the new tests, 0 failed
  · dotnet format --verify-no-changes → exit 0
  · git diff --stat main → ShortcutRouter.cs, ShellWindow.xaml.cs, ShortcutRouterTests.cs, plus the docs commit
  · Manual (Franz):
      - With a group open, go to Search, click in the results list and press Delete → nothing is deleted.
      - Back on Groups, Delete trashes the selected page as before.
```

---

```
PROMPT 2 of 6 — Open staged scans in the viewer
Spec: SPEC-2026-003 §04 (Scan page, Viewer), §05 N4, §08 Part A (Viewer input, ScanViewModel), §10 AC-1, AC-2, §16 R2
Phase: 1    Depends on: Prompt 1 (green)

Read SPEC-2026-003 §04, §08 Part A and §16 R2 before starting.

Setup: on branch `phase-21-scan-viewer-delete` with Prompt 1 committed. If this prompt's commit
already exists, report that and stop.

Do only this:
1. Write the failing tests FIRST:
   - tests/FgScanner.App.Tests/PageViewerTests.cs → "grid follows the viewer's index" (AC-2)
   - tests/FgScanner.App.Tests/ScanPageViewerTests.cs → "viewer opens at the chosen staged page"
     (AC-1). FakeScanService stages 5 pages and the third is selected. Opening passes all 5 paths in
     SequenceNumber order, with start index 2.
   Neither test may show a window. Give GroupDetailViewModel and ScanViewModel an injectable seam
   for opening the viewer: it takes the paths and the start index and returns the index the viewer
   closed on. Match any seam style already in these view models.
2. PageViewerWindow (Views/Dialogs) takes `IReadOnlyList<string> imagePaths, int startIndex` and
   exposes `CurrentIndex`. GroupDetailViewModel.OpenPageViewer (:317-336) passes the Rows' image
   paths and selects Rows[CurrentIndex] on close. SPEC-001's FitPolicy, DialogFit and ScrollChanged
   re-fit must keep working.
3. ScanView thumbnails:
   - SelectionMode Extended.
   - SelectedPages synced from SelectedItems in code-behind (WPF cannot bind SelectedItems).
   - Double-click or Enter on a thumbnail runs OpenPageViewerCommand.
   - Ctrl+A selects all.
   - AutomationProperties.Name reads "Page N".
   - Keep the ListBox virtualized, with a finite height from its parent.
4. Commit: "Open staged scans in the page viewer".

Do NOT in this prompt:
- add delete or position labels (Prompt 3)
- add any edit tool to the Scan page
- add a delete button inside the viewer (N4)
- touch Save to group

Stop when the checkpoint below is satisfied and report the results.
```

```
CHECKPOINT 2 — paste back:
  · dotnet build -c Release → 0 warnings, 0 errors
  · dotnet test -c Release → all passed, count up by the new tests
  · dotnet format --verify-no-changes → exit 0
  · git diff --stat HEAD~1 → PageViewerWindow.xaml.cs, GroupDetailViewModel.cs, ScanView.xaml(.cs),
    ScanViewModel.cs and the two test files
  · Manual (Franz), `dotnet run --project src/FgScanner.App -- --fake-scanner`:
      - Scan 5 pages and double-click the third → the viewer shows "Page 3 of 5".
      - The arrow keys page through; close the viewer.
      - In Groups, double-click a page, page forward and close → the grid lands on that page.
```

---

```
PROMPT 3 of 6 — Delete staged scans to the Recycle Bin
Spec: SPEC-2026-003 §05 Q1, N1–N3, N5, §08 Part A (discarder, Order of a delete), §09, §10 AC-3..AC-7, §12, §13, §14, §16 R3–R6
Phase: 1    Depends on: Prompt 2 (green)

Read SPEC-2026-003 §08 Part A, §12, §13 and §16 before starting.

Setup: on branch `phase-21-scan-viewer-delete` with Prompts 1–2 committed. If this prompt's commit
already exists, report that and stop.

Do only this:
1. Write the failing tests FIRST.
   tests/FgScanner.App.Tests/StagedPageDeleteTests.cs, with a fake discarder and a fake
   confirmation:
   - Deleting 2 of 5 leaves 3 in Pages and 3 in index.json, and discards exactly those 2 (AC-3).
   - Delete is unavailable while scanning or with nothing selected (AC-4).
   - A failed discard is named in the status line, and the others still go (AC-5).
   - After a delete, Save to group adopts only the remaining pages (AC-7).
   - A declined confirmation changes nothing.
   tests/FgScanner.App.Tests/RecycleBinDiscarderTests.cs:
   - A path outside the session folder is refused (AC-6), including a sibling folder whose name
     starts with the session folder's name, and a path that climbs out with `..`.
   - A real temp file inside a temp session folder is gone afterwards. Do not assert on the
     Recycle Bin's contents; they differ per machine.
   tests/FgScanner.Scanning.Tests/RecoveryTests.cs:
   - "forgotten pages are not recovered" (AC-7).
2. Add `IStagedPageDiscarder` and `RecycleBinDiscarder` in src/FgScanner.App/Services/:
   - P/Invoke SHFileOperationW with FO_DELETE and
     FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_SILENT | FOF_NOERRORUI.
   - pFrom ends in a double null.
   - A non-zero return or fAnyOperationsAborted counts as a failure.
   - Do NOT use Microsoft.VisualBasic.FileIO; §08 says why.
   - Guard: compare full paths (Path.GetFullPath) against Session.FolderPath plus a trailing
     separator.
   - Register it in DI.
3. ScanViewModel gets DeleteSelectedPagesCommand:
   - CanExecute: something is selected and no scan is running.
   - The confirmation goes through an injected Func<string, bool>. Text per §09; Cancel is the
     default button.
   - Order: Session.ForgetPages, then discard each file, then remove the pages from Pages (R3).
   - Status line and logging per §14.
4. Thumbnail labels show position 1…n and close the gap after a delete. Files are not renamed (N3).
5. Add a "Delete selected…" button under the thumbnails, with a tooltip explaining why it is
   disabled. The router's Scan DeletePage target from Prompt 1 now runs this command.
6. Commit: "Delete staged scans to the Recycle Bin before saving".

Do NOT in this prompt:
- delete pages already saved to a group from the Scan page
- touch TrashService
- change Save to group's ordering, the note-sheet sequence or auto-save
- start any Quick Scan work

Stop when the checkpoint below is satisfied and report the results.
```

```
CHECKPOINT 3 — paste back:
  · dotnet build -c Release → 0 warnings, 0 errors
  · dotnet test -c Release → all passed, count up by the new tests
  · dotnet format --verify-no-changes → exit 0
  · the discarder's guard code, pasted
  · Manual (Franz), with --fake-scanner:
      - Scan 5 pages, Ctrl-click 2, press Delete and confirm → 3 remain, labelled Page 1–3.
      - Open the Windows Recycle Bin → the 2 files are there. Restore one by hand.
      - Save to group → the group has 3 pages.
      - Scan 3 more, delete 1, close FG Scanner without saving, relaunch → recovery offers 2 pages.
```

---

```
PROMPT 4 of 6 — Quick Scan plan amendment (docs only)
Spec: SPEC-2026-003 §03 Part B, §05 Q2–Q6, N6, N7, §06 A3–A4, §08 Part B, §10 AC-11, §16 R7–R8
Phase: 2    Depends on: Prompt 3 (green)

Read these before starting:
- SPEC-2026-003 §05 answers and §08 Part B
- docs/superpowers/plans/2026-08-30-quick-scan-program.md: §1, §2 (bound decisions), and §6
  Phases 1, 3, 5, 6, 8 and 10
- docs/superpowers/research/2026-08-30-quick-scan-spikes.md
- docs/spec-scan-section.md

Do only this:
1. Check assumption A4 by reading UndoRedoService's constructor and every caller: can Quick Scan use
   it without the database? Record the answer in the plan's Phase 6, with file:line evidence.
2. Add a "§1a · The 2026-09-13 requests" table to the plan, mapping each request to where it lands:
   delete scan · scan to PDF · email PDF · email images · double-click viewer · print ·
   typed custom size · scan-then-draw-a-box · crop · OCR · undo · redo · rotate ccw · rotate cw ·
   flip · angle · deskew.
   The viewer and delete land on the Scan page (SPEC-2026-003). Custom size lands on both screens,
   via Phase 1.
3. Edit the phases to the answers:
   - Phase 1 — custom size is also offered on the evidence Scan page (Q5): a capture setting, not an
     edit.
   - Phase 3 — a page strip with select, delete (reusing IStagedPageDiscarder) and double-click
     viewer (N7).
   - Phase 5 — preview, draw the box, final scan cropped in software. No saved named sizes (Q6).
   - Phase 6 — add rotate ccw/cw, flip, angle, deskew and undo/redo. No split, combine or reorder (N6).
   - New Phase 8a — OCR makes searchable PDFs plus a "Copy text" button. No .txt files (Q3), and no
     database. Confirm the name of the existing searchable-PDF setting before citing it.
   - Phase 10 — rewrite it:
     - try the Windows Share sheet first, then MAPI when classic Outlook is present, otherwise open
       the folder with the file selected
     - send images as well as PDF
     - the Windows SDK target-framework change is untried in this solution, so it is the phase's
       first step, as a spike (Q4)
4. Re-estimate phase sizes in the table.
5. Update the plan's status line: amended 2026-09-13 by SPEC-2026-003; spikes 1 and 3 done; not
   started. Record the spike 3 decision.
6. docs/spec-scan-section.md: set its status to "Superseded 2026-09-13 by the Quick Scan program plan
   (SPEC-2026-003 §05 Q2). Save to group stays on the Scan page because note-sheet capture depends
   on it." Update any line in docs/STATUS-AND-REMAINING-WORK.md that still calls it approved.
7. Commit: "Amend the Quick Scan plan with the 2026-09-13 Scan-page requests".

Do NOT in this prompt:
- write code
- renumber existing phases (8a is inserted so that references elsewhere stay valid)
- change the plan's §2 bound decisions
- write ADRs 0006–0008 (they belong to Quick Scan's own phases)

Stop when the checkpoint below is satisfied and report the results.
```

```
CHECKPOINT 4 — paste back:
  · git diff --stat HEAD~1 → only the plan, docs/spec-scan-section.md and the STATUS doc
  · the §1a table
  · the A4 finding with its file:line evidence
  · Manual (Franz): read `git diff HEAD~1` and confirm it matches your answers (AC-11)
```

---

```
PROMPT 5 of 6 — Code review  [START A NEW SESSION FOR THIS]
Spec: SPEC-2026-003    Depends on: Prompt 4 (done)

The session that wrote the code remembers what it meant and reads intent into the code. A fresh
session sees what is actually there. Do not run this in the session that did Prompts 1–4.

On branch `phase-21-scan-viewer-delete`, run /code-review max over `git diff main...HEAD`.

Then check compliance with SPEC-2026-003, which is the primary criterion:
- Does the code do what §10 AC-1..AC-12 say, and only that? Name any criterion with no test or
  manual row behind it.
- Walk §16 R1–R8: is each mitigation actually in the code?
- Is anything built that §03 lists as a non-goal? For example:
  - an edit tool on Scan
  - a delete button in the viewer
  - Commit or the scan keys routed differently
- Attack the discarder's path guard: a sibling folder sharing the prefix, `..`, relative paths,
  UNC paths, different letter case. Check the SHFileOperationW struct marshalling on x64.
Report every drift. Each is either a bug in the code or a spec that needs revising.

Resolve every correctness finding, with a failing test first wherever one can be written. For
anything you decline to change, add one line to SPEC-2026-003 §22 saying what and why.

Not web-facing, so there is no separate security prompt. The file-deletion boundary (§13) is
reviewed here instead.
```

```
CHECKPOINT 5 — paste back:
  · the /code-review max findings and what happened to each
  · the compliance table: AC → test or manual row → present yes/no
  · dotnet build -c Release, dotnet test -c Release, dotnet format --verify-no-changes → all green
```

---

```
PROMPT 6 of 6 — Docs, manual tests, finish
Spec: SPEC-2026-003 §18, §21, §22    Depends on: Prompt 5 (resolved)

Read SPEC-2026-003 §18 and §21.

Do only this:
1. CLAUDE.md — one line: page shortcuts are routed per section by ShortcutRouter, and a new
   shortcut must say which section it belongs to.
2. docs/manual-tests.md — add a "Scan page review" block: the Prompt 2 and 3 manual steps, and the
   Prompt 1 shortcut check on every section.
3. docs/user-guide.md — Scan section: check pages and delete bad ones before saving.
4. build/installer/evidence-setup-walkthrough.txt — one step for Jim: "Double-click a page to check
   it. A bad page? Select it and press Delete before you save."
5. docs/FEATURE-PARITY.md — add a row.
6. Tick the §21 boxes that are actually true, with evidence. Leave the station box unticked.
   docs/specs/README.md → "Built — manual checks pending".
7. Memory: record the shortcut trap. Window-level bindings fire for sections that are not showing,
   so route through ShortcutRouter.
8. Commit: "Document the Scan page viewer and staged delete".
9. Ask Franz before pushing or merging. CI runs only on pull requests and on main, so open a PR and
   merge after CI is green.

Do NOT in this prompt: bump <Version>, build the installer, or change code.
```

```
CHECKPOINT 6 — paste back:
  · git log --oneline main..HEAD
  · the §21 checklist as ticked
  · Franz's answer on the PR and merge
```
