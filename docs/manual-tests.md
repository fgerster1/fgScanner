# Manual hardware smoke tests

Run before each release, and after any change to FgScanner.Scanning. Automated tests cover all logic with FakeScanService; this checklist covers what only real hardware can prove.

## Setup
- [ ] Launch `FgScanner.exe` (real drivers) — app starts, Scan section visible
- [ ] Launch `FgScanner.exe --fake-scanner` — 3 fake devices listed, scan produces pages

> **Partial pass 2026-08-24** on a Pantum M6550NW (TWAIN + eSCL; no WIA driver installed).
> Driven headlessly through `fgscanner.exe`, so every GUI-only row below is still open.
> Findings: **BUG-1** (first TWAIN page gets 96 DPI metadata) and two gaps, **GAP-1**/**GAP-2**
> — see "Findings from the 2026-08-24 pass" at the bottom of this file.

## Device discovery
- [x] ~~WIA~~ **BLOCKED, not a failure** — `WIA.DeviceManager` reports 0 devices on this machine
      (only a webcam in the PnP Image class), so our empty WIA list is correct. Needs a USB
      scanner with a WIA driver to actually exercise.
- [x] TWAIN: 8 Pantum sources enumerated via `fgscanner list-devices --driver twain`;
      two 32-bit `NAPS2.Worker` processes spawned and exited cleanly afterwards. 2026-08-24
      · **re-verified 2026-08-27**, same 8 sources, workers again exited cleanly.
- [x] eSCL: `M6550NW series (192.168.0.114)` discovered over the network. 2026-08-24
      · **re-verified 2026-08-27**, same device and address.

## Scanning
- [ ] WIA flatbed scan at 300 DPI Color → one page thumbnail, file in %APPDATA%\FGScanner\recovery\<session>\
      — blocked, see above
- [x] TWAIN flatbed scan at 300 DPI Color — one page, 2480x3507 px, **JFIF density 300x300**.
      This is the BUG-1 regression case and it now passes; see the 2026-08-27 findings. 2026-08-27
- [x] Feeder scan with 3+ pages — 3 pages captured twice via CLI, exit 0, all 2480x3507 px.
      **The "thumbnails stream in one at a time" half is GUI-only and still untested.** 2026-08-24
- [ ] Duplex scan (if hardware supports) → front/back pages in order.
      **Superseded by the SPEC-2026-006 section at the end of this file**, which covers one-pass
      duplex and the two-pass flow separately. Row 1 there is this row, and is still open.
- [~] BlackWhite bit depth + 150 DPI — eSCL produced a correct 1275x1650 px @ 150 DPI file, but the
      sheet on the glass was blank so "still legible" is **inconclusive**. Re-run over real text.
- [ ] Cancel mid-feeder-run → already-scanned pages remain, status shows canceled
- [ ] Empty feeder → error surfaces in status text, app stays responsive
      — attempted twice, feeder still had paper both times; not yet exercised

## Crash recovery
- [ ] Start a feeder scan, kill FgScanner.exe from Task Manager mid-scan
- [ ] Relaunch → recovery prompt shows correct page count → Yes → pages appear in list
- [ ] Repeat, answer No → pages discarded, no prompt on next launch
- [ ] Clean exit → no recovery prompt on next launch

## TWAIN specifics
- [ ] TWAIN scan works with a 32-bit-only vendor driver (e.g. older Canon/HP)
- [ ] Unplugging device mid-scan → error in status, no app crash

## Known issues to watch
- [~] After force-killing FgScanner.exe, verify no NAPS2.Worker.exe processes linger
      (observed once during phase 1 when killing seconds after startup — likely a race
      before Job-object assignment; workers are normally tied to the parent's lifetime).
      **2026-08-27, partial:** not reproduced on a *clean* path — the CLI TWAIN runs spawned
      workers that exited on their own. One live `NAPS2.Worker.exe` was found, but its parent
      `FgScanner.exe` was alive, so it was legitimate. **The force-kill half is untested**,
      because it needs a GUI scan to kill mid-run.

## Phase 4 — editing & export (manual checks)

- [x] Rotate/flip/deskew a page in Groups: thumbnail refreshes, file on disk changes, checksum updates.
      **Verified 2026-08-24** on the twain-feeder group, all 3 pages rotated via the toolbar button:
      thumbnails refreshed (observed); all 3 JPEGs changed on disk (e.g. scan_00002 970754 →
      927253 bytes, SHA-256 DE4DE23A… → 8BBFB0BC…); 6 undo snapshots written (3 pages ×
      before/after). Checksum refresh proven behaviourally — a copy of a *rotated* page under a new
      filename came back `1 duplicate(s) skipped`, which only happens if the stored checksum matches
      the post-rotation content. This is the path auto-orientation depends on (docs/scope-auto-orientation.md).
      Two notes: index.csv/manifest.json were not re-exported because the group is not in Committed
      state (by design — `ApplyEditsAsync` re-exports only when `Group.State == GroupState.Committed`,
      and rotation changes no index column anyway); and the `.md` OCR sidecars are now stale, since a
      manual rotate does not re-OCR — which is exactly why the auto-orient design re-OCRs after rotating.
- [ ] Undo (Ctrl+Z) and redo (Ctrl+Y) an edit and a reorder; verify committed groups re-export after each.
- [ ] Export PDF with PDF/A-2b + encryption; open in Adobe/Edge: metadata present, password required, printing restricted per flags.
- [ ] Export multi-page TIFF; open in an image viewer and page through frames.
- [ ] Import a password-protected PDF: password prompt appears, pages land in the group grid.
- [ ] Print… sends pages to a real printer / Microsoft Print to PDF at full page size.
- [ ] Copy puts the page image on the clipboard (paste into Paint).
- [ ] Drag the preview thumbnail into Explorer: file copy lands.

## Phase 5 — OCR (manual checks)

- [ ] Scan into a group with an OCR-enabled profile: pages go Pending → Yes automatically; .md sidecars appear beside images with YAML front matter.
- [ ] Kill the app mid-OCR (Task Manager), restart: queued pages resume without re-doing finished ones.
- [ ] Export PDF with OCR from a text page; open in a viewer: text is selectable and aligned over the ink.
- [ ] "Re-OCR all": previous .md files appear in Trash and are restorable.
- [ ] Low-quality page (crumpled/skewed photo) shows "Yes ⚠ nn% — review" in the grid.
- [ ] Settings → download German; OCR a German page with languages "eng+deu".

## Phase 6 — AI descriptions (manual checks)

- [ ] Settings → paste a PAID-tier AI Studio key: privacy notice appears once, key validates, "AI describe" button appears in Groups.
- [ ] Smoke run (≤$0.01): AI-describe a 2–3 page group; estimate dialog shows ≈$0.0006/page; descriptions land in the grid, AIDescription column fills in the re-exported index files; compare actual spend in Settings against the estimate (should be within 20%).
- [ ] Pull the network cable mid-run: remaining pages stay Pending; reconnect + restart resumes without re-billing finished pages.
- [ ] A blank page (OCRed, <5 words) shows Skipped / "BLANK PAGE" with no API call (spend unchanged).
- [ ] Windows Credential Manager shows "FGScanner:GeminiApiKey"; "Clear stored key" removes it and hides the AI button.

## Phase 7 — retro-processing (manual checks)

- [ ] "Process existing folder…" on a folder of old photos + a PDF: images keep their names, PDF pages appear as <name>_page_NNN.png, report matches reality.
- [ ] Run it again immediately: report shows nothing adopted, grid unchanged (idempotence).
- [ ] Rename an image in Explorer, hit Reconcile: row re-matches by checksum, field values intact.
- [ ] Delete an image in Explorer, Reconcile: offered removal moves the row to Trash, restorable.
- [ ] A folder with someone else's index.csv: warning appears; commit only replaces after the warning.
- [ ] "Re-process…" with "Redo everything": .md files land in Trash, OCR/AI redo (AI shows estimate first).

## Phase 8 — batch & CLI (manual checks)

- [ ] Batch scan (multiple-with-prompt, 3 passes) on the real feeder; pages accumulate and auto-save to the active group.
- [ ] `fgscanner scan --group C:\Scans\Test --source feeder` from a real scanner in Task Scheduler; index appears with `fgscanner process C:\Scans\Test --ocr --write-index`.
- [ ] `fgscanner list-devices` shows the real scanner.
- [ ] Rebind Scan to F5 in Settings, save: F5 scans immediately, Ctrl+Enter no longer does; reset restores defaults.
- [ ] F2 selects the first profile.
- [ ] Export a profile, re-import it: "Name (2)" appears with identical fields/formats.
- [ ] Launch the app twice: the second launch focuses the existing window; closing and reopening restores the last section and group.

## Phase 9 — ship it (manual checks, per release)

- [ ] Clean Win11 VM: download installer → SmartScreen "Run anyway" path works as documented → install → scan → commit → index.csv correct.
- [ ] Upgrade install over the previous version: groups, database, settings, and stored AI key survive; stale binaries purged.
- [ ] Installer privacy page shows; ticking the AI opt-out hides the AI pane and button after install.
- [ ] "Open with FG Scanner" on a JPG/PDF: appears in Open-with list; file imports into the group you open.
- [ ] Scanner hardware button / AutoPlay offers "Scan with FG Scanner".
- [ ] First-run wizard on a fresh profile: theme choice applies (incl. dark), custom profile created.
- [ ] Portable ZIP: runs from an extracted folder without install.
- [ ] After keys exist (docs/release.md): publish a test release, old build offers the update, /VERYSILENT upgrade succeeds.
- [x] Local installer build: `winget install JRSoftware.InnoSetup.7` (per-user, no admin needed), then
      ISCC per CLAUDE.md. Verified 2026-08-24 on 7.1.0 → `dist\fgscanner-0.1.0-win-x64.exe`, 91.6 MB.

## Phase 10 — differentiators (manual checks)

- [ ] Settings → Features: enable Patch-T; Scan section shows "Separator sheet…" — save the PDF, print it, scan a stack with the sheet between documents on a profile with detection on: the sheet is dropped and journal.txt records it; with "Keep separator pages" it stays.
- [ ] Blank-page policy on a real feeder scan: Drop removes the empty back sides (journal.txt lists them), Flag keeps them visible as "Blank — excluded" and out of index.csv/OCR/AI.
- [ ] Search: after OCR, find a word from a scanned page; the snippet highlights it; double-click opens the group with the page selected. Field values and AI descriptions are also found. Turn the feature off: section is gone on next launch.
- [ ] Commit hook: set command `echo %date% >> committed.txt` and a webhook (e.g. webhook.site); commit a group: file appears in the group folder, webhook receives the index.json payload, journal.txt records both.

## Phase 19 — batch row metadata (manual checks)

- [ ] Settings → "Build the Evidence profile", then create a new group on that profile: a "Batch
      values" panel appears above the entry grid offering `Box` and `Operator`; `Operator` is
      pre-filled with the current Windows username.
- [ ] Type a value into `Box` in the Batch values panel: every row already in the grid updates its
      `Box` cell to that value; scan or add a page afterwards and it carries the same value.
- [ ] In the entry grid, try to edit a cell in the `Box` or `Operator` column: the cell refuses
      editing — the Batch values panel is the only place those two are set.

---

## Findings from the 2026-08-24 pass

Hardware: Pantum M6550NW (TWAIN sources + eSCL at 192.168.0.114). Driven via `fgscanner.exe`.
What passed is ticked above; what follows is what the pass *found*.

### BUG-1 — first TWAIN page carries 96 DPI metadata on 300 DPI pixels — FIXED 2026-08-25, VERIFIED ON HARDWARE 2026-08-27

**Verification (2026-08-27):** the fix landed in `8ffcb3c` *"Stamp scan DPI when the driver reports
none (first TWAIN page)"*. Re-ran the exact failing case — a **TWAIN flatbed** scan at 300 DPI,
which is page 1 of a run and so always hit the bug. The saved JPEG now carries **JFIF units=1,
Xdensity=300, Ydensity=300** on 2480x3507 px. Was 96. Closed.

The original report follows, for the record.

Reproduced 3/3, and TWAIN-specific:

| Scan | page 1 | pages 2-3 |
|---|---|---|
| TWAIN feeder (batch 1) | **96 dpi** | 300, 300 |
| TWAIN flatbed | **96 dpi** | — |
| TWAIN feeder (batch 2) | **96 dpi** | 300, 300 |
| eSCL flatbed @150 | 150 dpi ✅ | — |

Every page is 2480x3507 px — genuinely 300 DPI pixel data — so only the metadata is wrong. eSCL is
clean, so this is not a universal save-path defect.

Cause: `Naps2ScanService.ScanAsync` does a bare `image.Save(path)` (Naps2ScanService.cs:61). We pass
`Dpi = options.Dpi` down to the driver but never check what comes back, so whatever resolution the
TWAIN bridge reports on the first image (GDI's 96 default) lands in the JPEG.

Impact: PLAN §5.5 feeds Tesseract `--dpi` from scan metadata and depends on it for text-layer
alignment — this is the NAPS2 #843 bug class the plan explicitly set out to regression-test. A PDF
built from that page would also be sized 25.8" wide instead of 8.27".

Fix has a judgment call in it — decide before implementing:
- stamp `options.Dpi` unconditionally (simple; mislabels scanners that clamp to a nearby DPI), or
- stamp only when the returned resolution looks unset/default (safer; "unset" vs "genuinely 96" is
  ambiguous).
Either way add a regression test asserting saved-image DPI == requested DPI for page 1 of a run.

### GAP-1 — no page-orientation detection — FIXED 2026-08-25

**Resolved** by the auto-orientation work: `osd.traineddata` now ships, `OcrPipeline` runs an OSD
pass per page and rotates the stored image to upright before recognition. See
`docs/adr/0002-auto-orient-every-angle.md` — the scoped "rotate 180 only" decision was overturned
by measurement, because exactly one of the two sideways directions reads correctly and the other is
indistinguishable from an inverted page.

Original report:


The test sheets went through the ADF 180° rotated. Capture quality was excellent, but OCR returned
reversed text (`smopulM\:D` = `C:\Windows` backwards) at 21-40% mean confidence, and nothing in the
pipeline noticed. `TesseractRunner` hardcodes `--psm 3` with no OSD pass (`--psm 0` detects
orientation), and there is no auto-rotate on the capture path.

Upside-down paper is user error, but silently producing garbage is a product gap: most scanning apps
auto-rotate. Cheap first step: an OSD pass when mean confidence lands below
`OcrPipeline.LowConfidenceThreshold`, then re-OCR at the detected orientation.

### GAP-2 — index.csv hides low-confidence OCR — FIXED 2026-08-25

**Resolved**: every export format now carries an `OCRConfidence` column beside `OCRed` (empty when
the page was never read, never 0). The XSD, the manifest and the Verify snapshots were updated with
it, and the XLSX cell is a real number so "confidence < 70" is one Excel filter.

Original report:


All three pages scored 40.63 / 21.85 / 29.7 mean confidence — every one below
`OcrPipeline.LowConfidenceThreshold` (65), i.e. all should be flagged for review. `index.csv` records
a flat `OCRed=Yes` for each:

```
Group,ImageName,OCRed,AIDescription,AIStatus
twain-feeder,scan_00001.jpg,Yes,,Off
```

The threshold exists and the GUI grid shows "Yes ⚠ nn% — review", but a headless consumer of the CSV
cannot tell clean OCR from 21%-confidence garbage. Consider a confidence column, or an
`OCRed=Review` value. Needs a decision: it is an index-schema change (PLAN §5.2 fixes column order),
so it affects the XSD, the manifest, and the Verify snapshots.

### Still requiring a human at the GUI

Thumbnail streaming, cancel mid-run, the crash-recovery prompt, duplex, empty-feeder error surfacing,
`--fake-scanner` startup, and everything in the phase 4-10 sections. **Duplex is now tracked in the
SPEC-2026-006 section at the end of this file**, where the two-pass rows have been rehearsed on the
fake scanner and the hardware rows are listed as owed.

## Findings from the 2026-08-27 pass (pre-hand-off, on the Pantum M6550NW)

Driven headlessly through `fgscanner.exe` again, so every GUI-only row above is still open. The
point of this pass was the **evidence path**, ahead of handing the USB stick to the scanning
station. No new defects.

**Use the right binary.** `src/FgScanner.Cli/bin/Release/net10.0-windows/fgscanner.exe` is the
current one (0.3.2). The sibling `win-x64/` directory holds a **stale 0.1.0 publish from
2026-08-24**; running it produced a `manifest.json` stamped `"appVersion": "0.1.0"` and briefly
looked like a phase-18 regression. It is not one — re-running with the 0.3.2 binary produced
`"appVersion": "0.3.2"`. Worth deleting that stale publish dir so it cannot mislead again.

### Verified

- **BUG-1 closed on hardware** — see above.
- **Phase 16 / 18 output on a real capture** — `manifest.json` from a genuinely scanned page carries
  `"evidenceExport": 1` and `"appVersion": "0.3.2"`.
- **The JimsStuff importer accepts FG Scanner's export shape.** This had never been run end to end.
  A folder holding the real TWAIN capture plus an `index.json` in the 0.3.2 export shape — the
  Phase-16 row keys and all nine Evidence field names — was passed to the actual
  `JimsStuff/pipeline/import_fgscanner.py --dry-run`. It parsed the row, **re-verified the SHA-256
  against the image bytes on disk**, resolved one document, and emitted the `batches.jsonl` line and
  the `SCAN0001 <- scan_00001.jpg` mapping. **Zero warnings.** The contract in CLAUDE.md is real and
  currently satisfied.
- **The typo failure mode is exactly as documented.** Re-running with `Operator` misspelled
  `Opperator` and `Box` as `Bx` produced `warning: 1 sheet(s) have no Operator field value` (and the
  same for Box) — **and then imported anyway**, with those columns empty. It warns; it does not
  fail. That is why the walkthrough's closing read-back step exists, and why a typo caught after
  4,000 sheets is expensive. Note the importer takes the batch name from `manifest.json`'s `group`,
  not from the folder name.

### Setup state on this machine (not a defect — but read before testing the evidence path here)

- **There is no `Evidence` profile on this dev machine.** Profiles are `Default`, `JimsStuff`, `s`.
  The `JimsStuff` profile carries four fields (`Came From`, `Recieved`, `Original Yes No`,
  `If Not Original Where is Original`) — **not** the nine contract names. The nine-field Evidence
  profile that `build/installer/evidence-setup-walkthrough.txt` describes has never actually been
  built and exercised by anyone.
- **All three profiles have JSON export OFF** (`ExportJson = 0`), so none of them currently writes
  an `index.json` at all. The walkthrough is right to call this out as "the single most important
  tick on this page" — it is off by default and the importer reads that file.
- `Feature.PreserveOriginals = true` is set, as ADR-0003 requires for evidence groups.
  `Feature.AutoOrient` has no row in `Settings`, which is correct — it defaults on when absent.

### Still open on this pass — all need a human at the scanner

Feeder 3+ pages with thumbnail streaming · duplex (see the SPEC-2026-006 section) · cancel mid-run ·
empty-feeder error ·
BlackWhite 150 DPI over real text (still the `[~]` row) · all four crash-recovery rows ·
32-bit-only vendor TWAIN driver · unplug mid-scan · WIA (no WIA device on this machine).

**And the one that matters most before hand-off:** walk
`build/installer/evidence-setup-walkthrough.txt` top to bottom in the GUI, building the Evidence
profile from scratch, and confirm its own closing test-group check passes. That validates the
document Jim will actually follow — the contract itself is now proven, but the path a human takes
to produce it is not.

## Fit and small screens (SPEC-2026-001)

The smallest supported screen is **1280 × 1024**. To check it on a bigger monitor: Windows Settings →
System → Display → set **Display resolution** to 1280 × 1024 and **Scale** to 100%, reopen FG Scanner,
and walk the list; then repeat at **125%**. Put your own resolution and scale back afterwards.

**Fit and zoom**
- [ ] Groups: select a page scanned at 300 DPI — the preview shows the whole page and the zoom label reads well above 21% (AC-8)
- [ ] Drag the preview's dividers — the page keeps fitting; press + and drag again — your zoom stays; press Fit — it fits again
- [ ] Double-click the preview — the viewer shows the whole page and Close is visible; press 100% — the page is actual paper size (hold a sheet up to the screen)
- [ ] A narrow scan (a receipt) after Fit looks sharp, not blown up
- [ ] Ctrl+wheel zooms the preview; the plain wheel scrolls it (AC-12)
- [ ] With focus in the Groups grid, Ctrl+Shift+Left/Right still rotates the page

**Sections** — make the window about 800 × 600, then maximize it
- [ ] Scan, Groups, Search, Trash, Settings: every button reachable by scrolling when small (AC-9); no section scroll bars when maximized
- [ ] A group of 1,000+ pages scrolls smoothly; a Scan session with many pages stays responsive (AC-11)
- [ ] Groups: the value panels and toolbars stop at under half the height and scroll on their own; commit errors and the status line stay visible below them
- [ ] Drag the window edge slowly across its minimum size — the scroll bars do not flicker
- [ ] Switching groups opens the new one scrolled to the top; switching sections back keeps each section's own scroll position
- [ ] The app opens fully on the monitor under the mouse and cannot be dragged smaller than about 800 × 560
- [ ] Widen the preview, narrow the window (the grid keeps its room), widen the window (the preview returns to your width); restart — the width is remembered

**Dialogs** — drag the main window near the bottom of the screen first (AC-10)
- [ ] Batch scan…, Adjust…, Export images…, Export PDF…, Re-process…, Delete group…, Move all scans…, a name prompt (Create…), Duplicates…, and the page viewer each open fully on screen with their buttons visible
- [ ] In each, Tab reaches the fields before the buttons, and clicking a caption does not take focus out of the field being typed in
- [ ] First run (a fresh profile folder): the Welcome dialog fits the screen with "Start scanning" visible

## Scan page review (SPEC-2026-003)

Run with `FgScanner.exe --fake-scanner`. Set Source to Feeder so one Scan gives several pages.

> **Passed 2026-09-14 on the dev PC** (Franz, 14 of 14, before the code-review fixes). The rows marked
> *(review)* were added by the code review and have not been walked yet. Nothing here has been walked
> on the station.

**Page keys act only on the screen showing**
- [ ] With a group open in Groups and a page selected, go to Search, click in the results list, and press Delete: nothing is deleted
- [ ] Do the same on Trash and Settings, and try Ctrl+Z, Ctrl+Y and Ctrl+Shift+←/→ too: nothing changes in the group
- [ ] Back on Groups, Delete moves the selected page to the Trash as before; Ctrl+Shift+← rotates it

**The viewer**
- [ ] Scan 5 pages and double-click the third: the viewer shows "Page 3 of 5", and the arrow keys page through
- [ ] Click a thumbnail and press Enter: the viewer opens on that page
- [ ] *(review)* Open page 2, page to page 4, close: page 4 is the one selected and focused, and Delete would remove page 4
- [ ] In Groups, double-click the preview, page forward and close: the grid lands on the page you closed on

**Delete before saving**
- [ ] Ctrl+click 2 thumbnails and press Delete: the confirmation names 2 pages, with Cancel as the default; press OK and the rest renumber Page 1–3
- [ ] Press Cancel instead: nothing changes
- [ ] The deleted `page-0000N.png` files are in the Windows Recycle Bin
- [ ] "Delete selected…" is greyed out with nothing selected, while a scan runs, and *(review)* while Save to group is running (use "Scan into this group", which saves automatically)
- [ ] *(review)* Delete the focused thumbnail, then press Down: focus moves to the next page, not back to Page 1
- [ ] Select a group, then Save to group: the group gets only the kept pages
- [ ] Scan 3 more, delete 1, close FG Scanner without saving, and relaunch: recovery offers 2 pages
- [ ] *(review)* Restore a deleted page from the Recycle Bin, then Save to group: the restored file is **not** in the group, and it is gone from the session folder. This is expected; the user guide says to copy it out first.

## Record editor (SPEC-2026-002)

> **Passed 2026-09-16 on the dev PC** (Franz, "nothing failed"), on an Evidence group, on the build
> at `3f907f9` — that is, **before** the code-review fixes. The rows marked *(review)* were added by
> the code review afterwards and have not been walked. Nothing here has been walked on the station.

**Settings — length and memo**
- [ ] Settings → custom fields: the label reads "up to 16", and there are **Length** and **Memo** columns
- [ ] Give a Text field Length 10, tick Memo on a second Text field, save — a new field layout version is made
- [ ] A Date field's Length box is disabled
- [ ] Length 0, or 101 on an ordinary Text field, or 2001 on a memo: refused, and the message names the field

**The Groups grid**
- [ ] Open a group on that profile → "Use latest field layout"
- [ ] Typing an 11th character into the length-10 field is refused
- [ ] Pasting 20 characters into it is refused, with a message, and the cell keeps what it had
- [ ] The memo column shows one line, trimmed with "…" — the value itself is untouched (widen the column to check)

**The editor window** — select a page, press "Record editor…" on the Groups toolbar
- [ ] All 13 Evidence fields can be read in one view, with scrolling at most
- [ ] Drag the divider between the fields and the page, and the one above the page list — both panes resize
- [ ] Drag a memo box's corner grip: it will not go wider than its pane, and stops between 2 and 20 lines tall
- [ ] Close and reopen: the sizes come back. Open a different group's editor: it keeps its own sizes
- [ ] Ctrl+PageDown and Ctrl+PageUp: the fields, the page and the list all move together
- [ ] Paste something too long into a length-limited field: refused, with the reason on the status line at the bottom
- [ ] Type a value in the form — the same cell in the list below shows it; close and reopen — it is still there
- [ ] Click into a text field, type, and press **Delete**: it deletes a character. The page must **not** be deleted
- [ ] Tab from the fields reaches the zoom buttons, then the page list
- [ ] −, +, Fit and 100% work on the page and the zoom percentage updates
- [ ] A page whose image file has been renamed on disk shows "Image file not found" with the path, and its fields still open
- [ ] *(review)* A List field whose stored value is **not** one of today's choices: open the editor, close it, and the value is still there (check the Groups grid, or `index.json` on a committed group)
- [ ] *(review)* With focus on a List field in the form, press **Delete**: the page must **not** be deleted
- [ ] *(review)* Drag a divider immediately after opening, before the window has settled: your drag stays put

**Delete, add and close** — on a committed group
- [ ] Delete a page: it is in the Trash, `index.json` no longer lists it, and the editor moves to the next page
- [ ] Import 2 images: the editor stays on the page you were editing, and the new pages appear in the list
- [ ] Close: the Groups grid is on that same page
- [ ] On a scratch group, delete every page: the editor stays open, says "No pages in this group yet", and Delete greys out

**Also walked 2026-09-16** (Franz, confirmed after the code review)
- [x] The open, resize and reopen checks on a group under a **non-Evidence** profile that has a memo field (AC-15)
- [x] The same at **1280 × 1024**, the smallest supported screen (AC-15)

## Settings take effect without a restart (SPEC-2026-004)

Twelve settings used to need the program closed and reopened; two were worse than that. Every row
below is checked **without relaunching**. Walk them after an upgrade on each station — the last
two rows in particular are window chrome that no automated test can reach.

**Walked 2026-09-20** (Franz, dev station, against the copy of Jim's data at `D:\Evidence-Scans`)

- [x] Trash retention shows the **stored** value (365 on Jim's data), not 30
- [x] Theme combo exists under Appearance, and changing it applies on save without a relaunch
- [x] Full-text search section appears and disappears as the checkbox is toggled and saved
- [x] A profile built in Settings is in the Groups profile list straight away
- [x] Editing a profile's fields raises the "Use latest field layout" banner on the group already open
- [x] Values typed for the next scan survive a settings save

Still open — these need a second pass, and the starred ones were never exercised by hand:

- [ ] Rename a profile: the new name shows in Groups
- [ ] Delete a profile: it stops being selectable in Groups
- [ ] Import a `.fgprofile`: it appears in Groups
- [ ] Set a base folder in Settings, then create a group: it lands in the new folder without asking
- [ ] Patch-T off and on: the "Separator sheet…" button follows, without a relaunch
- [ ] *(regression)* Save Settings with **"Only this profile's groups"** ticked and a group open with
      typed values — the values must survive. This is the configuration that broke twice.
- [ ] *(regression)* Toggle the search section while **standing in Settings** — the app must not
      navigate away or crash. WPF writes the cleared selection back; no headless test sees it.
- [ ] *(regression)* Change the retention, save, and read the status line — it must report how many
      trash items the purge removed
- [ ] Save Settings **during a feeder run**, and again **with an annotated sheet in hand**: the change
      is held, and lands when the run finishes or the sheet is completed or abandoned
- [ ] Kill the app during a deferred change: nothing is left half-applied on the next launch

## SPEC-2026-005 — memo, wrapping and field widths (phase 24)

Every row here needs a real window: WPF's layout, its write-back into a bound selection, and the
focus walk are all invisible to a headless suite. That blind spot is exactly what let the Settings
Type list ship empty (see below), so none of these is ticked from a green test run.

**Walked 2026-09-21** (Franz, dev station, against the copy of Jim's data at `D:\Evidence-Scans`)

- [x] AC-1 — a long value is readable without resizing anything: open the **Defamation Folder**
      group's 1,398-character `Notes` in the record editor. It wraps, grows, and does not need a
      sideways scrollbar. (Confirmed after two failed attempts; the first two fixes reached only
      the memo box, and the group is pinned to field layout v2 where `Notes` is plain Text.)
- [x] AC-6 — **Memo** appears in the Settings field grid's **Type** list, and the separate Memo
      checkbox is gone. A field already stored as a memo reads back as Memo when Settings reopens.
- [x] AC-5 — a list field's dropdown is no longer the full width of the form.

Still open — these need a pass on the real window:

- [ ] AC-4 — a list field is about as wide as its **longest** choice. **Known to fail:** the box
      tracks the *currently selected* choice, so a list with nothing chosen is a narrow stub and
      the box changes width as you page through rows with Ctrl+PageDown. Recorded rather than
      ticked; a real fix has to measure the choices.
- [ ] *(regression)* Change a field's **Type** in Settings and save — the value must stick. The
      column's items and its selection binding were different types for one build, so every Type
      cell rendered blank and no edit could be written. No test can see this.
- [ ] *(regression)* Switch a Memo field with a 2000-character length to **Text** and save. The
      length must clear, and the save must complete — a length plain text cannot hold used to
      throw before theme, retention, flags and the shortcut map were written.
- [ ] *(regression)* Tab to the **last** field in the record editor and press **Enter**: focus must
      stay in the form. Past the last field it reached the toolbar, where **Delete** trashes the
      page instead of clearing a value.
- [ ] `DocNo` (a **Number** field, required) has a box to type in, in the record editor.
- [ ] A long value in the group grid ends in an ellipsis, not a hard clip at the cell edge —
      on a plain Text field, not only on a memo.
- [ ] Make a field name long enough to fill the form pane, then drag the divider to its minimum:
      the input must not vanish.

## SPEC-2026-006 — duplex capture (phase 25)

**§11.3 rehearsed on the fake scanner 2026-09-21** (Franz's station, the real scanner in use
elsewhere). The `--fake-scanner` switch grew options so the protocol can be walked without paper:
`--fake-pages=10`, `--fake-pages=10,9` (a sheet removed before the backs), `--fake-pages=5,4` (an
odd stack), `--fake-blank-backs`, `--fake-no-duplex`.

**A fake scanner is not a scanner.** It has no rotation, no jam, no double feed and no real feeder,
and it always reports what it was told to report. These rows prove the sequence, the wording and
the refusals; they prove nothing about any device. **Every row below is repeated on hardware, and
named with the scanner, before this spec is Done** — the dev station has an HP ENVY 7640 and Jim's
station is a different machine, so a row passed on one proves nothing about the other either.

- [x] **2 — a source the scanner cannot do** (`--fake-no-duplex`). "Feeder (both sides, one pass)"
      is disabled, and its reason is readable three ways: as text under the box, as the entry's
      accessibility name, and as a tooltip that shows on a disabled control. Exact wording:
      *"This scanner does not report feeder (both sides, one pass) support."* No driver error.
- [x] **3 — two-pass, 10 sheets** (`--fake-pages=10`). After the fronts: *"10 front(s) scanned.
      Turn the whole stack over, put it back in the feeder, and press "Scan the backs"."* The
      button changes to **Scan the backs**. After the backs: *"Both sides scanned — 10 front(s)
      and 10 back(s), paired into 20 page(s) in sheet order."* 20 pages in the session.
- [x] **4 — blank backs** (`--fake-pages=10 --fake-blank-backs`). 20 pages on disk, **1 distinct
      checksum between all of them** — a genuine stack of identical blanks — and all 20 paired.
      The "20, not 11" half is adoption, which is not exercised here because it would write into
      the live database; `DuplexScanTests.Identical_blank_backs_all_reach_the_group` proves it at
      exactly those numbers.
- [x] **5 — a sheet removed before the back pass** (`--fake-pages=10,9`). *"The two passes do not
      match: 10 front(s) and 9 back(s). Nothing has been paired. Scan the backs again, or save the
      pages as they are and put them in order in Groups."* 19 pages, left in capture order.
- [x] **6 — cancel mid-sequence**. *"Stack abandoned — 6 page(s) moved to the Recycle Bin."* The
      session folder holds 0 pages afterwards, the prompt and **Cancel stack** leave the screen,
      and **Scan** is enabled again.
- [x] **7 — odd stack, last sheet single-sided** (`--fake-pages=5,4`). Refused, not guessed:
      *"The two passes do not match: 5 front(s) and 4 back(s)…"* 9 pages, unpaired. This is §05
      Q1(a) working as chosen — it costs the operator a rescan rather than risking a wrong pairing.

**OPEN — hardware only, cannot be rehearsed:**

- [ ] **1 — hardware duplex on a duplex-capable scanner**: 3 double-sided sheets, checking order
      **and rotation**. If the backs come out upside down, tick "Turn the backs the right way up"
      and repeat. The fake has no physical sides, so nothing here has ever been exercised —
      `FlipDuplexedPages` reaching the driver is proven only by `ScanOptionMappingTests`.
- [ ] Re-run rows 2–7 on the dev station's HP ENVY 7640 with real paper.
- [ ] Re-run rows 1–7 on Jim's station, naming the scanner.
- [ ] A real feeder failure — a jam or a double feed part-way through a pass — and what the Scan
      page says. Only three NAPS2 exceptions are translated into plain words; a jam is not yet one
      of them, so it currently reads "Scan failed: …".

## SPEC-2026-007 — email (phase 26)

**Which route a station gets depends on what is installed on it**, so a send proves nothing about
a different kind of station. Name the station and its mail app on every row. The dev station has
**new Outlook only and no MAPI client** (all three probe values empty, checked 2026-09-22), so it
gets the Share sheet; a classic-Outlook station gets MAPI first; a station with neither gets Explorer.

**Phase 1 spike (AC-8)** — performed 2026-09-21, recorded in full in the spec's §22:

- [x] `dotnet build -c Release` green after the target-framework change — 0 warnings, 0 errors.
- [x] `dotnet test -c Release` green — 793 of 793, unchanged.
- [x] `dotnet publish -p:PublishProfile=win-x64` still non-single-file and non-trimmed — nine
      NAPS2 assemblies separate, 329 DLLs, `FgScanner.exe` 0.16 MB.
- [x] Installer built, installed and run — `fgscanner-0.5.1-win-x64.exe`, exit 0, 94.8 → 104.1 MB,
      the installed copy scanned on the fake scanner.

**The four sends:**

- [~] **A session as PDF.** 2026-09-22, dev station, `--fake-scanner`, active group "Defamation
      Folder": Email… → Continue opened the **Windows Share sheet** reading *"You are sharing
      Defamation Folder. 1 scanned page(s) from FG Scanner"* with `Defamation Folder.pdf` (3.5 KB)
      attached and Outlook for Windows among the targets; status *"1 page attached. Opened in your
      mail app."* (both since corrected: the sheet no longer counts files as pages, and the status no longer claims a message opened). The log read `Email: 1 page(s) from this scan as
      "Pdf" via "ShareSheet"`. **The sheet was dismissed with Escape — no target was chosen and
      nothing was sent**, so the rest of the row is open: pick Outlook, send to yourself, open the
      PDF.
- [ ] **Three selected pages as images** from a group: exactly those three arrive, in page order,
      as the original files — compare one attachment's SHA-256 with that page's `checksum` in the
      group's `index.json`.
- [ ] **New Outlook receives the attachments** through the Share sheet: pick Outlook, and the draft
      has the file attached (not an empty message).
- [ ] **The forced fallback**: on a station with no mail app, or with the Share sheet unavailable,
      the status reads *"… No mail app was found. The attachment is in … — attach it to your
      message yourself. It stays there until FG Scanner closes."* and Explorer opens with the file
      selected.

**Added by the code review (2026-09-22):**

- [ ] **Classic Outlook gets its own draft first** — on a station where the probe finds a MAPI
      client: the Outlook compose window opens over FG Scanner, not the Share sheet. Send it: the
      status reads *"… Your mail app reports the message as sent."* Repeat and close the draft
      instead: *"You closed the message without sending it — nothing left the app."*, and no Share
      sheet follows. **Never seen on hardware** — the dev station has no MAPI client.
- [~] **A comma in the path.** 2026-09-22, dev station, on the shell directly rather than through
      the app: for a file `…\comma-test\Smith,John.pdf`, the old argument form opened the Desktop
      with Documents selected; the new always-quoted form opened `comma-test` with
      `Smith,John.pdf` selected. Still to do through the app: a subject "Smith,John" on the
      fallback, and "Open containing folder" on a group whose folder name holds a comma.
- [ ] **The evidence warning** on a committed group on the Evidence profile: shown on the first
      send; tick "don't show this again" and press **Cancel** — the next send does not show it.
- [ ] **A crash leaves nothing behind**: send once, end FG Scanner from Task Manager, relaunch —
      `%TEMP%\FGScanner\email` is empty after startup. (Seen once 2026-09-22 on a harness-killed
      session: 17 folders before launch, 0 after.)

**Known and not fixed here:** at the 800×560 minimum window the Scan page's Email… button, like
**Scan** itself and every Groups toolbar button, is below the fold (§16 R7, app-wide).

**Webmail (added 2026-09-22)** — Franz sends from Gmail, Jim from Yahoo, both in a browser. Set
Settings → Email → "Send email with" on each station first.

- [ ] **Gmail on Franz's station.** A session as PDF: a new Gmail message opens in the browser
      with the subject filled in, and Explorer beside it with the PDF selected; drag it in, send
      to yourself, open the attachment. Check a subject with `&` and `,` arrives whole.
- [ ] **Yahoo Mail on Jim's station.** The same. **The Yahoo compose link is undocumented** — if
      the subject does not fill in, or the page is not a new message, record what Yahoo shows.
- [ ] **Three images to Gmail**: the status says 3 files are in the Explorer window; select all
      three and drag them in together.
- [ ] **The Share sheet on a mail-program station** now also names the file's folder.

