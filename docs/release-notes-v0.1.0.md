# FG Scanner v0.1.0 — release notes (DRAFT)

Paste as the GitHub Release body. Delete this header line and anything in `<!-- -->` first.

<!-- Verify before publishing: asset filenames match what release.yml actually produced; the
     Known limitations list still matches reality; SmartScreen wording still applies (drop it once
     SignPath is live). -->

---

First public release. FG Scanner is a Windows desktop scanner with a document-indexing layer: it
scans like NAPS2, then writes **typed index fields** to CSV/XLSX/XML/JSON with a SQLite database
behind them, OCRs pages to Markdown, and can describe pages with AI.

## What it does

**Scanning** — WIA, TWAIN (via a bundled 32-bit worker, so older vendor drivers work) and eSCL for
network scanners. Flatbed, feeder and duplex; profiles for source, resolution, bit depth, page size,
brightness and contrast. Batch scanning with single / multiple-with-prompt / multiple-with-delay
modes. Crash recovery restores pages if the app dies mid-scan.

**Indexing — the point of the app** — define up to 12 typed fields per profile (Text, Date, Number,
List) with Required / Default / Sticky flags and token defaults like `$(today)`, `$(group)`,
`$(counter)`, `$(user)`. Enter data in a keyboard-first grid before *or* after scanning, review, then
commit. Every enabled format is written atomically: **CSV** (RFC 4180, UTF-8 BOM, formula-injection
prefixing), **XLSX** (real typed cells, frozen header, auto-filter), **XML** (with a committed XSD)
and **JSON** — plus a `manifest.json` describing the profile and schema so other tools can read your
exports without guessing.

**OCR** — Tesseract 5.5, one pass per page, writing a `.md` sidecar beside each image with geometric
structure (columns, headings, lists) and YAML front matter. Produces searchable PDFs with an aligned
invisible text layer. Nine languages downloadable on demand with SHA-256 verification; English is
bundled. The queue is durable — kill the app and it resumes without redoing finished pages.

**AI descriptions (optional, off by default)** — bring your own Google AI Studio key, stored in
Windows Credential Manager. Shows a cost estimate before running and tracks cumulative spend. Blank
pages are skipped locally without an API call. Can be disabled machine-wide at install time.

**Working with what you already have** — point it at an existing folder of images and PDFs and it
adopts them in place, keeping filenames, with SHA-256 duplicate detection. Reconcile re-matches rows
to renamed files. Re-run OCR or AI selectively.

**Editing and export** — rotate, flip, deskew, crop, brightness/contrast, hue/saturation, B&W
threshold, sharpen, split, combine, reorder, with undo/redo. Export PDF (PDF/A-1b/2b/3b/3u,
metadata, encryption with permission flags) or images (JPEG/PNG/BMP/TIFF incl. multi-page and
CCITT4). Print, clipboard, drag-out.

**Headless** — `fgscanner.exe` provides `scan`, `process`, `export` and `list-devices` for scheduled
tasks and scripting, with no UI dependency.

**Also in this release** — Patch-T separator sheets, per-profile blank-page policies, full-text
search over OCR text and field values (FTS5), and run-command/webhook hooks on commit. The last four
are individually toggleable in Settings; search is on by default, the rest are opt-in.

## Install

Download `fgscanner-0.1.0-win-x64.exe` and run it. Windows 10 1607 or later, 64-bit.

> **SmartScreen will warn you.** This build is **not code-signed** — the certificate is pending. You
> will see "Windows protected your PC": choose **More info → Run anyway**. Verify the download
> against `SHA256SUMS` if you want certainty. Signed builds will follow.

Silent install: `fgscanner-0.1.0-win-x64.exe /VERYSILENT /NORESTART /SUPPRESSMSGBOXES`
Add `/MERGETASKS="!desktopicon"` to skip the desktop icon, or `/TASKS="aioptout"` to disable the AI
feature machine-wide. Uninstall with `unins000.exe /VERYSILENT`.

The installer registers "Open with FG Scanner" for `.pdf .jpg .jpeg .png .tiff .tif .bmp`, plus
"Scan with FG Scanner" on the scanner hardware button and AutoPlay. Your data under `%APPDATA%` is
never touched by install or uninstall.

Prefer no installer? Use the portable ZIP — extract and run, config lives beside the exe.

## Privacy

Everything is local by default. Nothing leaves your machine unless you explicitly enable AI
descriptions with your own API key, or a commit webhook you configure yourself. Update checks can be
turned off in Settings. See `PRIVACY.md`.

## Known limitations

Being straight about what is not proven or not built:

- **Not code-signed yet** — SmartScreen warns on first run (above).
- **WIA is untested on real hardware.** The code path exists and TWAIN + eSCL are verified against a
  physical scanner, but no WIA device was available to test with. Please report what you find.
- **Pages fed upside down OCR poorly and nothing warns you.** Orientation detection is designed and
  scheduled, not shipped. If output looks like gibberish, check the page orientation.
- **`index.csv` records `OCRed=Yes` regardless of OCR quality.** The app flags low-confidence pages
  in the grid, but that signal does not reach the CSV yet.
- **`fgscanner.exe` (the CLI) needs the .NET 10 Desktop Runtime installed.** The GUI is
  self-contained and needs nothing; the command line tool is not. Relevant if you are scripting it
  on a headless or server machine. See the user guide.
- **English UI only.**
- **No MAPI email export** — use Print, drag-out or the export formats.
- Duplex and empty-feeder error handling are lightly tested.

## Verified in this build

- TWAIN and eSCL discovery and scanning against a physical Pantum M6550NW.
- Scan → OCR → `index.csv` + `manifest.json` end to end.
- Page editing rewrites the file, refreshes the thumbnail and updates the stored checksum.
- 263 automated tests passing; CI green.

## Fixed since development builds

- **First page of a TWAIN scan carried 96 DPI metadata on 300 DPI pixels**, which fed OCR the wrong
  resolution and would have mis-sized exported PDF pages.
- Installer shipped a blank FileVersion resource.

---

*Built on [NAPS2.Sdk](https://github.com/cyanfish/naps2) (LGPL-2.1). FG Scanner itself is MIT — see
`LICENSE` and `THIRD-PARTY-NOTICES.md`.*
