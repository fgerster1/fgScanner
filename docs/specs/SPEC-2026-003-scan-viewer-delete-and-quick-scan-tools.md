# SPEC-2026-003 — Scan page viewer and delete; page tools move into Quick Scan

| | |
|---|---|
| **Status** | Approved |
| **Revision** | B |
| **Tier** | Feature |
| **Author** | Claude, for Franz Gerster |
| **Date** | 2026-09-13 |
| **Project** | FgmakerScanner |
| **Supersedes** | — |

Part of the 2026-09-13 "Fixes and Changes" request. **Decided 2026-09-13 (Franz, in conversation):**
the evidence Scan page gets the double-click viewer and delete for pages not yet saved to a group.
PDF, email, print, custom page size, window-around crop, edit tools, OCR and undo/redo go into
**Quick Scan**, whose program plan this spec amends.

---

## 01 · Management summary

**What we are building.** Two things on the **Scan page**, where evidence is captured:
- Double-click a scanned page to see it full size, stepping through the others.
- Delete a bad scan **before** it is saved into a group.

It also fixes a keyboard bug found while reading the code. The Delete key, and Undo, Redo and Rotate,
act on the page selected in **Groups** even while another screen is showing.

The rest of the Scan-page list moves into the **Quick Scan** screen:
- PDF, email and print
- custom page sizes and "scan, draw a box, scan to that size"
- rotate, flip, angle, deskew and crop
- OCR, undo and redo

That screen was already planned for everyday documents. This spec **updates its plan** so every item
has a home and a phase. It does not build Quick Scan.

**Why.** Today a crooked or double-fed page can only be removed after it has been saved into an
evidence group, by trashing it from Groups. Editing tools don't belong on the evidence Scan page:
pages there have no checksum and no preserved original yet, so an edit before saving leaves no trace
that the page was ever changed.

**What it costs.** This spec: about a day, 6 prompts across 2 phases. **Quick Scan itself** is the
existing 10-phase plan — several weeks of work, started phase by phase once you approve it.

**What could go wrong.** Changing which screen a shortcut acts on could break the Groups shortcuts
people already use; tests cover every key on every screen. A deleted scan could be the wrong one,
so deletes go to the Windows Recycle Bin (§05 Q1).

**Decided (Round A, 2026-09-13).**
- Deleted scans go to the Windows Recycle Bin.
- The old scan-to-folder spec is retired in favour of Quick Scan.
- In Quick Scan:
  - OCR makes searchable PDFs, plus a "Copy text" button.
  - Email tries the Windows Share sheet first, then MAPI, then opens the folder.
  - "Draw a box" crops the final scan in software.
- Custom page size comes to both Scan and Quick Scan.

## 02 · Outcomes

- **Goal** — no bad scan reaches an evidence group unseen, and no Groups page is ever deleted from
  another screen by a stray key.
- **Who benefits** — Jim at the station; Franz, whose groups no longer need after-the-fact trashing.
- **How we will know it worked**
  - Every staged page can be opened and deleted before saving (tests AC-1, AC-3).
  - Delete, Undo, Redo and Rotate keys act only on the screen showing (tests AC-8 to AC-10).
  - The Quick Scan plan lists a phase for every 2026-09-13 Scan-page request (AC-11).

## 03 · Scope and non-goals

**In scope — Part A, built**
- Scan page thumbnails become selectable (click, Shift/Ctrl multi-select, Ctrl+A).
- Double-click or Enter opens `PageViewerWindow` on the staged pages at that page.
- "Delete selected" button and the Delete key remove selected staged pages, after a confirmation
  naming how many (§05 Q1 decides where they go). The recovery index is updated.
- Page labels show position (Page 1…n) after deletes.
- Shortcut routing becomes screen-aware for Delete, Undo, Redo, Rotate left and Rotate right.

**In scope — Part B, plan amendment (no code)**
- `docs/superpowers/plans/2026-08-30-quick-scan-program.md` gains a request-to-phase table and the
  added scope:
  - a page strip with select, delete and viewer
  - rotate ccw/cw, flip, angle, deskew, undo/redo
  - OCR (§05 Q3)
  - email route (§05 Q4), and email of images as well as PDF
  - custom page size entry (§05 Q5)
  - the "draw a box and scan to it" flow (§05 Q6)
- Its status line and the spike 3 decision are updated.
- `docs/spec-scan-section.md` gets its status updated per §05 Q2.

