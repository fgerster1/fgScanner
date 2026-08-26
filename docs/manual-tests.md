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
- [x] eSCL: `M6550NW series (192.168.0.114)` discovered over the network. 2026-08-24

## Scanning
- [ ] WIA flatbed scan at 300 DPI Color → one page thumbnail, file in %APPDATA%\FGScanner\recovery\<session>\
      — blocked, see above
- [x] Feeder scan with 3+ pages — 3 pages captured twice via CLI, exit 0, all 2480x3507 px.
      **The "thumbnails stream in one at a time" half is GUI-only and still untested.** 2026-08-24
- [ ] Duplex scan (if hardware supports) → front/back pages in order
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
- [ ] After force-killing FgScanner.exe, verify no NAPS2.Worker.exe processes linger
      (observed once during phase 1 when killing seconds after startup — likely a race
      before Job-object assignment; workers are normally tied to the parent's lifetime).

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

---

## Findings from the 2026-08-24 pass

Hardware: Pantum M6550NW (TWAIN sources + eSCL at 192.168.0.114). Driven via `fgscanner.exe`.
What passed is ticked above; what follows is what the pass *found*.

### BUG-1 — first TWAIN page carries 96 DPI metadata on 300 DPI pixels

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
`--fake-scanner` startup, and everything in the phase 4-10 sections.
