# Quick Scan — Program Plan & Specification

**Date:** 2026-08-30
**Status:** proposed, not started. Approve before any phase begins.
**Reference UI:** `C:\Users\fgers\Pictures\Scanner1.png` — Pantum Scan Application (USB).

---

## 1. Summary — what gets built

FG Scanner is today a single-purpose machine: everything routes through a **Group → Document →
Page** model with index fields, OCR, AI, validation, commit and evidence export. That is correct
for Jim's legal capture work and wrong for everything else. Scanning a warranty card should not
require creating a group, pinning a schema version and committing a batch.

**Quick Scan** adds a second, parallel surface: pick a source, preview, scan, adjust, save to a
file. It resembles the manufacturer's utility in the screenshot — preview pane with rulers and a
crop marquee, a left tool rail, Basic/Advanced settings tabs, a live info panel, named Quick
Settings presets, and a destination block with filename, format and folder.

**Eleven phases**, each one Claude Code prompt, each independently shippable:

| # | Phase | Delivers | Size |
|---|---|---|---|
| 0 | Spikes | Three feasibility answers before anything is built | S |
| 1 | Widen scan options | Custom page size, auto-deskew, blank-exclude, duplex flip | S |
| 2 | Quick Scan session | Non-database session, filename patterns, counters | M |
| 3 | The screen | Section, Basic Settings, info panel, scan → folder | L |
| 4 | Preview + zoom | Preview scan, rulers, zoom, pan | M |
| 5 | Crop marquee | Marquee, Units selector, crop applied on save | M |
| 6 | Advanced tab | Rotation, brightness/contrast/sharpen/threshold, Default | M |
| 7 | Quick Settings | Named presets, save/load/delete | S |
| 8 | PDF output | Multi-page PDF, searchable via existing OCR | S |
| 9 | Clipboard + printer | Scan to clipboard; scan to printer (copier mode) | M |
| 10 | Email | Simple MAPI with an honest fallback | M |

Sizes assume the reuse inventory in §3 holds. Phases 1–3 are the minimum that produces something
usable; 4–7 make it feel like the manufacturer's app; 8–10 complete the destination set.

---

## 2. Decisions that bind every phase

These were settled before planning. They are not open for a phase to relitigate.

1. **Quick Scan is a new section in the existing app**, beside Scan / Groups / Search / Trash /
   Settings — not a second window and not a mode toggle inside the evidence Scan screen.
2. **Quick Scan never touches the database.** No groups, no documents, no pages, no index, no
   search rows. It writes files and nothing else. See §5.1 — this is enforced structurally, not
   by discipline.
3. **All four destinations are in scope**: folder (images), PDF (multi-page and searchable),
   email, and clipboard/printer.
4. **Image processing reuses what exists.** Rotation gets a UI. **Descreening and Fading
   Correction are deliberately not built** — they are photo-restoration filters with no
   implementation in this codebase and little value for documents. This is a decision to record
   in an ADR, not an omission to fix later.
5. **Quick Scan refuses to save into an evidence folder.** Not a warning, not a confirmation —
   a refusal, with an explanation and a prompt to pick elsewhere. See §5.3.
6. **The default output folder is `%USERPROFILE%\Pictures\`, and the operator can change it.**
   The default matches the manufacturer utility the station already has; the setting lives in
   Quick Scan's own JSON store, never the evidence settings table.
7. **Both Jim and other people use this station.** The same person switches between the
   legal-evidence surface and the casual one, which makes mode confusion a real hazard rather
   than a theoretical one. See §5.4.
8. **The two surfaces are visually distinct**, not merely differently labelled. A label reads the
   same to someone moving quickly; this is the mistake that costs a rescan. See §5.4.
9. **Nothing is hidden behind a station-type flag.** Quick Scan is always visible, on every
   install. FG Scanner is not a one-case tool — it is expected to serve other projects of the
   same shape as JimsStuff — so gating features on "this is an evidence machine" would be
   building for a configuration that does not generalise.

---

## 3. What already exists — the reuse inventory

Verified in the source on 2026-08-30. **Read this before writing any phase.** Most of the Pantum
panel maps onto code that is already here and tested; rebuilding any of it is a defect.

| Pantum UI element | Already exists as | Where |
|---|---|---|
| Save format PNG/JPEG/TIFF/BMP | `ImageExportFormat`, `ImageExportService` | `Scanning/Export/ImageExportService.cs` |
| PDF output | `PdfExportService`, `PdfCompatLevel`, `PdfSecurity` | `Scanning/Export/PdfExportService.cs` |
| **Searchable PDF** | `PdfOcrSettings` — embeds an invisible Tesseract text layer | `Scanning/Export/PdfExportService.cs:38,50` |
| Crop | `PageEdit.Crop(Left, Top, Right, Bottom)` | `Scanning/Editing/PageEdit.cs:16` |
| Rotation | `PageEdit.Rotate(Degrees)` | `Scanning/Editing/PageEdit.cs:10` |
| Image adjustment icons | `PageEdit.Brightness / Contrast / Hue / Saturation / Sharpen / BlackWhite` | `Scanning/Editing/PageEdit.cs:18-29` |
| Deskew | `PageEdit.Deskew`, `SkewEstimator` | `Scanning/Editing/` |
| Applying edits | `ImageEditor` | `Scanning/Editing/ImageEditor.cs` |
| Zoom in / zoom out / fit | `ZoomController` (Min 0.05, Max 8.0, `Fit(...)`) | `App/Views/ZoomController.cs` |
| Filename tokens | `TokenExpander` — `$(today) $(group) $(counter) $(user)` | `Core/Index/FieldValidator.cs:30` |
| Atomic file writes | `AtomicFileWriter` | `Core/Index/AtomicFileWriter.cs` |
| Blank detection | `BlankPageDetector` | `Scanning/Capture/BlankPageDetector.cs` |
| Scan plumbing | `IScanService`, `IPageStorage`, `FakeScanService` | `Scanning/IScanService.cs` |

**What does not exist and must be built:** the preview pane and its rulers, the crop marquee, the
Units selector, named presets, the info panel, the section itself, clipboard, printer, and email.

---

## 4. What NAPS2 offers that FG Scanner does not currently use

`ScanProfileOptions` (`Scanning/ScanModels.cs:37`) exposes only Device, Source, Dpi, BitDepth,
a 7-value `ScanPageSize` enum, Brightness and Contrast. NAPS2's `ScanOptions` offers considerably
more, all of it useful here:

- **`PageSize` accepts custom dimensions** — `PageSize(decimal width, decimal height, PageSizeUnit)`,
  with `WidthInMm` / `WidthInInches` / `WidthInThousandthsOfAnInch` conversions. FG Scanner's enum
  is a self-imposed limit, not a driver limit.
- `AutoDeskew` — straighten at the driver rather than in post.
- `ExcludeBlankPages` with `BlankPageWhiteThreshold` / `BlankPageCoverageThreshold`.
- `FlipDuplexedPages` — corrects back-side orientation on duplex runs.
- `RotateDegrees` — fixed rotation at scan time.
- `ThumbnailSize` — driver-generated thumbnails, useful for a fast preview.
- `CropToPageSize` / `StretchToPageSize`.

**Important caveat, and the reason Phase 0 exists:** `PageSize` is a *size*, not an offset plus a
size. A preview marquee needs an origin as well as dimensions, so it probably **cannot** drive a
device-side region scan through NAPS2's public API. The plan therefore specifies **software crop**
(scan the full platen, apply `PageEdit.Crop`), which works with code that already exists and is
deterministic. Phase 0 checks whether a device-side region is reachable; if it is, it becomes a
later optimisation, never a blocker.

---

## 5. Architecture

### 5.1 The database wall, enforced by project structure

The single most important structural decision. Quick Scan code lives in **`FgScanner.Core`**,
which does not reference `FgScanner.Data`. That is not a convention anyone has to remember — a
Quick Scan class *cannot* open a `DbContext`, because the assembly it lives in has no path to one.

```
FgScanner.Core/QuickScan/          <- session, destinations, naming. No DB access possible.
FgScanner.App/Views/QuickScan*     <- the section's view + view model
FgScanner.Scanning/                <- widened options; already has export + editing
```

Presets are stored as **a JSON file** under `%APPDATA%\FGScanner\quickscan-presets.json`, written
through the existing `AtomicFileWriter` — not in the settings table. That keeps the wall intact
and costs nothing, since atomic JSON writing is already solved here.

A test asserts the boundary directly: `FgScanner.Core` must not reference `FgScanner.Data`, and no
Quick Scan type may appear in any `DbSet`. A wall nobody tests is a wall that erodes.

### 5.2 Flow (continued below in §5.3)

```
Basic/Advanced settings ─┐
                         ├─> ScanProfileOptions ─> IScanService.ScanAsync ─> temp session folder
