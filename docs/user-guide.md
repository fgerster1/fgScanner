# FG Scanner — User Guide

## The workflow in one paragraph

Create or open a **Group** (a folder on disk = a group), scan pages into it,
fill in your index fields in the grid (before or after scanning — "values for
the next scan" pre-fill incoming pages), then **Review & Commit**. Committing
validates required fields and writes `index.csv` (and, if enabled in the
profile, `.xlsx` / `.xml` / `.json`) plus `manifest.json` into the group
folder. Everything else — OCR, AI descriptions, PDF export — feeds that index.

## Scanning

- Pick a driver (WIA is the default; TWAIN for older drivers; eSCL for network
  scanners), a device, source (flatbed/feeder/duplex), DPI, bit depth, page size.
- **Batch scan…** runs several passes with a prompt or delay between them and
  saves to the active group at the end.
- If the app is killed mid-scan, the next start offers to recover the pages.

## Profiles and index fields (Settings)

A profile holds up to 12 typed fields — Text, Date (ISO), Number, List — each
optionally Required (blocks commit), Sticky (carries to the next page), or
defaulted (tokens: `$(today)`, `$(group)`, `$(counter)`, `$(user)`). Saving
field changes creates a new schema version; existing groups keep theirs.
Profiles also choose the export formats and the CSV delimiter, and can be
shared as `.fgprofile` files (Export/Import buttons).

## Editing pages (Groups section)

Select rows in the grid (Ctrl/Shift for several) and use the toolbar:
rotate ⟲/⟳/flip/custom angle, Deskew, Adjust… (brightness, contrast, hue,
saturation, sharpen, black & white, crop), Split, Combine, reorder
(▲ ▼ / Reverse / Interleave for manual duplex), Undo/Redo (Ctrl+Z / Ctrl+Y).
Deleting a page moves it to **Trash**, restorable for 30 days (configurable).

## OCR

"OCR pages" recognizes text (English out of the box; add languages in
Settings). Each page gets a `<image>.md` Markdown sidecar beside it, the OCRed
column updates in the index, and the text becomes searchable in the database.
Pages under 65% confidence show "⚠ review". "Re-OCR all" redoes everything —
old `.md` files go to the Trash. "Export PDF…" can embed a selectable text layer.

## AI descriptions (optional)

Settings → AI: paste your own Google AI Studio key (paid tier recommended —
see PRIVACY.md), validate, and an "AI describe" button appears in Groups. Every
run shows a page count and cost estimate first; results land in the
AIDescription index column. Blank pages are skipped without an API call.

## Existing folders

"Process existing folder…" registers a folder full of images and PDFs as a
group — file names are kept, PDFs become `<name>_page_NNN.png` pages, and
running it twice changes nothing. "Reconcile" re-matches files you renamed in
Explorer (by content checksum) and reports files that vanished.

## Command line

```
fgscanner scan --group C:\Scans\Inbox --source feeder -n 2
fgscanner process C:\Scans\Inbox --ocr --write-index
fgscanner export --group C:\Scans\Inbox -o inbox.pdf --pdfcompat A2-b --ocr
fgscanner list-devices
```

Exit code 0 = success; add `--verbose` for detail. Ideal for Task Scheduler.

## Keyboard shortcuts

Rebindable in Settings. Defaults: Ctrl+Enter scan, Ctrl+S save to group,
Ctrl+Shift+Enter commit, Ctrl+Z/Y undo/redo, Ctrl+Shift+←/→ rotate,
Delete → Trash, F2–F12 select profile 1–11.

## Your data

The SQLite database (`%APPDATA%\FGScanner\fgscanner.db`) is yours to query —
the views `v_index`, `v_pages`, and `v_ocr_text` are the stable public
surface; see docs/db-schema.md. The database is backed up automatically
before every schema migration.
