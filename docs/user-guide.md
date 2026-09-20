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
- **Check a page before saving:** double-click a thumbnail, or select it and press Enter, to open it
  full size. The arrow keys page through the other scans. When you close the viewer, the page you
  were on stays selected.
- **Delete a failed scan before saving:**
  - Select one or more thumbnails (Ctrl/Shift+click, or Ctrl+A for all).
  - Press Delete or **Delete selected…**, then confirm.
  - The files go to the **Windows Recycle Bin**, and the page labels renumber.
  - Delete is unavailable while a scan or a save is running.

  Pages already saved to a group are deleted from Groups instead, into FG Scanner's Trash.
- **Restoring a deleted scan:** the Recycle Bin puts the file back in FG Scanner's hidden scan-session
  folder (`%APPDATA%\FGScanner\recovery\…`). That folder is cleared after Save to group, so copy the
  restored file somewhere else straight away — or simply scan the sheet again.

## Profiles and index fields (Settings)

A profile holds up to 16 typed fields — Text, Date (ISO), Number, List — each
optionally Required (blocks commit), Sticky (carries to the next page), or
defaulted (tokens: `$(today)`, `$(group)`, `$(counter)`, `$(user)`). Saving
field changes creates a new schema version; existing groups keep theirs.

**Settings apply when you save them — you do not need to restart.** A profile
you create, rename, delete or import is in the Groups list straight away, and a
base folder you set is used by the next group you create. If you change a
profile's fields while a group is open, that group keeps its own layout and
shows a banner offering **"Use latest field layout"**; values you have typed for
the next scan survive, except for a field that no longer exists — the status
line says how many were dropped. A change made while a scan is running, or while
a sheet with notes is part-captured, waits until the scan finishes or the sheet
is completed or abandoned, so a half-captured sheet is never disturbed.

**Appearance.** Settings → Appearance picks the theme — system, light or dark —
and it applies when you save.

**Trash.** "Keep deleted pages for N days" shows what is actually stored. Change
it and save, and the purge runs there and then, reporting how many items it
removed.

**Length and memo (Text fields).** A Text field can carry a **Length** — 1 to
100 characters — and can be marked **Memo**, which gives it a larger box you
can resize and raises its limit to 2000. Leave Length blank for no limit. Both
are about typing and checking values on screen: neither is written to the index
files, and shortening a length never shortens values already stored — they show
as invalid until someone corrects them. Typing stops at the limit, and a paste
that would go over is refused whole, with a message, rather than being silently
cut short.
Profiles also choose the export formats and the CSV delimiter, and can be
shared as `.fgprofile` files (Export/Import buttons).

## Editing pages (Groups section)

Select rows in the grid (Ctrl/Shift for several) and use the toolbar:
rotate ⟲/⟳/flip/custom angle, Deskew, Adjust… (brightness, contrast, hue,
saturation, sharpen, black & white, crop), Split, Combine, reorder
(▲ ▼ / Reverse / Interleave for manual duplex), Undo/Redo (Ctrl+Z / Ctrl+Y).
Deleting a page moves it to **Trash**, restorable for 30 days (configurable).

**Record editor.** "Record editor…" on the toolbar opens the selected page in a window of its own:
its fields on the left, the page on the right, and the rest of the group in a list below. Drag the
dividers to give a pane more room, and drag the corner grip of a memo box to make it bigger; the
sizes come back the next time you open that group. Ctrl+PageDown and Ctrl+PageUp move to the next
and previous page, and the fields, the image and the list stay on the same page. The toolbar also
has Undo, Redo, Add missed page…, Import PDF/images… and Delete page (to Trash). What you type here
goes exactly where the grid's cells go — it is the same record, in a bigger window. Closing it
leaves the Groups list on the page you were editing.

**Preview and page viewer.** Fit shows the whole page — in the preview beside the grid and in the
full-size viewer (double-click the preview). 100% is the page at its actual paper size. The page
keeps fitting as you resize the panel until you zoom with + or −; press Fit to go back. On a small
screen every section and dialog scrolls, and dialog buttons always stay visible.

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

Exit code 0 = success. Ideal for Task Scheduler.

`--verbose` and `--fake` are **global** options and must come *before* the
command — `fgscanner --verbose scan --group ...`. Placed after the command they
are rejected with "Unrecognized command or argument".

> **`fgscanner.exe` requires the .NET 10 Desktop Runtime.** Unlike `FgScanner.exe`,
> which is self-contained, the command line tool is published framework-dependent.
> On a machine without .NET 10 it exits with "You must install .NET". Install the
> runtime from <https://dotnet.microsoft.com/download/dotnet/10.0> — the GUI needs
> nothing. This matters most for headless or server machines running scheduled
> tasks, which are exactly where the runtime is least likely to already be present.

## Keyboard shortcuts

Rebindable in Settings. Defaults: Ctrl+Enter scan, Ctrl+S save to group,
Ctrl+Shift+Enter commit, Ctrl+Z/Y undo/redo, Ctrl+Shift+←/→ rotate,
Delete → Trash, F2–F12 select profile 1–11.

The page keys — Delete, undo/redo and rotate — act only on the screen that is showing:
- **Groups:** they act on the selected page, and Delete moves it to the Trash.
- **Scan:** Delete removes the selected unsaved scans, to the Recycle Bin.
- **Search, Trash and Settings:** they do nothing.

The scan, save, commit and profile keys work from any screen.

## Your data

The SQLite database (`%APPDATA%\FGScanner\fgscanner.db`) is yours to query —
the views `v_index`, `v_pages`, and `v_ocr_text` are the stable public
surface; see docs/db-schema.md. The database is backed up automatically
before every schema migration.