**Non-goals**
- No rotate, crop, adjust, OCR, PDF, ~~email~~ or print on the evidence Scan page.
  > **Amended 2026-09-20.** Franz approved email on the evidence Scan page
  > (SPEC-2026-007 §05 Q1a, Round A part 3, verdict *approve*). The page gains an **Email…**
  > button that sends the session's pages; every other exclusion on this line stands. See
  > [SPEC-2026-007](./SPEC-2026-007-email-scanned-pages.md) and ADR-0012.
- No deleting pages already saved to a group from the Scan page.
- No change to Save to group, Batch scan, the note-sheet sequence, or auto-save after
  "Scan into this group".
- Scan and note-sheet shortcut keys keep working from any screen, as today — only the five page
  keys change.
- Quick Scan is not built here.
- No delete button inside the viewer.

## 04 · Current state

- **Scan page** (`src/FgScanner.App/Views/ScanView.xaml`, `ScanViewModel.cs`):
  - Thumbnails are a virtualized `ListBox` of `ScannedPage(FilePath, SequenceNumber)`
    (`ScanView.xaml:96-120`) with no selection binding, no double-click and no context menu.
  - Code-behind is `InitializeComponent` only.
  - Pages stream into a recovery session under `%APPDATA%\FGScanner\recovery\<guid>\`
    (`ScanSessionService.cs:16-30`).
  - Pages are saved by `SaveToGroupAsync` in `SequenceNumber` order (`ScanViewModel.cs:429-498`).
  - `ScanViewModel` already injects `PageEditingToolset` and `TrashService` (`:21-22`).
- **Recovery session** (`src/FgScanner.Scanning/Recovery/RecoverySession.cs`):
  - `ForgetPages(paths)` drops pages from the in-memory list and force-writes `index.json` (`:91-105`).
  - `ResetSession` deletes the whole folder (`ScanSessionService.cs:35-39`).
  - Closing the app keeps a non-empty session for recovery (`:64-75`).
  - `ForgetPages` exists because an index naming missing files breaks recovery (`:86-90`).
- **Trash** — `TrashService.DeleteDocumentAsync` requires a database `Document` (`TrashService.cs:30-33`).
  Staged pages have none.
- **Viewer** — `PageViewerWindow(IReadOnlyList<DocumentRow>, int)` (`Views/Dialogs/PageViewerWindow.xaml.cs:24`)
  uses only `ImagePath` (`:42-43`). Its one caller maps back by `DocumentId`
  (`GroupDetailViewModel.cs:316-336`).
- **Shortcuts — the bug.** `ShellWindow.ApplyShortcuts` binds every shortcut at **window level**
  (`ShellWindow.xaml.cs:77-106`, re-checked at `4e3b954`).
  - `DeletePage`, `Undo`, `Redo`, `RotateLeft` and `RotateRight` route through `DetailCommand`,
    which executes on `GroupsViewModel.Detail` **whenever a group is open, regardless of which
    section is showing** (`:108-145`).
  - `Commit` goes through the same `DetailCommand` (`:114`). §03 leaves it unchanged; it is raised
    with Franz separately.
  - The default Delete gesture is plain `Delete` (`src/FgScanner.Core/ShortcutMap.cs:52`).
  - Found by reading, **not yet reproduced**. A focused TextBox handles Delete itself, so the bug
    fires when focus is on a list, button or grid outside Groups. Prompt 1 proves it with a test
    before fixing it.
- **Note sheets** — each capture saves itself (`ScanViewModel.cs:362-375`), so captures are never
  staged. The sequence records document ids only after adoption (`:442-448`).
- **Quick Scan plan** — 11 phases, "proposed, not started" (`quick-scan-program.md:4`).
  - Phase 0 spikes 1 and 3 answered 2026-09-12
    (`docs/superpowers/research/2026-08-30-quick-scan-spikes.md`): no device-side region scan, so
    crop is in software; Simple MAPI is not viable on the dev PC.
  - Bound decisions: never touches the database, refuses evidence folders, visually distinct,
    nothing hidden per station (`:42-70`).
- **Older spec** — `docs/spec-scan-section.md` (2026-08-24, "approved design, not implemented")
  would make Scan a scan-to-folder tool and **remove Save to group** (`:85-93`). That predates the
  note-sheet sequence, which depends on Save to group.
- **Tests** — 545 passing (Release, `4e3b954`, after SPEC-001 merged). Relevant: `ShellTests.cs`, `ScanReturnTests.cs`,
  `AnnotatedScanTests.cs`, `PageViewerTests.cs`, `tests/FgScanner.Scanning.Tests/RecoveryTests`.
- **Not examined** — `ShellViewModel` section switching; `RecoveryManager` orphan reading;
  `UndoRedoService`'s dependencies (Quick Scan reuse must be checked database-free before the plan
  claims it); Windows Recycle Bin behaviour on the station's drives.

## 05 · Questions for Franz

**Answered 2026-09-13, Round A** — [review page](https://claude.ai/code/artifact/77e41ea4-403e-4493-8e27-85cf7b96b379),
db doc `review/SPEC-2026-003-rA`, verdict **approve**. Every blocking question took option (a), and
every call N1–N8 was agreed. No notes.

| # | Answer |
|---|---|
| Q1 | (a) Windows Recycle Bin |
| Q2 | (a) Mark `docs/spec-scan-section.md` superseded by Quick Scan |
| Q3 | (a) Quick Scan OCR: searchable PDF plus a "Copy text" button |
| Q4 | (a) Share sheet first; MAPI when classic Outlook is present; otherwise open the folder |
| Q5 | (a) Custom page size on both Scan and Quick Scan |
| Q6 | (a) Preview, draw the box, final scan cropped to it in software; no saved named sizes |

The questions as asked are kept below for the record.

**Blocking**

1. **Where does a deleted scan go?**
   - (a) **The Windows Recycle Bin.** *(recommended)*
   - (b) Deleted permanently after the confirmation.
   - (c) FG Scanner's own Trash.
   *Why it matters:* (a) is recoverable, using Windows' own shell file operation (about 30 lines, no
   new package). (b) is simplest, but a mis-click on page 180 of 200 is gone. (c) needs new Trash plumbing, because Trash only
   holds database records (`TrashService.cs:30-33`), costing about half a day more. A staged scan
   is not yet part of any evidence record (A1); if you consider every capture evidence, choose
   (c) and say so.
2. **Retire the 2026-08-24 "Scan section becomes scan-to-folder" spec?**
   - (a) **Mark it superseded by Quick Scan.** *(recommended)*
   - (b) Keep it as a future plan.
   *Why it matters:* it removes Save to group from the evidence Scan page, which the note-sheet
   capture built since then relies on. Quick Scan now covers scan-to-folder for everyday documents.
3. **What does OCR do in Quick Scan, which has no database?**
   - (a) **Makes PDFs searchable, plus a "Copy text" button.** *(recommended)*
   - (b) Also save a `.txt` file next to each image.
   - (c) Searchable PDF only.
4. **How does Quick Scan send email?** Spike 3 found MAPI does not work on this PC: only the new
   Outlook is installed.
   - (a) **Windows Share sheet first; MAPI when classic Outlook is present; otherwise open the folder with the file selected.** *(recommended)*
   - (b) As originally planned: MAPI, otherwise open the folder.
   - (c) Leave email out.
   *Why it matters:* with (b) the Email button never produces an email on this station. (a) needs
   a Windows SDK target-framework change that is untested in this solution.
5. **Custom page size on the evidence Scan page too?**
   - (a) **Yes — both Scan and Quick Scan get "Custom…" width × height.** *(recommended)*
   - (b) Quick Scan only.
   *Why it matters:* evidence boxes hold odd sizes (receipts, legal), and a Letter scan cuts a
   longer page off. This is a capture setting, not an edit, so evidence rules allow it. It costs
   one dropdown entry once Quick Scan Phase 1 adds the option underneath.
6. **"Scan a page, draw a box, scan to that size" — how far does it go?**
   - (a) **Preview, draw the box, and the final scan is cropped to it in software** (the scanner cannot scan a region, per spike 1). *(recommended)*
   - (b) Also save the box as a named custom size for reuse.

**Non-blocking** — proceeding on these unless corrected.

1. Several pages can be deleted at once; the confirmation names the count.
2. Delete is disabled while a scan is running.
3. Labels show position 1…n after deletes; file names are not renamed.
4. The viewer opened from Scan is view-only.
5. Shortcuts: Delete, Undo, Redo and Rotate act only on the screen showing. On Scan, Delete deletes
   staged pages; on Search, Trash and Settings these keys do nothing.
6. Quick Scan's edit set: rotate ccw/cw, flip, angle, deskew, crop, undo/redo, plus the planned
   Advanced adjustments. No split, combine or reorder.
7. Quick Scan gets the same page strip (select, delete, double-click viewer) as Scan.
8. The app's WPF Fluent theme governs; the web "Organic" kit does not apply.

## 06 · Assumptions

| # | Assumption | If wrong |
|---|---|---|
| A1 | A staged page is not yet evidence: no checksum, no index row, no manifest | Deleting needs a journal (like capture-policy drops) and §05 Q1 becomes (c) |
| A2 | The Recycle Bin exists for `%APPDATA%` (system drive) | Delete fails; handled in §12, never silent |
| A3 | The Quick Scan plan's 2026-08-30 decisions still stand | Part B rewrites more than it adds |
| A4 | `UndoRedoService` can serve Quick Scan without the database — its only import is `System.IO` (`src/FgScanner.App/Services/UndoRedoService.cs:1`); callers not yet checked | The plan's undo/redo phase needs its own small stack |

## 07 · Data model

_Not applicable_ — no database change. The recovery session's `index.json` is updated through the
existing `ForgetPages`.

## 08 · Architecture and approach

### Part A

- **Viewer input.** Change `PageViewerWindow` to take `IReadOnlyList<string> imagePaths, int
  startIndex` and expose `CurrentIndex`. `GroupDetailViewModel.OpenPageViewer` passes
  `Rows.Select(r => r.ImagePath)` and selects `Rows[viewer.CurrentIndex]` on close. This is the
  smallest change; the window only ever used the path.
- **`ScanViewModel`**
  - Adds `SelectedPages` (synced from the ListBox's `SelectedItems` in code-behind; WPF cannot bind
    it), `OpenPageViewerCommand`, and `DeleteSelectedPagesCommand` (CanExecute: selection not
    empty and not scanning).
  - The confirmation goes through an injectable `Func<string, bool>` so tests do not show a
    MessageBox.
- **`IStagedPageDiscarder`** (`src/FgScanner.App/Services/`), with `RecycleBinDiscarder`:
  - It P/Invokes `SHFileOperationW` (shell32) with `FO_DELETE` and
    `FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_SILENT | FOF_NOERRORUI`, and reports failure by
    return code.
  - It does not use `Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(…, SendToRecycleBin)`.
    That assembly (`Microsoft.VisualBasic.Forms`) ships only in the WindowsForms profile of the
    desktop reference pack (verified in `FrameworkList.xml`, 2026-09-13). This app is `UseWPF`
    only, so using it would mean switching on WinForms.
  - Tests use a fake.
  - **Guard:** refuses any path not inside `Session.FolderPath`.
- **Order of a delete:**
  1. `Session.ForgetPages(selected)`, so the index never names a file about to disappear (R3).
  2. Discard each file.
  3. Remove the pages from `Pages`.

  A file that cannot be recycled stays unlisted on disk and goes with the session folder at the
  next reset. The status line says which, and a warning is logged. The page is out of the index
  either way, which is what the operator asked for.
- **Screen-aware shortcuts.**
  - Extract the routing in `ShellWindow.CommandFor/DetailCommand` into a WPF-free `ShortcutRouter`
    that takes the current section.
  - `DeletePage` goes to Scan's `DeleteSelectedPagesCommand` on Scan and Groups' `DeleteSelected` on
    Groups, and does nothing elsewhere.
  - Undo, Redo and Rotate go to Groups only on Groups.
  - The Scan, note-sheet and Save-to-group keys are unchanged.

**Alternatives rejected**
- **Tools on the evidence Scan page** — declined 2026-09-13; edits before adoption leave no
  original or checksum trail (ADR-0003).
- **FG Trash for staged pages** — possible (§05 Q1c) at extra cost.
- **Section-scoped `InputBindings` on each view instead of window level** — shortcuts would stop
  working whenever focus sat outside the view (e.g. on the nav list). The router keeps one binding
  set with explicit, testable routing.

### Part B — the plan amendment

Edit `docs/superpowers/plans/2026-08-30-quick-scan-program.md`:
- **§1a** — a table mapping each 2026-09-13 request to its phase.
- **Phase 1** — custom page size wired on Scan too, if §05 Q5 is (a).
- **Phase 3** — page strip with select, delete (same discarder) and double-click viewer.
- **Phase 5** — "draw a box and scan to it", per Q6.
- **Phase 6** — adds rotate ccw/cw, flip, angle, deskew and undo/redo, checking `UndoRedoService`
  is database-free first (A4).
- **New Phase 8a** — OCR per Q3.
- **Phase 10** — rewritten to the Q4 answer, including images as well as PDF.
- **§4 status** — updated; spikes referenced.

Sizes are re-estimated in the table. Any new phase follows the plan's own prompt format.

**Data access / system evolution.** None. The Quick Scan plan's project skill (`fgscanner-wpf-section`,
§7.2) should also record `ShortcutRouter` as the one place shortcuts are routed.

## 08a · AI opportunity assessment

Not a fit for Part A. Spotting a bad scan to delete is already served deterministically by
`BlankPageDetector` and the capture policy. The page-quality ideas worth having (auto colour
detection, multi-feed detection) are already in the Quick Scan plan's §8.

## 09 · Design system compliance

The WPF Fluent theme governs. Controls:
- **Button** "Delete selected…" under the thumbnails, disabled with a tooltip when nothing is
  selected.
- **Confirmation text:** "Move 3 scanned pages to the Recycle Bin? They have not been saved to a group."
- **Selection** uses the default Fluent highlight.

**Accessibility.** Thumbnails are keyboard-selectable. Enter opens, Delete deletes, and Ctrl+A
selects all. Each thumbnail's automation name is "Page N". The confirmation's default button is
Cancel.

## 10 · Acceptance criteria

- **AC-1** — Opening the viewer from a staged page passes all staged paths in order, starting at
  that page.
  *Proven by:* `tests/FgScanner.App.Tests/ScanPageViewerTests.cs` → "viewer opens at the chosen staged page"
- **AC-2** — On Groups, the grid still selects the page the viewer closed on.
  *Proven by:* `tests/FgScanner.App.Tests/PageViewerTests.cs` → "grid follows the viewer's index"
- **AC-3** — Deleting 2 of 5 staged pages leaves 3 in `Pages` and in `index.json`, and discards
  exactly those 2 files.
  *Proven by:* `tests/FgScanner.App.Tests/StagedPageDeleteTests.cs`
- **AC-4** — Delete cannot run while scanning or with nothing selected.
  *Proven by:* `StagedPageDeleteTests.cs` → "delete is unavailable while scanning"
- **AC-5** — A file the discarder cannot remove is named in the status line, and the rest still go.
  *Proven by:* `StagedPageDeleteTests.cs` → "failed discard is reported, others proceed"
- **AC-6** — The discarder refuses a path outside the session folder.
  *Proven by:* `tests/FgScanner.App.Tests/RecycleBinDiscarderTests.cs`
- **AC-7** — After deleting, Save to group adopts only the remaining pages; relaunch recovery
  offers only the remaining pages.
  *Proven by:* `StagedPageDeleteTests.cs` and `tests/FgScanner.Scanning.Tests/RecoveryTests` → "forgotten pages are not recovered"
- **AC-8** — **Written first, failing on today's code:** with a group open in Groups and the Scan
  section showing, the Delete route does not reach Groups' delete.
  *Proven by:* `tests/FgScanner.App.Tests/ShortcutRouterTests.cs` → "delete on Scan never touches the open group"
- **AC-9** — Delete, Undo, Redo and Rotate route to nothing on Search, Trash and Settings.
  *Proven by:* `ShortcutRouterTests.cs`
- **AC-10** — On Groups, all five keys behave as before; the Scan and note-sheet keys still work
  from any section.
  *Proven by:* `ShortcutRouterTests.cs`
- **AC-11** — The Quick Scan plan maps every 2026-09-13 Scan-page request to a phase, records the
  spike 3 decision and §05 answers, and updates its status. `spec-scan-section.md` shows its new
  status.
  *Proven by:* manual — Franz reviews the doc diff
- **AC-12** — Full suite green.
  *Proven by:* `dotnet test -c Release`

## 11 · Test strategy

**11.1 — The failing tests to write first**

| Feature | Test file | The failing assertion |
|---|---|---|
| Shortcut bug | `tests/FgScanner.App.Tests/ShortcutRouterTests.cs` | Delete on Scan routes to Groups' delete today → must not |
| Viewer paths | `tests/FgScanner.App.Tests/ScanPageViewerTests.cs` | start index 2, 5 paths in sequence order |
| Staged delete | `tests/FgScanner.App.Tests/StagedPageDeleteTests.cs` | 3 pages remain, index lists 3 |
| Discard guard | `tests/FgScanner.App.Tests/RecycleBinDiscarderTests.cs` | path outside the session throws |
| Recovery | `tests/FgScanner.Scanning.Tests/RecoveryTests` | orphan with forgotten pages recovers 3 |

**11.2 — Test data.** `FakeScanService` produces staged pages in a temp recovery root, as the
existing `ShellTests` do. The discarder is faked in VM tests; one discarder test uses a real temp
file and asserts it leaves the folder. It does not assert on Recycle Bin contents, which differ per
machine.

**11.3 — Verification suite.** Close FG Scanner first, then `dotnet build -c Release`,
`dotnet test -c Release`, `dotnet format --verify-no-changes`. Manual:
`docs/manual-tests.md` § Scan page review — scan 5 pages with `--fake-scanner`, open, delete 2,
restore one from the Recycle Bin by hand to prove it is there, save the rest to a group.

## 12 · Edge cases and failure modes

| Case | Expected behaviour | Where handled |
|---|---|---|
| Delete every staged page | Session empty; Save to group disabled; empty session discarded on exit as today | VM + `ScanSessionService.Dispose` |
| Delete pressed twice fast | Second press has no selection; nothing happens | CanExecute |
| File locked by antivirus | Named in status; page leaves the index; file goes with the session folder | discarder + VM |
| Recycle Bin disabled by policy | Same as locked, message says the Recycle Bin refused it | discarder |
| Recovered orphan pages | Deletable like any staged page | same path |
| A note sheet in progress with a failed-save page staged | Deleting the staged page is allowed; the sequence holds only adopted ids | `AnnotatedCaptureSequence` untouched |
| 200 pages selected | One confirmation, one index write, discards in a loop with status progress | VM |
| Path outside the session folder | Refused, logged as an error | discarder guard |
| Viewer open while pages change | Modal, so cannot happen | window |
| Groups section showing, focus in the grid, Delete | Deletes the Groups page as today | router |

## 13 · Security and configuration

Not web-facing. File deletion is the boundary: the discarder accepts only paths inside the current
session folder (AC-6), so a corrupted index cannot aim it anywhere else. No new dependencies —
the Recycle Bin call is a P/Invoke into `shell32.dll`, which every Windows install has. No
environment variables.

## 14 · Observability

- **Information log per delete:** count and session folder. **Warning** per file that could not be
  discarded.
- Status line after every delete: "Deleted 2 page(s) — in the Recycle Bin."
- **A silent failure would look like** a page that disappears from the screen but comes back in
  recovery, or is saved to the group anyway. AC-3 and AC-7 assert against `index.json` and adoption,
  not just the on-screen list.
- **Shortcut regressions** would be silent by nature; `ShortcutRouterTests` covers every key and
  section combination.

## 15 · Performance and scale

Stacks of up to a few hundred staged pages. One index write per delete, one file operation per
page. Not a performance question.

## 16 · Regression risk

| # | What could break | Why | Evidence (file:line) | Mitigation |
|---|---|---|---|---|
| R1 | Groups shortcuts stop working, or start firing somewhere new | Routing moves out of `ShellWindow` | `ShellWindow.xaml.cs:108-145` | `ShortcutRouter` with a test per key × section (AC-8 to AC-10) |
| R2 | Page viewer from Groups opens on the wrong page, or the grid loses its place | Constructor changes from rows to paths | `GroupDetailViewModel.cs:316-336`, `Dialogs/PageViewerWindow.xaml.cs:24-34` | AC-2 |
| R3 | A crash mid-delete makes recovery stop at a missing file | The index would name a file already discarded | `RecoverySession.cs:86-105` | Forget first, then discard (§08) |
| R4 | Save to group adopts a deleted page, or misses one | Save orders `Pages` by `SequenceNumber` | `ScanViewModel.cs:435-438` | Pages removed from the collection; gaps are harmless; AC-7 |
| R5 | Note-sheet state confused by a delete | The sequence tracks adopted documents | `ScanViewModel.cs:442-448` | Captures are never staged; the delete touches only staged files |
| R6 | Deleting from the recovery folder deletes the wrong thing | A path taken from a bad index | `RecoverySession.cs:158-162` | Guard to the session folder (AC-6) |
| R7 | The Quick Scan plan amendment contradicts a bound decision | New OCR or undo scope could reach the database | `quick-scan-program.md:42-70` | Part B prompt re-reads §2 and checks `UndoRedoService` dependencies (A4) |
| R8 | Docs or links still describe the old scan-to-folder spec as approved | Its status changes (§05 Q2) | `docs/spec-scan-section.md:3`, STATUS doc | Part B updates both |

## 17 · Migration and rollback

- **Forward** — ships in the next release; no data.
- **Backward** — revert; files already sent to the Recycle Bin stay there and can be restored by
  hand.
- **Point of no return** — none.
- **Backup** — not needed.

## 18 · Documentation updates

- **CLAUDE.md** — one line: shortcuts are routed per section by `ShortcutRouter`; a new shortcut
  must say which section it belongs to.
- **`docs/superpowers/plans/2026-08-30-quick-scan-program.md`** — amended (Part B).
- **`docs/spec-scan-section.md`** — status per §05 Q2.
- **`docs/manual-tests.md`** — "Scan page review" block.
- **`docs/user-guide.md`** — Scan section: check and delete pages before saving.
- **`build/installer/evidence-setup-walkthrough.txt`** — one short step for Jim: "Double-click a
  page to check it. A bad page? Select it and press Delete *before* you save."
- **`docs/FEATURE-PARITY.md`** — row.
- **Memory** — record the shortcut routing trap: window-level bindings fire for hidden sections.

## 19 · Phase plan

### Phase 1 — Check and discard before saving
- **Objective** — Scan page viewer, staged delete, screen-aware shortcuts.
- **Delivers** — AC-1 to AC-10, AC-12.
- **Not in this phase** — any Quick Scan code; any edit tool on Scan.
- **Done when** — tests pass and the manual Scan-page block is walked with `--fake-scanner`.

### Phase 2 — Every request has a home
- **Objective** — the Quick Scan plan and the old scan-to-folder spec reflect the decisions.
- **Delivers** — AC-11.
- **Not in this phase** — building any Quick Scan phase.
- **Done when** — Franz has reviewed the plan diff.

## 20 · Prompt pack

See [SPEC-2026-003-scan-viewer-delete-and-quick-scan-tools-PROMPTS.md](./SPEC-2026-003-scan-viewer-delete-and-quick-scan-tools-PROMPTS.md)
— 6 prompts, written 2026-09-13 on approval:
1. Shortcut router, failing test first
2. Viewer paths and Scan page viewer
3. Staged delete
4. Quick Scan plan amendment
5. Code review (fresh session)
6. Docs and manual tests

## 21 · Definition of done

- [x] All acceptance criteria met. AC-1..AC-10 and AC-12 are covered by tests (§10), with 628 passing
  at `225ddb5`. AC-11 was signed off by Franz on 2026-09-14 (§22).
- [x] Failing tests written first, now passing. The red runs were recorded at Prompts 1–3, and the
  review's fixes also started from failing tests.
- [x] Full suite green: `dotnet test -c Release` gives 628 total, 0 failed (`225ddb5`, 2026-09-14).
- [x] `/code-review max` run on 2026-09-14 by a fresh reviewer: 9 fix commits, and every declined or
  deferred finding is recorded in §22.
- [x] Security review — _not applicable, not web-facing_. The file-deletion boundary was reviewed
  under Prompt 5.
- [x] Documentation updated per §18:
  - CLAUDE.md
  - the Quick Scan plan and `spec-scan-section.md` (Prompt 4)
  - `manual-tests.md`
  - `user-guide.md`
  - `evidence-setup-walkthrough.txt`, per Franz's 2026-09-14 decision in §22
  - `FEATURE-PARITY.md`
  - memory
- [ ] Installed on the station; Jim's walkthrough step confirmed
- [x] Rollback — _waived: revert only, no data_

## 22 · Sign-off

| | |
|---|---|
| **Review round answered** | ☑ date: 2026-09-13 · Round A: https://claude.ai/code/artifact/77e41ea4-403e-4493-8e27-85cf7b96b379 (db doc `review/SPEC-2026-003-rA`) |
| **Franz approved** | ☑ date: 2026-09-13 (Round A, verdict approve) |
| **Manual checks (Prompts 1–3)** | ☑ date: 2026-09-14 · Franz, dev PC, `--fake-scanner`, branch `phase-21-scan-viewer-delete` at `a7100be`: 14 of 14 pass. Covered the Delete key on Search vs Groups; the viewer from Scan (double-click, Enter) and from Groups (grid follows); staged delete (count confirmation, labels close up, files in the Recycle Bin and one restored, button disabled when nothing is selected or while scanning, Save to group takes only the kept pages, relaunch recovery skips deleted pages). Not yet walked on the station. |
| **Code review (Prompt 5)** | ☑ date: 2026-09-14 · cold review of `main...HEAD`. Fixed, each with a failing test first where one could be written: the discarder refuses `*` and `?` in a path, which the shell would expand to every page (`dfa5082`); Delete is unavailable while a save is in flight, and an unwritable recovery index no longer closes the app (`399c8de`); the Recycle Bin call is owned by the active window (`5f24e7a`); the two real-shell discarder tests fail after 30 s instead of hanging (`31c4291`). After `/code-review max`: a failed index write leaves the session's pages in place, so exit cannot delete scans still on screen, and overlapping saves keep Delete off (`dc3ca57`); page keys route by the section on screen, not the nav selection (`3be6990`); the discarder test keeps a worker-thread exception inside its test and no longer names its files like scans (`df259c3`); the Scan selection follows the viewer, so Delete removes the page last looked at (`28e13f5`). |
| **Review — pre-existing, declined** | Scan and Save to group can both start while a save is in flight, including the auto-save after "Scan into this group" or a note-sheet capture. Pages scanned during the save are wiped by its `Pages.Clear()` and `ResetSession()`. Two overlapping `ScanAsync` runs share `_scanCts`, so the later `finally` throws. Two concurrent saves can give two documents the same `sequence`, which the importer refuses. All of this is on `main` too, and §03 rules out changing Save to group and auto-save here. **Needs its own fix soon.** |
| **Review — spec decision kept** | A page the Recycle Bin refuses, or whose permanent-delete question gets "No", still leaves the list and the index (§08) and is removed at the next session reset. Revisit if the station's bin refuses files. |
| **Review — docs follow-up** | Restoring a deleted staged page from the Recycle Bin puts it back in the unlisted session folder, which the next Save to group or exit deletes. The manual check passed only because the file reappeared. Prompt 6's user guide must say to copy a restored page out before saving. Re-importing restored pages would be a feature. |
| **Review — declined** | A "Yes" to Windows' permanent-delete question still reports "in the Recycle Bin". `SHFileOperationW` cannot tell recycled from destroyed, and this happens only where the bin refuses files. |
| **Review — declined** | A corrupt image with a damaged header throws `FileFormatException`, which neither `ThumbnailConverter` nor the viewer's `LoadFullImage` catches, so the app closes. Pre-existing on `main`, where the thumbnail hits it first. Needs a separate fix to both. |
| **Review — declined** | Page keys other than Delete, plus Scan annotated, Save to group and Commit, still act from every section. §03 and AC-10 keep this, and Commit is raised with Franz separately (§04). A Scan shortcut rebound to bare Enter cannot fire while focus is in the thumbnail list. |
| **Review — manual check to add** | After deleting the focused thumbnail, the list may lose its focus anchor, so Down goes to Page 1. Not reproduced; add it to Prompt 6's manual block. |
| **Review — spec revision** | `FOF_WANTNUKEWARNING` stays, although §08 does not list it. Where the Recycle Bin cannot take a file, Windows asks before a permanent delete; without it the file would go silently while the status line said "in the Recycle Bin". Known cost: one question per page when the bin refuses every file. A "No" leaves the file unlisted in the session folder, and the next session reset removes it anyway. |
| **Review — declined** | §12 "200 pages … with status progress": the discards run synchronously on the UI thread, so a progress line could not render. There is one status line at the end. Revisit if a large delete is noticeably slow on the station. |
| **Review — declined** | §12 "Viewer open while pages change — modal, so cannot happen" is not strictly true: a scan or save already in flight keeps running under the modal viewer. The viewer holds a snapshot of the paths and shows a file that has gone as blank, so no code change. |
| **Review — declined** | A junction or symlink created inside the session folder passes the path guard. That is outside §13's threat model: a corrupt index cannot create one, and anyone who can already has the operator's file rights. |
| **Review — declined** | Every bound shortcut now marks its key handled even when its command cannot run. The Scan and Save keys used to pass through when disabled. No effect with the default gestures, which all carry a modifier or are Delete/F-keys. |
| **AC-11 — plan diff** | ☑ date: 2026-09-14 · Franz confirmed the Quick Scan plan amendment (`a7100be`) matches his Round A answers. |
| **Jim's walkthrough — decision** | 2026-09-14, Franz: failed scans (a double-feed, or a jammed, torn or cut-off picture) **may be deleted before saving**, and the sheet is then scanned again. Step 6.9 of `evidence-setup-walkthrough.txt` is reworded, replacing its old "do not delete pages" rule. Scan everything and never delete because of what is on the paper (Step 6.8) still apply, as does no deleting after save. Part 9 gains "deleted a scan and never rescanned the sheet". |
| **Built** | ☐ date: |
| **Verified in production** | ☐ date: |
