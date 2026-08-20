# Manual hardware smoke tests

Run before each release, and after any change to FgScanner.Scanning. Automated tests cover all logic with FakeScanService; this checklist covers what only real hardware can prove.

## Setup
- [ ] Launch `FgScanner.exe` (real drivers) — app starts, Scan section visible
- [ ] Launch `FgScanner.exe --fake-scanner` — 3 fake devices listed, scan produces pages

## Device discovery
- [ ] WIA: physical USB scanner appears in device list after Refresh
- [ ] TWAIN: same scanner appears under TWAIN driver (32-bit worker starts; check Task Manager for NAPS2.Worker.exe)
- [ ] eSCL: network MFP appears (same subnet, mDNS allowed through firewall)

## Scanning
- [ ] WIA flatbed scan at 300 DPI Color → one page thumbnail, file in %APPDATA%\FGScanner\recovery\<session>\
- [ ] Feeder scan with 3+ pages → thumbnails stream in one at a time
- [ ] Duplex scan (if hardware supports) → front/back pages in order
- [ ] BlackWhite bit depth + 150 DPI → smaller file, still legible
- [ ] Cancel mid-feeder-run → already-scanned pages remain, status shows canceled
- [ ] Empty feeder → error surfaces in status text, app stays responsive

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

- [ ] Rotate/flip/deskew a page in Groups: thumbnail refreshes, file on disk changes, checksum updates (re-scan of same original no longer flags duplicate).
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