Preview (low DPI) ───────┘                                                          │
                                                                                    v
   marquee ──> crop rect ──> PageEdit.Crop ──> ImageEditor ──> ImageExportService ──> file
                                                            └─> PdfExportService  ──> file
                                                            └─> clipboard / printer / email
```

The session folder is temp scratch, cleaned on exit and on cancel. Nothing survives except the
file the operator asked for.

### 5.3 Refusing to save into an evidence folder

Quick Scan **refuses** to write anywhere inside an evidence group folder. Not a warning with an
override — a refusal that names the folder and asks for a different one.

This looks like it needs the database, since group folders are recorded in `Group.DirectoryPath`.
It does not, and the reason matters: **a committed evidence folder identifies itself on disk.** It
contains `manifest.json` carrying `"evidenceExport": 1` — the marker phase 16 added so an external
importer could refuse a pre-phase-16 folder outright. Quick Scan reads the filesystem, which
`FgScanner.Core` is perfectly able to do, and the database wall stays intact.

That filesystem check is also strictly better than a database lookup would have been, because it
catches a case a lookup misses entirely: **a group folder copied to the transfer drive**, which is
not in this machine's database at all but is still evidence.

Two nets, each covering the other's gap:

| Net | Catches | Misses |
|---|---|---|
| `manifest.json` with `evidenceExport` in the target folder **or any ancestor** | Committed groups, including copies on other drives | A group created but not yet committed — no manifest exists yet |
| Known group directories, passed in from the App layer (which does have database access) | Uncommitted groups on this machine | Folders copied to a machine that never had the group |

The App queries `Group.DirectoryPath` and hands `QuickScanDestination` a list of forbidden roots.
Core does not fetch it and does not know where it came from.

**Ancestors are checked, not just the folder itself.** Saving into `loepp-box1\originals\` or
`loepp-box1\anything\` is exactly as wrong as saving into `loepp-box1\`, and the `originals\`
subfolder in particular is part of a folder's evidentiary integrity (ADR-0003).

### 5.4 Mode confusion is the real operational risk

Jim uses **both** surfaces on the same machine. That makes confusing them an operational hazard,
not a hypothetical one, and it runs in one direction that matters:

**Scanning evidence in Quick Scan by mistake.** The pages land as ordinary image files. No index
row, no checksum, no `originals\` copy, no `capturedBy`, no manifest. Nothing is destroyed — but
nothing is captured to evidentiary standard either, and the box has to be scanned again. The
operator gets no error, because from Quick Scan's point of view nothing went wrong.

The reverse direction is self-correcting: scanning a warranty card into an evidence group is
obvious, reversible, and the person notices immediately.

So the mitigations all point one way — make it unmistakable which surface you are on:

- The Quick Scan header states plainly what it is **not** for: *"For everyday documents. Not for
  case evidence — use Scan for that."*
- The refusal in §5.3 catches the case where someone points Quick Scan at a case folder.
- The plain-language guide (§7.5) opens with a "which one do I use?" decision, before anything
  else.
- **The two surfaces are visually distinct** — the requirement below.

#### The visual distinction

Text labels are not enough. Someone moving quickly reads position and colour, not words, and both
screens otherwise share the same shell, the same chrome and a scan button in roughly the same
place. The distinction has to register before anyone reads anything.

Three requirements, and the third is the one usually skipped:

1. **A persistent accent** carried on the section's header band, distinct between the two
   surfaces, present at all times rather than only on hover or focus.
2. **Both themes.** The app is Fluent light/dark. The accent must be legible in both and must not
   be a colour that reads as an error state in either.
3. **Never colour alone.** Around 8% of men have some colour-vision deficiency, and the whole
   point of this distinction is that it works for someone who is not looking carefully. Pair the
   accent with a persistent word and an icon, so the surface is identifiable by three independent
   channels. A design that would fail a greyscale screenshot has not met this requirement — a
   greyscale check is the cheapest possible test of it.

Deliberately **not** specified here: the exact colours. That is Phase 3's design work, informed by
the existing Fluent palette rather than invented against it.

---

## 6. The phases

Each phase below is one Claude Code prompt. Run them in order. Each ends green, committed, and
independently useful.

---

### Phase 0 — Three spikes before anything is built

**Why first:** three questions, each of which changes a later phase's design. Cheap to answer now,
expensive to discover mid-build. Output is an answer, not code — anything built is throwaway.

**Model:** Sonnet.

> **Prompt:**
>
> Three feasibility questions about `C:\Users\fgers\Visual\FgmakerScanner`. Answer each by reading
> the source and the NAPS2 SDK; write throwaway probes if needed but keep no code. Report findings
> in `docs/superpowers/research/2026-08-30-quick-scan-spikes.md`.
>
> 1. **Device-side region scan.** NAPS2's `ScanOptions.PageSize` takes a size, not an origin. Is
>    there any supported route to scanning a sub-rectangle of the flatbed with an X/Y offset
>    through NAPS2.Sdk? Check `ScanOptions`, the TWAIN and WIA option paths, and whether NAPS2
>    exposes ImageLayout/frame settings. If there is no supported route, say so plainly — the plan
>    already assumes software crop and only needs confirmation.
> 2. **Preview cost.** What is the fastest honest way to get a flatbed preview image?
>    Compare a low-DPI (75–100) full-platen scan against `ScanOptions.ThumbnailSize`. Report
>    approximate wall-clock for each on the attached Pantum M6550NW if hardware is reachable; if
>    not, report what the API guarantees and mark the timing as unmeasured.
> 3. **Email viability.** Does Simple MAPI (`MAPISendMailW` in `mapi32.dll`) work on this
>    Windows 11 machine? Check `HKEY_LOCAL_MACHINE\Software\Clients\Mail` for a registered MAPI
>    client. There are reports of MAPI breaking on Windows 11 — determine whether it is viable
>    here, and what the fallback should be if no MAPI client is registered.
>
> Do not modify any production code. Do not add dependencies.

---

### Phase 1 — Widen the scanning options

**Goal:** `ScanProfileOptions` can express what NAPS2 already supports, without changing evidence
scanning behaviour by a single pixel.

**Scope:** `Scanning/ScanModels.cs`, `Scanning/Naps2ScanService.cs`, `FakeScanService.cs`, tests.
**Out of scope:** any UI, any Quick Scan code.

**Model:** Sonnet.

**Specification:**
- Add to `ScanProfileOptions`, all with defaults that preserve today's behaviour exactly:
  - `CustomPageSize` — nullable `(decimal Width, decimal Height, PageSizeUnit Unit)`. When set, it
    wins over `PageSize`. When null, behaviour is unchanged.
  - `AutoDeskew` (default `false`)
  - `ExcludeBlankPages` (default `false`)
  - `FlipDuplexedPages` (default `false`)
  - `RotateDegrees` (default `0`)
- Map each to NAPS2 `ScanOptions` in `Naps2ScanService`.
- `FakeScanService` honours them so business logic stays testable without hardware.

**The constraint that matters:** the evidence capture path must be byte-identical. Every new
property defaults to today's behaviour, and a test asserts that an options object constructed the
old way produces the same NAPS2 `ScanOptions` as before.

> **Prompt:**
>
> Widen `ScanProfileOptions` in `C:\Users\fgers\Visual\FgmakerScanner\src\FgScanner.Scanning\ScanModels.cs`
> to expose NAPS2 capabilities the app does not currently use: a nullable custom page size
> (width, height, unit — NAPS2's `PageSize` accepts `(decimal, decimal, PageSizeUnit)`),
> `AutoDeskew`, `ExcludeBlankPages`, `FlipDuplexedPages` and `RotateDegrees`. Map them in
> `Naps2ScanService`, and honour them in `FakeScanService`.
>
> **Every new property must default to current behaviour.** This record is on the legal-evidence
> capture path; a behaviour change here is a defect, not an improvement. Write a test that builds
> an options object the way existing callers do and asserts the resulting NAPS2 `ScanOptions`
> matches what it is today. When `CustomPageSize` is set it takes precedence over `PageSize`; when
> null, nothing changes.
>
> TDD. `dotnet build -c Release` (warnings are errors), `dotnet test -c Release`,
> `dotnet format --verify-no-changes`. Do not touch any UI or add any Quick Scan code.

---

### Phase 2 — The Quick Scan session

**Goal:** the non-database core — a scan session, a destination, and filename generation — with no
UI and no possibility of database access.

**Scope:** new `src/FgScanner.Core/QuickScan/`, tests.
**Out of scope:** UI, scanning integration, export.

**Model:** Sonnet.

**Specification:**
- `QuickScanSession` — owns a temp working folder, holds captured page paths in order, disposable,
  cleans up on dispose and on cancel.
- `QuickScanDestination` — folder path, filename pattern, `ImageExportFormat`, JPEG quality.
- `QuickScanNaming` — expands a filename pattern and resolves collisions:
  - Tokens: `$(date)` → `yyyy-MM-dd`, `$(time)` → `HHmmss`, `$(counter)` → zero-padded sequence.
    The Pantum default is `2026-08-18_001`, i.e. `$(date)_$(counter)`.
  - **Never overwrite an existing file.** If the resolved name exists, increment until free.
  - Counter persists across sessions (see presets file in §5.1) and resets per day if the pattern
    contains `$(date)`.
- Reuse `TokenExpander` where it fits; extend rather than duplicate if it does not.
- `EvidenceFolderGuard` — refuses a destination inside an evidence folder, per §5.3:
  - Rejects if the target folder **or any ancestor** contains `manifest.json` with
    `"evidenceExport"`.
  - Rejects if the target is at or under any path in a caller-supplied forbidden-roots list.
  - Returns a refusal carrying the offending folder, so the UI can name it rather than saying
    "invalid folder".

**The boundary test:** assert `FgScanner.Core` has no reference to `FgScanner.Data`, so Quick Scan
structurally cannot reach the evidence database.

> **Prompt:**
>
> Create `src/FgScanner.Core/QuickScan/` in `C:\Users\fgers\Visual\FgmakerScanner` with three
> types: `QuickScanSession` (owns a temp working folder, holds captured page paths in order,
> `IDisposable`, cleans up on dispose and on cancel), `QuickScanDestination` (folder, filename
> pattern, `ImageExportFormat`, JPEG quality), and `QuickScanNaming` (expands the pattern,
> resolves collisions).
>
> Filename tokens: `$(date)` → `yyyy-MM-dd`, `$(time)` → `HHmmss`, `$(counter)` → zero-padded.
> The default pattern is `$(date)_$(counter)`, producing `2026-08-18_001`. **Never overwrite an
> existing file** — if the name is taken, increment until one is free, and test that. Read
> `src/FgScanner.Core/Index/FieldValidator.cs:30` (`TokenExpander`) first and reuse it if it fits;
> extend it rather than writing a second token expander.
>
> Also add `EvidenceFolderGuard`. Quick Scan **refuses** — not warns — to save anywhere inside a
> legal-evidence folder. Two independent checks:
>
> 1. Reject if the target folder **or any ancestor** contains a `manifest.json` holding
>    `"evidenceExport"`. A committed evidence folder identifies itself on disk, so this needs no
>    database, and it catches group folders copied to a transfer drive that this machine has never
>    heard of.
> 2. Reject if the target is at or under any path in a forbidden-roots list supplied by the caller.
>    The App passes known group directories in; Core does not fetch them and must not try.
>
> Check ancestors, not just the folder itself — saving into `loepp-box1\originals\` is exactly as
> wrong as saving into `loepp-box1\`, and `originals\` is part of a folder's evidentiary integrity
> (ADR-0003). The refusal must carry the offending folder path so the UI can name it, rather than
> reporting a generic "invalid folder".
>
> This code must be unable to touch the database. It goes in `FgScanner.Core`, which does not
> reference `FgScanner.Data` — keep it that way, and add a test asserting `FgScanner.Core` has no
> `FgScanner.Data` reference. That wall is the whole point of the placement.
>
> No UI. No scanning calls. No export calls. TDD, Release build clean, format clean.

---

### Phase 3 — The screen: section, Basic Settings, scan to folder

**Goal:** the first usable Quick Scan — choose settings, press Scan, get a file. Laid out like the
Pantum window so later phases drop into place.

**Scope:** new `QuickScanView.xaml` / `QuickScanViewModel`, shell registration, DI, tests.
**Out of scope:** preview, marquee, Advanced tab, presets, PDF, clipboard, printer, email.

**Model:** Opus — this sets the layout every later phase builds into.

**Specification:**

Layout, mirroring the reference screenshot:

```
┌────────────────────────────────────────────────────────────┐
│ Units [Pixels ▾]            Quick Settings [Untitled ▾] [Save] │
├──┬──────────────────────┬──────────────────────────────────┤
│⬚ │                      │ ┌ Basic ┐ Advanced               │
│✋│   preview area       │ │ Document Source  [Platen ▾]    │
│🔍│   (placeholder in    │ │ Resolution       [300 dpi ▾]   │
│🔍│    this phase)       │ │ Color Mode       [True Color ▾]│
│🗑 │                      │ │ Scan Area        [Full Platen ▾]│
├──┴──────────────────────┼──────────────────────────────────┤
│ Document Source: Platen │ Save the scanned image to        │
│ Resolution:      300dpi │  [Folder] [Email]                │
│ Color Mode: True Color  │  File Name    2026-08-30_001     │
│ Scan Area:  Full Platen │  Save format  PNG                │
│ Image Size: 2551×3508px │  Save to      C:\Users\...\Pictures\│
│ Data Size:  25.60 MB    │                                  │
├─────────────────────────┴──────────────────────────────────┤
│ [Help] [About]              [Preview] [Scan] [Close]        │
└────────────────────────────────────────────────────────────┘
```

- Basic Settings: Document Source (Flatbed/Feeder/Duplex), Resolution (75/150/**300**/600/1200),
  Color Mode (True Color/Grayscale/Black & White), Scan Area (the page-size list plus Full Platen).
- Info panel is **live** — it recomputes on every settings change, before scanning. Image size in
  px is `dpi × inches`; data size is `width × height × bytesPerPixel`, shown in MB. This is the
  panel that tells an operator a 1200 dpi colour scan will be 400 MB before they wait for it.
- Save panel: Folder / Email toggle (Email disabled until Phase 10, with a tooltip saying so —
  never a dead button with no explanation), File Name (pattern preview shown resolved), Save
  format, Save to with a folder browser.
- Scan → `IScanService.ScanAsync` → session → `ImageExportService` → file. Then show where it went.
- Preview and the left tool rail are present but disabled in this phase, with tooltips.
- Toolbar/button-bar buttons that do nothing yet must be **disabled with a reason**, never enabled
  and silently inert.
- **Default output folder is `%USERPROFILE%\Pictures\`**, and the operator can change it. The
  chosen folder is remembered between sessions in Quick Scan's own store, not the settings table.
- **Wire the evidence-folder refusal.** The App queries `Group.DirectoryPath` and passes the list
  to `EvidenceFolderGuard` (Phase 2). When the operator picks a forbidden folder, refuse at the
  point of choosing — not after a scan has already run — and name the folder: *"That folder holds
  case evidence. Pick somewhere else."*
- **Say what this screen is not for.** A header line: *"For everyday documents. Not for case
  evidence — use Scan for that."* The same person uses both surfaces, and a box scanned here is a
  box with no index, no checksums and no preserved originals (§5.4).
- **Make the two surfaces visually distinct** per §5.4 — a persistent accent on the section header
  band, working in both Fluent themes, paired with a word and an icon so it never depends on
  colour alone. Verify with a greyscale screenshot: if the two screens are indistinguishable
  without colour, the requirement is not met.

**Testing:** view-model level, as the App test project does. Assert the info panel's computed image
and data sizes against known dpi/area combinations; assert format/extension pairing; assert the
resolved filename preview updates with the pattern.

> **Prompt:**
>
> Add a "Quick Scan" section to `C:\Users\fgers\Visual\FgmakerScanner`, a sixth entry beside
> Scan / Groups / Search / Trash / Settings in `ShellViewModel`. It is general-purpose scanning for
> documents that are **not** legal evidence: pick settings, press Scan, get a file.
>
> Read `docs/superpowers/plans/2026-08-30-quick-scan-program.md` §3 (reuse inventory) and §6
> Phase 3 (layout and specification) before writing anything. Read `ScanView.xaml` /
> `ScanViewModel.cs` first and follow their conventions for DI, commands and testing.
>
> This phase delivers: the section and its layout; the Basic Settings tab (Document Source,
> Resolution, Color Mode, Scan Area); the live info panel; the save panel (Folder destination,
> filename pattern with resolved preview, format, folder browser); and a working Scan button that
> writes one file via the existing `ImageExportService`.
>
> **Hard boundary: Quick Scan must never touch the database.** No groups, no documents, no pages.
> Use `FgScanner.Core/QuickScan/` from Phase 2 for the session and naming.
>
> Default the output folder to `%USERPROFILE%\Pictures\`, let the operator change it, and remember
> their choice between sessions in Quick Scan's own store — not the settings table.
>
> **Wire the evidence-folder refusal from Phase 2.** Query `Group.DirectoryPath` in the App layer
> and pass those paths to `EvidenceFolderGuard` as forbidden roots. Refuse at the moment the
> operator picks the folder, not after a scan has run, and name the folder in the message —
> "That folder holds case evidence. Pick somewhere else." A refusal the operator only discovers
> after waiting for a 200-page stack is a bug.
>
> Put a line in the header saying what this screen is not for: "For everyday documents. Not for
> case evidence — use Scan for that." The same operator uses both surfaces on this machine, and
> a box scanned here gets no index, no checksums and no preserved originals — with no error to
> tell them.
>
> **Make Scan and Quick Scan visually distinct**, per §5.4 of the plan. A persistent accent on the
> section header band, different between the two, legible in both Fluent light and dark themes,
> and **never colour alone** — pair it with a word and an icon so it survives colour-vision
> deficiency and a hurried glance. Take a greyscale screenshot of both screens: if you cannot tell
> them apart without colour, the requirement is not met. Choose colours from the existing Fluent
> palette rather than inventing new ones. Consider loading the `frontend-design` skill for the
> accent and hierarchy work, but note it is web-oriented — apply the principles, not its CSS.
>
> The preview pane, the left tool rail and the Email button belong to later phases. Put them in the
> layout **disabled, with a tooltip saying which feature they are waiting for** — never an enabled
> control that does nothing.
>
> UI strings are inline English, no `.resx` (ADR-0001). TDD at the view-model level, as the App
> tests do. Release build clean, format clean.

---

### Phase 4 — Preview: scan, rulers, zoom, pan

**Goal:** the preview pane works. Press Preview, see the platen, zoom and pan around it.

**Model:** Opus.

**Specification:**
- Preview = a low-DPI (default 100) full-area scan into the session's temp folder, never saved as
  output and discarded when the next preview replaces it.
- Rulers on the top and left edges, matching the reference screenshot's tick style.
- Reuse `ZoomController` for zoom in / out / fit. Wire the left rail's zoom buttons and the pan
  (hand) tool. Mouse wheel zooms; drag with the hand tool pans.
- The trash icon in the rail clears the current preview.
- Preview must be cancellable — a flatbed preview takes seconds and the operator must be able to
  abandon it.

> **Prompt:**
>
> Make the Quick Scan preview pane work in `C:\Users\fgers\Visual\FgmakerScanner`. Pressing
> Preview runs a low-DPI (100) full-area scan into the session temp folder and shows it in the
> preview area. It is never saved as output and is replaced by the next preview.
>
> Add rulers along the top and left edges (see `C:\Users\fgers\Pictures\Scanner1.png` for the tick
> style). Wire the left tool rail's zoom-in, zoom-out and pan (hand) tools, and the trash icon,
> which clears the preview. **Reuse `src/FgScanner.App/Views/ZoomController.cs`** — it already has
> `In`, `Out`, `Reset` and `Fit`; do not write a second zoom implementation. Mouse wheel zooms,
> hand-tool drag pans.
>
> The preview must be cancellable mid-scan — a flatbed pass takes seconds and the operator must be
> able to abandon it without waiting.
>
> Units and the crop marquee are Phase 5; leave them alone. No database access. TDD at view-model
> level, Release build clean, format clean.

---

### Phase 5 — Crop marquee and Units

**Goal:** drag a rectangle on the preview, scan only that, in whatever units you think in.

**Model:** Opus.

**Specification:**
- Marquee over the preview: draw, move, resize by corner and edge handles, respecting zoom.
- Units selector (Pixels / Inches / Millimetres) drives the rulers **and** the marquee readout.
- **Crop is applied in software**, not at the device: the final scan runs at full area and target
  DPI, then `PageEdit.Crop` trims it via `ImageEditor`. Phase 0 confirms whether a device-side
  region is reachable; if it is, it is a later optimisation, not this phase's job.
- Marquee coordinates convert preview-px → physical → final-scan-px. The conversion is where this
  phase will go wrong: preview DPI and final DPI differ, so **test the conversion directly** with
  known values rather than only through the UI.
- Info panel's Scan Area and Image Size update live from the marquee.
- Clearing the marquee returns to full area.

> **Prompt:**
>
> Add a crop marquee and a Units selector to the Quick Scan preview in
> `C:\Users\fgers\Visual\FgmakerScanner`.
>
> The marquee draws over the preview, and can be moved and resized by corner and edge handles, at
> any zoom level. The Units dropdown (Pixels / Inches / Millimetres) drives both the rulers and the
> marquee's readout. The info panel's Scan Area and Image Size update live from the marquee, and
> clearing it returns to full area.
>
> **The crop is applied in software, not at the scanner.** The final scan runs full-area at the
> target DPI, then `PageEdit.Crop` trims it through `ImageEditor` — both already exist in
> `src/FgScanner.Scanning/Editing/`. Do not attempt a device-side region scan.
>
> The coordinate conversion is the part that will go wrong: the preview is ~100 dpi and the final
> scan may be 300 or 600, so marquee pixels are not output pixels. **Write direct unit tests for
> preview-px → physical → output-px with known values**, not just UI tests. A marquee that is
> visually right and numerically wrong produces silently mis-cropped scans.
>
> No database access. Release build clean, format clean.

---

### Phase 6 — Advanced Settings tab

**Goal:** the second tab, matching the reference screenshot's structure minus the two filters we
deliberately are not building.

**Model:** Sonnet.

**Specification:**
- **Image Processing** group: Rotation (No Rotation / 90° / 180° / 270°).
- **Image Adjustment** group: brightness, contrast, sharpen, black-and-white threshold — all four
  already exist as `PageEdit` records. Present them as the reference's four icon buttons opening
  small adjustment popovers, applied live to the preview.
- **Default** button resets the whole tab.
- **Descreening and Fading Correction are not built.** The reference shows them; we are choosing
  not to. Record that in ADR-0008 with the reasoning, rather than leaving a gap someone
  "fixes" later without knowing why it was empty.

> **Prompt:**
>
> Build the Advanced Settings tab for Quick Scan in `C:\Users\fgers\Visual\FgmakerScanner`,
> following `C:\Users\fgers\Pictures\Scanner1.png`.
>
> Two groups. **Image Processing:** a Rotation dropdown (No Rotation / 90 / 180 / 270).
> **Image Adjustment:** four controls — brightness, contrast, sharpen, black-and-white threshold —
> presented as the reference's four icon buttons, each opening a small adjustment popover, applied
> live to the preview. Plus a Default button resetting the tab.
>
> **All four adjustments already exist** as `PageEdit.Brightness`, `PageEdit.Contrast`,
> `PageEdit.Sharpen` and `PageEdit.BlackWhite` in `src/FgScanner.Scanning/Editing/PageEdit.cs`,
> applied by `ImageEditor`. Wire them up; do not write new image maths.
>
> **Do not build Descreening or Fading Correction**, even though the screenshot shows them. They
> are photo-restoration filters with no implementation here and little value for documents. Write
> `docs/adr/0008-no-descreening-or-fading-correction.md` recording that decision and its reasoning,
> following the shape of `docs/adr/0003-preserve-originals.md`, so nobody later fills the gap
> without knowing it was deliberate.
>
> No database access. TDD at view-model level, Release build clean, format clean.

---

### Phase 7 — Quick Settings presets

**Goal:** name a configuration, get it back later. The dropdown at the top of the reference.

**Model:** Sonnet.

**Specification:**
- Save the current Basic + Advanced + destination configuration under a name; load it; delete it.
- Stored as JSON at `%APPDATA%\FGScanner\quickscan-presets.json`, written through the existing
  `AtomicFileWriter` — **not** in the settings table, which lives in the evidence database.
- A `FormatVersion` field from day one, with unknown fields tolerated on read. The `.fgprofile`
  work in phase 19 showed what a missing version field costs later.
- Ship two built-in presets, since an empty dropdown teaches nothing:
  - **Document** — 300 dpi, Grayscale, Letter, PDF
  - **Photo** — 600 dpi, True Color, Full Platen, PNG

> **Prompt:**
>
> Add Quick Settings presets to Quick Scan in `C:\Users\fgers\Visual\FgmakerScanner` — the named
> dropdown and Save button at the top of `C:\Users\fgers\Pictures\Scanner1.png`.
>
> A preset captures the Basic tab, Advanced tab and destination settings under a name. Support
> save, load and delete. Store them as JSON at `%APPDATA%\FGScanner\quickscan-presets.json` using
> the existing `AtomicFileWriter` (`src/FgScanner.Core/Index/AtomicFileWriter.cs`).
>
> **Do not store presets in the settings table** — that lives in the evidence database, and Quick
> Scan does not touch it. The JSON file keeps that wall intact.
>
> Include a `FormatVersion` field from the first version, and tolerate unknown fields on read. The
> `.fgprofile` format in phase 19 needed a version bump precisely because this was not done early.
>
> Ship two built-in presets so the dropdown is not empty on first run: **Document** (300 dpi,
> Grayscale, Letter, PDF) and **Photo** (600 dpi, True Color, Full Platen, PNG).
>
> TDD including a round-trip test and an unknown-field test. Release build clean, format clean.

---

### Phase 8 — PDF output, multi-page and searchable

**Goal:** feed a stack, get one PDF. Optionally a searchable one.

**Model:** Sonnet. **Smaller than it sounds** — see below.

**Specification:**
- Add PDF to the Save format list. When chosen, a feeder run produces **one** multi-page PDF.
- A "Searchable (adds text layer)" checkbox runs OCR during export.
- **This is mostly wiring.** `PdfExportService` already accepts `PdfOcrSettings`, which "runs OCR
  and embeds an invisible, selectable text layer" (`PdfExportService.cs:38,50`). The Tesseract
  paths come from `FgScanner.Ocr`. Do not build an OCR pipeline; connect the existing one.
- Warn honestly: searchable PDF is much slower per page. Show progress, keep it cancellable.

> **Prompt:**
>
> Add PDF output to Quick Scan in `C:\Users\fgers\Visual\FgmakerScanner`. Selecting PDF as the save
> format makes a feeder run produce one multi-page PDF instead of one file per page. Add a
> "Searchable (adds text layer)" checkbox.
>
> **Most of this already exists.** `src/FgScanner.Scanning/Export/PdfExportService.cs` takes
> `PdfExportOptions` with an optional `PdfOcrSettings` that runs Tesseract and embeds an invisible
> text layer (see lines 38 and 50). The Tesseract paths come from `FgScanner.Ocr`. Read that
> service before writing anything — your job is wiring, not building an OCR or PDF pipeline.
>
> Searchable PDF is much slower per page. Show progress and keep it cancellable; do not let the UI
> appear hung on a 200-page stack.
>
> No database access. TDD, Release build clean, format clean.

---

### Phase 9 — Clipboard and printer

**Goal:** scan straight to the clipboard, and scan-to-printer so the machine works as a copier.

**Model:** Sonnet.

**Specification:**
- Clipboard: single-page only. Multi-page to clipboard is meaningless — disable it with a reason
  rather than copying only page one silently.
- Printer: WPF `PrintDialog`, honouring page size and letting the operator choose the printer.
  Multi-page prints all pages.
- Both are destinations alongside Folder/Email, not extra buttons bolted elsewhere.

> **Prompt:**
>
> Add Clipboard and Printer destinations to Quick Scan in `C:\Users\fgers\Visual\FgmakerScanner`,
> alongside the existing Folder destination.
>
> **Clipboard** copies the scanned image for pasting. It is single-page only — if the session holds
> more than one page, disable it with a tooltip explaining why, rather than silently copying only
> the first page.
>
> **Printer** scans and prints, so the scanner plus a printer works as a photocopier. Use WPF's
> `PrintDialog` so the operator picks the printer and honours the page size. Multi-page sessions
> print every page.
>
> Both are destinations in the same save panel as Folder, not separate buttons elsewhere in the UI.
>
> No database access. TDD at view-model level, Release build clean, format clean.

---

### Phase 10 — Email

**Goal:** the Email button in the reference screenshot, done honestly.

**Model:** Opus — the fallback design matters more than the happy path.

**Specification:**
- **Read Phase 0's spike findings first.** Simple MAPI (`MAPISendMailW`) has reported breakage on
  Windows 11, and the default MAPI client comes from
  `HKEY_LOCAL_MACHINE\Software\Clients\Mail`.
- Attempt Simple MAPI via P/Invoke: save the scan to a temp file, attach, open the client's compose
  window. Never send silently — the operator sees and sends the message.
- **The fallback is the real design work.** If no MAPI client is registered, or the call fails, do
  not show a raw error. Save the file to the normal destination folder, open that folder in
  Explorer with the file selected, and say plainly: the scan is saved here, attach it yourself.
  A dead-end error on a machine with no Outlook is a bug; a graceful hand-off is a feature.
- **Never copy NAPS2's email code** — it lives in NAPS2.Lib, which is GPL. Re-implement.

> **Prompt:**
>
> Add the Email destination to Quick Scan in `C:\Users\fgers\Visual\FgmakerScanner`, and enable the
> Email button that earlier phases left disabled.
>
> **Read `docs/superpowers/research/2026-08-30-quick-scan-spikes.md` first** — spike 3 determined
> whether Simple MAPI works on this machine.
>
> Happy path: save the scan to a temp file, then use Simple MAPI (`MAPISendMailW` from
> `mapi32.dll`, via P/Invoke) to open the default mail client's compose window with the file
> attached. **Never send silently** — the operator reviews and sends.
>
> **The fallback matters more than the happy path.** MAPI has reported breakage on Windows 11, and
> a machine with no registered MAPI client (`HKEY_LOCAL_MACHINE\Software\Clients\Mail`) is normal
> now. If MAPI is unavailable or fails, do not show a raw error code: save the file to the usual
> destination folder, open Explorer with it selected, and tell the operator plainly that the scan
> is saved and they can attach it themselves. Test that path explicitly — it is the one most users
> will hit.
>
> **Licensing: never copy NAPS2's email implementation.** It is in NAPS2.Lib, which is GPL, and
> this project is not. Re-implement from the Win32 API.
>
> No database access. Release build clean, format clean.

---

## 7. Claude infrastructure — skills, models, and the .md files

### 7.1 Skills to use as-is

- **`superpowers:brainstorming`** — before any phase whose shape is not fully settled by this
  document (realistically Phases 3, 5 and 10).
- **`superpowers:writing-plans`** — to turn any phase here into a task-level plan if it proves
  bigger than one prompt.
- **`superpowers:subagent-driven-development`** — the execution harness used for phase 19. It
  earned its cost there: the whole-branch review caught a data-destruction bug that ten
  task-scoped reviews could not see.
- **`superpowers:test-driven-development`** and **`superpowers:verification-before-completion`** —
  every phase.

### 7.2 A new project skill is warranted

**Create `.claude/skills/fgscanner-wpf-section/SKILL.md`.**

Phases 3–9 all add or modify WPF views and view models in the same codebase, and phase 19 showed
what that costs without a written convention: an implementer referenced `FieldRow` as a nested
type when it is top-level, briefs cited line numbers that had drifted, and one brief named a method
(`AdoptFilesAsync`) that does not exist. Those are all "how this codebase does views" knowledge
that currently lives nowhere.

The skill should capture: how a section is registered in `ShellViewModel` and DI; the
CommunityToolkit MVVM conventions in use (`[ObservableProperty]`, `[RelayCommand]`, `partial void
On…Changed`); that App tests exercise view models directly and never the visual tree; that UI
strings are inline English with no `.resx` (ADR-0001); how `RebuildColumns`-style dynamic XAML is
done; and the rule that a control which does nothing yet is disabled with a tooltip, never enabled
and inert.

**Do not create** a "scanning" skill — `IScanService` is small and its rules already live in
CLAUDE.md.

### 7.3 CLAUDE.md

Add a short **Quick Scan** section stating the wall: Quick Scan lives in `FgScanner.Core/QuickScan`
and the App view; it must never touch groups, documents, pages, the index or search; presets live
in a JSON file, not the settings table; and the placement in `FgScanner.Core` is what enforces it,
because that assembly cannot reference `FgScanner.Data`. Link the spec rather than restating it.

Keep it to a few lines. CLAUDE.md is read at the start of every session and pays for its length.

### 7.4 ADRs

| ADR | Decision |
|---|---|
| 0006 | Quick Scan never touches the database, and why the wall is structural rather than conventional |
| 0007 | Crop is applied in software, not as a device-side region scan (with Phase 0's finding) |
| 0008 | Descreening and Fading Correction are deliberately not built |

### 7.5 Other documents

- **`docs/spec-quick-scan.md`** — the specification this plan argues from, written before Phase 1.
- **`docs/FEATURE-PARITY.md`** — rows per phase as they land.
- **`docs/manual-tests.md`** — a Quick Scan block. The WPF GUI cannot be verified by an agent, as
  phase 19 established; the checklist is the only coverage of what a human must confirm.
- **`docs/user-guide.md`** — a Quick Scan section for the general reference.
- **`build/installer/QUICK-SCAN-HOWTO.txt`** — a plain-language guide staged on the machine
  alongside `EVIDENCE-SETUP.txt`. **Jim uses this surface too, not only other people**, so it gets
  the same treatment that guide got: short steps, one action each, a "what you should see" after
  the ones that can go wrong.

  It must **open with the choice, before anything else**:

  > **Which one do I use?**
  > Scanning papers for the court case? Use **Scan**. Everything else — a receipt, a letter, a
  > photo, a warranty card? Use **Quick Scan**.
  > If you are not sure, use **Scan**. It is easy to move a page out later. It is not easy to go
  > back and re-scan a box properly.

  That last line is the whole point. The two mistakes are not symmetrical (§5.4), so the guidance
  should not be either. First draft lands with Phase 3, updated as each later phase ships.

### 7.6 Model selection per phase

| Phase | Model | Why |
|---|---|---|
| 0 Spikes | Sonnet | Reading and reporting, no design |
| 1 Options | Sonnet | Mechanical, complete spec |
| 2 Session | Sonnet | Self-contained logic |
| 3 The screen | **Opus** | Sets the layout every later phase builds into |
| 4 Preview | **Opus** | Interactive canvas work |
| 5 Marquee | **Opus** | Coordinate conversion is subtle and silently wrong when off |
| 6 Advanced tab | Sonnet | Wiring existing edits |
| 7 Presets | Sonnet | Serialisation, well specified |
| 8 PDF | Sonnet | Mostly wiring |
| 9 Clipboard/printer | Sonnet | Two small integrations |
| 10 Email | **Opus** | The fallback design is the hard part |

Reviews scale to risk: Sonnet for most, Opus for Phases 3, 5 and 10, and Opus for the final
whole-branch review.

---

## 8. Features worth considering that the reference screenshot does not show

Researched against current scanning utilities (VueScan, PaperScan, ScanPro, Scanner Pro). Ranked by
value here divided by cost, given what this codebase already has.

### Strong candidates — cheap because the pieces exist

1. **Auto colour detection.** Decide per page whether to save colour or mono: typed pages become
   black-and-white (smaller, sharper text), pages with photos or coloured stamps stay colour. Real
   file-size and legibility wins on mixed stacks. Needs one new heuristic over the existing
   pipeline.
2. **Blank-page removal as a visible toggle.** `BlankPageDetector` and NAPS2's `ExcludeBlankPages`
   both already exist — this is a checkbox away, and it is the single most requested feeder
   feature anywhere.
3. **Auto-deskew as a visible toggle.** `SkewEstimator` and `PageEdit.Deskew` exist; NAPS2 has
   `AutoDeskew` at the driver. Phase 1 already exposes it — surface it in Basic Settings.
4. **Recent scans list.** The last ten outputs with "open file" / "open folder". Costs almost
   nothing and removes the commonest post-scan question: *where did it go?*
5. **Drag-and-drop out.** Drag the preview into another application. Small, and it makes the tool
   feel finished.
6. **PDF/A for archiving and password-protected PDF.** `PdfCompatLevel` and `PdfSecurity` already
   exist in `PdfExportService` — surfacing them is a dropdown and two fields.

### Worth doing, more work

7. **Multi-feed (double-feed) detection.** Standard on document scanners and the main cause of
   silently missing pages. Depends on driver support; worth a spike.
8. **Colour dropout.** Drop a colour (typically the red or blue of form rules) so OCR reads the
   content instead of the lines. Genuinely useful for forms.
9. **Long-page / receipt mode.** Continuous-length scanning for receipts and adding-machine tape.
10. **Watch folder.** Drop a file in, the pipeline runs. Promised in phase 10 and never built
    (`STATUS-AND-REMAINING-WORK.md`) — Quick Scan is a more natural home for it than the evidence
    path.
11. **Scanner hardware button.** The installer already mentions a scanner button; binding it to a
    Quick Scan preset is the manufacturer-app behaviour people expect.

### Deliberately excluded

12. **Descreening** — frequency-domain moiré removal for scanned printed halftones. New filter
    from scratch, rarely useful for documents. (ADR-0008.)
13. **Fading correction** — photo colour restoration. Same reasoning. (ADR-0008.)
14. **Infrared dust/scratch removal** — needs an infrared channel the hardware does not have.

---

## 9. Risks and open questions

| Risk | Impact | Mitigation |
|---|---|---|
| Phase 1 changes evidence scanning behaviour | Legal capture path regresses | Every new option defaults to current behaviour; a test pins the existing mapping |
| Marquee coordinate conversion is wrong | Silently mis-cropped scans | Direct unit tests on preview-px → physical → output-px, not just UI tests |
| MAPI unavailable on Windows 11 | Email dead-ends | Phase 0 spike; fallback is the specified behaviour, not an afterthought |
| Quick Scan output lands in an evidence folder | Casual scan mistaken for evidence | Structural wall (§5.1) plus the two-net refusal (§5.3), tested both ways |
| **Evidence scanned in Quick Scan by mistake** | **Box captured with no index, checksums or originals — and no error. Must be rescanned** | Header states what the screen is not for; visually distinct sections; the guide opens with the choice (§5.4) |
| GUI cannot be verified by an agent | Ships unverified | `docs/manual-tests.md` checklist per phase, as phase 19 established |
| Scope creep into a photo editor | Never ships | ADR-0008 records what is deliberately excluded |

**Answered 2026-08-30:**

1. **Refuse, not warn.** Quick Scan will not save into an evidence folder at all — no override.
   Implemented as two independent nets in §5.3, one of which also catches group folders copied to
   a transfer drive that this machine has no database record of.
2. **Default `%USERPROFILE%\Pictures\`, operator-changeable**, remembered in Quick Scan's own
   store rather than the evidence settings table.
3. **Jim uses it too**, alongside other people. That is what makes §5.4 the most important
   operational section in this plan: the same person moves between a surface with legal weight
   and one without, and only one of the two possible mistakes is self-correcting.

4. **The two surfaces are visually distinct**, not just differently labelled — accent, word and
   icon together, per §5.4. Colour alone would fail exactly the hurried glance it exists to catch.
5. **Nothing is hidden behind a station-type flag.** Quick Scan ships visible everywhere.

**Nothing is open. This plan is ready to execute.**

### 9.1 A forward-looking note, outside this plan's scope

Answer 5 came with a reason worth recording: *"this application will be used for things similar to
what JimsStuff is doing."* FG Scanner is not a one-case tool — it is expected to serve other
projects of the same shape.

That is why nothing here is gated on "this is the evidence machine", and it flags a tension that
**this plan does not attempt to solve**: `EvidenceProfile` currently hard-codes JimsStuff's
thirteen field names as *the* contract, and `ProfileService.EnsureEvidenceProfileAsync` builds
exactly that one profile. A second project of the same shape would need its own field contract as
code, its own importer agreement, and a way to choose between them.

Not a problem today, and not Quick Scan's problem. But when a second case appears, the work is
generalising the profile-as-code mechanism — not copying `EvidenceProfile.cs` and editing the
strings, which would silently fork the contract that phase 19 worked to make singular.

---

## 10. Sources

- [NAPS2 SDK — ScanOptions](https://www.naps2.com/sdk/doc/api/NAPS2.Scan.ScanOptions.html)
- [NAPS2 SDK — PageSize](https://www.naps2.com/sdk/doc/api/NAPS2.Images.PageSize.html)
- [NAPS2 — Profile Settings](https://www.naps2.com/doc/profile-settings)
- [MAPISENDMAILW (mapi.h)](https://learn.microsoft.com/en-us/windows/win32/api/mapi/nc-mapi-mapisendmailw)
- [mapi32.dll stub registry settings](https://learn.microsoft.com/en-us/previous-versions/windows/desktop/windowsmapi/mapi32-dll-stub-registry-settings)
- [MAPI on Windows 11 — reported issues](https://forum.emclient.com/t/mapi-on-windows-11/92822)
- [VueScan — feature reference](https://www.hamrick.com/)
- [Document scanner software comparison, 2026](https://geekchamp.com/19-best-document-scanner-software-for-pc-in-2026/)
