# FG Scanner — Complete Programming Plan & Specification

**Version:** 1.1 (approved) · **Date:** 2026-08-19 · **Author:** Franz Gerster / Claude Code
**Status:** APPROVED — all open decisions were resolved on 2026-08-19 (§6). Changes in v1.1: index export in **CSV, Excel (XLSX), XML, and JSON**; the SQLite database elevated to a **first-class, documented, reusable deliverable**; **Trash with 30-day retention** committed to v1.

---

## 1. Executive Summary

FG Scanner is an installable, open-source Windows desktop scanning application that matches NAPS2's scanning feature set and adds a **document indexing layer** NAPS2 deliberately does not have: per-profile index files (CSV / Excel / XML / JSON) with typed user-defined fields, OCR-to-Markdown sidecar files, Google AI image descriptions, and retro-processing of already-scanned folders.

Five deep-research streams (NAPS2 internals & licensing, scanning-driver stack, OCR & Google AI, packaging & infrastructure, competitor feature analysis) were completed on 2026-08-19. The findings converge on one clear plan:

| Decision | Choice | One-line reason |
|---|---|---|
| Language / runtime | **C# on .NET 10 (LTS, supported to Nov 2028)** | The only maintained, legally-friendly TWAIN/WIA/eSCL stack on the planet is a .NET library; Python has no viable TWAIN path |
| Scanning layer | **NAPS2.Sdk 1.3.0 (LGPL-2.1) + 32-bit TWAIN worker** | Wraps WIA + TWAIN + eSCL in one API; years of driver edge-case handling for free; legal to use from an MIT app |
| UI framework | **WPF** | Built-in virtualized editable DataGrid (the metadata grid is our core UI), mature imaging, Fluent light/dark theme |
| Build strategy | **New app on the SDK — NOT a fork of NAPS2** | NAPS2's app layer is GPL-2.0 (would force GPL forever); the SDK is LGPL; forking also ties us to Eto.Forms/WinForms UI |
| Data store | **SQLite (EF Core 10) as source of truth; index files (CSV/XLSX/XML/JSON) regenerated as exports** | A flat file alone cannot survive Excel locks, crashes, appends, renames; every serious capture product keeps a DB behind its index files. The DB stores *all* work (fields, OCR text, AI descriptions, statuses, history) and is a documented, reusable asset in its own right |
| OCR | **Tesseract 5.5 via shell-out** (binaries from `NAPS2.Tesseract.Binaries` NuGet, Apache-2.0) | Same approach as NAPS2 and OCRmyPDF; process isolation; one pass emits searchable PDF + hOCR + TSV |
| AI descriptions | **Google `gemini-2.5-flash-lite` via official `Google.GenAI` SDK, user-supplied API key** | ~$0.21 per 1,000 pages; thinking off by default; user's own key = user's own quota, bill, and privacy relationship |
| Installer | **Inno Setup 7** (+ MSI later for enterprises), **NetSparkleUpdater** auto-update, **SignPath Foundation** free code signing | Exactly the combination NAPS2 converged on after 15 years |
| License | **MIT** (Apache-2.0 acceptable alternative) | Maximum adoption; compatible with every dependency chosen |

**Build order:** 10 phases, each a shippable vertical slice, with copy-paste-ready Claude Code prompts in §10.

---

## 2. What Will Be Delivered (Scope Summary)

**In scope (v1.0):**

1. Scan from TWAIN, WIA, and eSCL (network) scanners — flatbed, feeder, duplex — with NAPS2-style profiles.
2. Image editing: rotate, crop, deskew, brightness/contrast, blank-page removal, reorder, delete, undo.
3. Export: PDF (incl. PDF/A and searchable text layer), TIFF/JPEG/PNG, print, email, file-naming placeholders.
4. **Groups:** scan batches into a chosen/created directory whose name is the Group.
5. **Index schema per profile:** 4 required columns + up to 12 user-defined fields typed as text, date, number (+ recommended: pick-list).
6. **Index file in each group directory — in the format(s) each audience needs: CSV (default), Excel (.xlsx), XML, and/or JSON**, selectable per profile — Excel-safe, atomic, append/update-capable, all regenerated from the SQLite database.
7. **Field data entry before or after scanning**, with sticky values and defaults.
8. **OCR to `.md`** sidecar file per image (same base name), plus searchable PDF.
9. **Google AI description** (≤1,000 chars) per image, durable retry queue, cost estimate before running.
10. **Add a missed page** to an already-scanned group; index files update correctly.
11. **Retro-process an existing directory** of images/PDFs: OCR + AI + index as if freshly scanned.
12. **Trash with 30-day retention:** deleted pages (and `.md` files replaced by re-OCR) go to an in-app Trash, restorable for 30 days (configurable) before permanent purge.
13. Installable via signed Inno Setup installer; auto-update; portable ZIP; winget.

**Out of scope for v1.0** (candidate v2 features listed in §7): cloud destinations, zonal OCR, barcode-driven field capture, auto-classification, multi-user server features, Mac/Linux builds.

---

## 3. Technology Decision (Why .NET 10, Not Python)

You asked for deep research rather than a preference-based pick. The decision is driven entirely by TWAIN reality on Windows:

- Many vendor TWAIN drivers are still **32-bit only** (HP's TWAIN is 32-bit only; Canon is inconsistent). A 64-bit app cannot load them. Every serious scanning app therefore runs TWAIN in a **separate 32-bit worker process** over IPC.
- **NAPS2.Sdk is the only actively maintained library in any language** that ships this worker model plus WIA plus eSCL, under a license (LGPL-2.1) that permits use in an MIT app. NuGet 1.3.0 published 2026-07-20; repo last commit 2026-08-10; targets .NET 10.
- **Python's best TWAIN option (`pytwain`) is GPLv2, has no worker model, and its own docs recommend running 32-bit Python** as the workaround for invisible scanners. There is no maintained Python eSCL library. The easy Python WIA path (Automation layer) is documented by Microsoft as **incapable of duplex**. Python packaging adds PyInstaller antivirus false-positives and 100 MB+ bundles.
- .NET 10 is **LTS (supported to Nov 14, 2028)**; your machine already has SDK 10.0.300 installed.

Decision matrix (1–5, higher better):

| Criterion | .NET 10 | Python |
|---|---|---|
| TWAIN access | 5 | 1 |
| eSCL / network scanning | 5 | 1 |
| GUI for grid + thumbnails + viewer | 5 | 4 |
| Packaging / installer / AV reputation | 5 | 2 |
| OCR integration | 5 | 4 |
| Large-batch image performance | 5 | 3 |
| AI/HTTP API features | 4 | 5 |
| Open-source licensing friendliness | 5 | 4 |
| Long-term maintenance risk | 5 | 2 |

**UI framework: WPF.** The core new UI is a data-entry grid bound to a dynamic schema — WPF's virtualized, editable `DataGrid` handles this out of the box. WinUI 3 has no first-party DataGrid (and the community one was archived Feb 2026). Avalonia is the fallback if cross-platform ever becomes a requirement — the scanning layer (NAPS2.Sdk) is UI-framework-agnostic, so that door stays open.

**Fork vs. SDK — important licensing finding:** the NAPS2 *application* (all its UI, profiles, batch, export orchestration) is **GPL-2.0-or-later** — forking it means FG Scanner is GPL forever and none of its code can ever move to a permissive license (no CLA exists upstream, so relicensing is impossible). The *SDK* is **LGPL-2.1-or-later**, and the author explicitly endorses using it from any app, including closed-source. We therefore **write our own app** (MIT) on top of the SDK, reading NAPS2's source only for reference (its UI text, registry integration patterns, and test seams are documented in this plan's research, re-implemented rather than copied).

Key packages (all verified current, Aug 2026):

| Package | Version | License | Role |
|---|---|---|---|
| NAPS2.Sdk (+ Worker.Win32, Images.Gdi) | 1.3.0 | LGPL-2.1+ | Scanning (WIA/TWAIN/eSCL) |
| NAPS2.Tesseract.Binaries | 1.4.0 | Apache-2.0 | Tesseract 5.5 executables (x86/x64/arm64) |
| PDFsharp | 6.2.4 | MIT | PDF assembly/merge/metadata/encryption |
| PDFtoImage | 5.3.x | MIT | Render imported PDFs to images |
| Google.GenAI | 1.18.0 | Apache-2.0 | Gemini API (official SDK, weekly releases) |
| Microsoft.EntityFrameworkCore.Sqlite | 10.0.x | MIT | Data store |
| CsvHelper | current | MS-PL/Apache | CSV writing |
| ClosedXML | current | MIT | Excel .xlsx index export (verify version at phase 3) |
| ZXing.Net | 0.16.x | Apache-2.0 | Barcode/patch-code (phase 10) |
| xunit.v3, NSubstitute, AwesomeAssertions, Verify, FlaUI | current | permissive | Testing |

**Avoid list (licensing/maintenance traps found in research):** iText (AGPL) · FluentAssertions ≥8 (went commercial — use AwesomeAssertions) · `NAPS2.Images.ImageSharp` (drags in Six Labors Split License — use `NAPS2.Images.Gdi`) · charlesw `Tesseract` NuGet (stale, x64-only, breaks on modern publish) · Emgu.CV (GPL — use OpenCvSharp4 if CV needed) · LiteDB (v6 in prerelease 3 years) · System.Data.SQLite (legacy) · **EPPlus ≥5 (PolyForm Noncommercial license — use ClosedXML for XLSX)**.

---

## 4. Licensing & Distribution Strategy

- **FG Scanner license: MIT.** ✅ Decided (#1, 2026-08-19).
- LGPL obligations for NAPS2.Sdk: keep its DLLs as separate files (never merge/single-file-bundle them), ship the LGPL text, state usage in an About box + THIRD-PARTY-NOTICES.md. All satisfied by our normal build setup.
- **Ship a THIRD-PARTY-NOTICES.md** (NAPS2 itself has none — we assemble our own: NAPS2.Sdk LGPL-2.1, Tesseract Apache-2.0, Leptonica BSD-2, PDFium BSD-3, PDFsharp MIT, etc.).
- **Code signing:** apply to **SignPath Foundation** (free for OSS). Two requirements to design in from day one:
  1. A **privacy policy shown during install** with an **opt-out for the Gemini feature** (scanned content leaves the machine — SignPath requires this; it's also simply correct).
  2. All components under OSI licenses without commercial dual-licensing — our chosen stack complies.
  Fallback: Azure Artifact Signing at $9.99/month (GA Jan 2026, individuals eligible in US/CA). **Do not buy an EV cert — Microsoft confirmed EV no longer bypasses SmartScreen.**
- **Google AI privacy (critical):** the Gemini **free tier trains on submitted content and allows human review — never acceptable for users' scanned documents.** The app will (a) default the AI feature off, (b) require the user's own API key (BYO-key), (c) display a clear notice that pages are sent to Google under *their* API terms, (d) recommend paid tier. EEA/UK/Switzerland users are contractually required to use paid tier.

---

## 5. Product Specification

### 5.1 Domain model

Adopt the three-level hierarchy every professional capture product uses (Kofax, PaperStream, Epson Document Capture Pro):

```
Profile ──has one── IndexSchema (4 required + ≤12 custom typed fields, versioned)
Group  (= batch = directory)  ── lifecycle: Scanning → Indexing → Committed
 └─ Document  (= one CSV row; immutable GUID; ordered pages)
     └─ Page  (= one image file; checksum; sequence; OCR/AI status)
```

- **Group:** the directory. Pick an existing directory (its name becomes the Group) or create one. Stores profile reference + version, group-level default values, state, and the exported `index.csv` + `manifest.json`.
- **Document:** the unit an index row describes. ✅ Decided (#2): the v1 UI treats **1 image = 1 row** (as originally specified), so document == page and the model is invisible to the user — but the schema supports multi-page documents from day one, because "staple these 3 scans together" is the first feature request every capture product received, and retrofitting it into a flat file is painful. Multi-page document UI is a v2 feature.
- **Page:** image file + immutable ID + SHA-256 checksum (survives renames) + sequence number + per-page OCR/AI state.

**Source of truth:** a **central SQLite database** (`fgscanner.db` in `%APPDATA%\FGScanner`) plus a `manifest.json` in each group folder so folders remain understandable on their own, with a "reconcile group" command that re-syncs after outside changes. ✅ Decided (#3). Index files are **regenerated in full on every commit** — atomic (write temp → rename), so a crash or an Excel lock can never corrupt them.

**The database is a first-class deliverable, not an implementation detail.** Everything the app ever produces is stored there for later reuse: profiles and schema versions, every group/document/page record, all custom field values, the full OCR text (searchable via FTS5), AI descriptions with status and token usage, checksums, timestamps, operator identity, and the group event journal. Commitments that make it genuinely reusable:
- **Documented schema** in `docs/db-schema.md`, updated with every migration (a CI check keeps it honest).
- **Stable read views** (`v_index`, `v_pages`, `v_ocr_text`) that survive internal refactors — query them from any SQLite tool (DB Browser for SQLite, Python, Power BI, Excel's ODBC) without knowing the app's internals.
- **Backups:** automatic copy before every schema migration; a "Back up database…" menu item; the DB file is safe to copy while the app is closed (single file + WAL).
- The JSON/XML index exports (§5.2) double as a machine-readable extraction path if you ever want the data without touching SQLite.

### 5.2 The index file — CSV, Excel, XML, JSON (hardened specification)

Your requirement, made precise enough to code:

- **Formats (per-profile, any combination; CSV on by default):** different audiences need different files, so each profile selects which index formats to emit on commit — all generated from the same database rows, all written atomically:
  - `index.csv` — the universal default (spec below).
  - `index.xlsx` — real Excel workbook via **ClosedXML (MIT)**: typed cells (dates as dates, numbers as numbers), frozen header row, auto-filter — completely immune to Excel's silent CSV date/number mangling. The right choice for people who live in Excel.
  - `index.xml` — `<fgIndex group="…" profile="…" schemaVersion="…"><document id="…"><field name="…" type="…">value</field>…</document></fgIndex>`, with an XSD published in the repo. For enterprise/archival systems that ingest XML.
  - `index.json` — `{ "manifest": {…}, "rows": [ {…}, … ] }` (manifest embedded, ISO dates, UTF-8). For scripts, web tools, and developers.
- **Location:** `<group directory>\index.<ext>` (base name configurable per profile).
- **CSV format:** UTF-8 **with BOM** (Excel requires the BOM to detect UTF-8), CRLF line endings, RFC 4180 quoting (fields containing comma/quote/newline are quoted; `"` escaped as `""`). AI descriptions **will** contain commas and newlines — quoting is mandatory, not optional. Optional per-profile delimiter (`,` default; `;` for European Excel locales).
- **CSV-injection protection:** any cell beginning with `=`, `+`, `-`, `@` is prefixed with `'` on export (OCR/AI text is attacker-influenced — someone can print a malicious formula on a paper you scan).
- **Excel-lock handling:** if `index.csv` is locked, the commit succeeds in the database and the app shows "index.csv is open in another program — will retry," retrying with backoff until the export lands. Data is never lost to a lock.
- **Required columns (always first, in order):**
  1. `Group` — the directory name (auto-filled).
  2. `ImageName` — file name of the image (or first image, for multi-page documents).
  3. `OCRed` — per-row **state**, not a wish: `Yes` / `No` / `Failed` / `Pending`. ✅ Decided (#4): whether OCR *should* run is a profile setting; the column reports what actually happened — "Yes" that silently failed is worse than "Failed".
  4. `AIDescription` — the ≤1,000-character description text itself, or empty; a separate `AIStatus` column (`Done/Failed/Pending/Skipped/Off`) records state. ✅ Decided (#5): the Yes/No from the original spec is the *profile toggle*; the index carries the actual description text (that's what makes it useful) plus the status column.
- **User-defined columns:** up to **12** additional fields per profile. Types: `text`, `date`, `number`, **`list`** (pick-list with fixed choices; prevents "Inv"/"invoice"/"INV." chaos), each with optional `required` / `default` / `sticky` flags. ✅ Decided (#6).
- **Data formats:** dates stored/exported **ISO-8601 (`YYYY-MM-DD`)** — Excel's silent date mangling ("12/2" → a date, leading zeros stripped) cannot be turned off, so unambiguous ISO text is the only safe export; numbers exported with invariant `.` decimal separator (or locale-matched if delimiter is `;`).
- **Append & update:** "add a missed page" inserts the row in the correct position (order lives in the database, not in file names) and re-exports every enabled format. Re-running OCR/AI updates the existing row (matched by internal ID, not by file name).
- **Trash (30-day retention, v1 commitment):** deleting a page removes its row and moves the image plus its `.md` sidecar to an in-app Trash; re-OCR sends the replaced `.md` there too. A Trash view lists items with origin group and deletion date, supports **Restore** (row and files return, index re-exported) and **Delete permanently**; a background job purges items older than the retention period (default 30 days, configurable in Settings). Nothing is ever hard-deleted directly from a group.
- **`manifest.json`** exported beside the index files: profile name + schema version + field definitions + enabled formats + app version — so any other program (or future you) can interpret the exports without guessing.

### 5.3 Profiles and index schemas

- A profile = NAPS2-style scan settings (device, source glass/feeder/duplex, DPI, color/gray/BW, page size, brightness/contrast, blank-page removal, deskew, quality) **plus** the index schema **plus** the pipeline toggles (OCR on/off + language(s), AI description on/off, searchable-PDF on/off).
- Different profiles → different CSV layouts. A group remembers which profile (and schema version) created it; opening a group with a different profile warns about mismatched headers.
- **Schema evolution:** adding a field to a profile bumps its schema version. Re-opening an old group offers: "Export with new layout (new columns empty for old rows)" or "Keep old layout." Renaming/retyping a field warns that existing data is remapped/frozen. (Competitors that ignored this — Docspell documents stranded values — taught us to define it up front.)
- Profiles are export/importable as `.fgprofile` JSON files (shareable between machines).

### 5.4 Data entry workflow

- **Before scanning:** open the group, fill the field grid for the next document(s), then scan — values attach as pages arrive. **Sticky fields** carry values forward from the previous row (toggle per field). **Defaults** support tokens: `$(today)`, `$(group)`, `$(counter)`, `$(user)`.
- **After scanning:** grid view of all rows with thumbnail beside the fields; keyboard-first (Enter = next field, auto-advance to next row); invalid fields highlighted with the reason; required fields block **Commit**, warnings don't.
- **Commit step:** nothing hits `index.csv` until the user commits the group (review screen: thumbnails + rows + validation summary). After commit, the group is marked Committed; further edits require reopening it (state change is logged).

### 5.5 OCR pipeline (the `.md` files)

- Tesseract 5.5 (`NAPS2.Tesseract.Binaries`), executed as child processes: `tesseract <image> <out> --dpi <dpi> --oem 1 --psm 3 pdf hocr tsv` — one recognition pass produces the searchable PDF, hOCR, and TSV simultaneously.
- Language packs: `tessdata_fast` (4 MB/language; research shows accuracy within 0.01 % of `best` at ~1.9× the speed), downloaded on demand like NAPS2 does; English bundled.
- Parallelism: `OMP_THREAD_LIMIT=1` per child + one process per physical core (3–6× throughput vs. default).
- **Markdown generation (Tier 0, always available, offline):** a geometric reconstructor over the TSV — headings from line-height percentiles, lists from marker patterns + hanging indents, paragraphs from gap analysis, column detection from ink-projection valleys. (Tesseract's LSTM engine reports **no font/bold/size data** — this geometric approach is the correct one, confirmed by research.) Tables are emitted as fenced preformatted blocks in v1.
- `.md` file: same base name as the image, YAML front matter recording engine, mean confidence, duration. Written next to the image (per your spec).
- Low-confidence pages (mean word confidence below threshold, default 65) are flagged in the UI for review rather than silently marked OCRed.
- **Optional Tier 1 (later phase):** local GLM-OCR model (0.9 B params, 2.2 GB, MIT license, runs via Ollama) produces true structured Markdown incl. HTML tables, fully offline — as an optional download. **Optional Tier 2:** Azure Document Intelligence Layout (~$10/1,000 pages) for cloud-quality Markdown. Both slot behind the same interface; neither blocks v1.

### 5.6 AI image description

- Provider: Google Gemini via the **official `Google.GenAI` .NET SDK**; default model **`gemini-2.5-flash-lite`** (thinking off by default — on Gemini 3 models thinking tokens can silently multiply cost 5–10×, so the model choice is deliberate; model ID is a setting).
- **Cost: ≈ $0.21 per 1,000 pages** (a Letter/A4 page ≈ 1,032 image tokens; ~250 output tokens). 5,000 pages ≈ $1.04 interactive, ≈ $0.52 via Batch API. The app shows an **estimate before every AI run** and tracks actual spend from response usage metadata.
- **Key management:** user pastes their own AI Studio API key (Settings → AI). Stored in **Windows Credential Manager** (user-visible, revocable), never logged, redacted from diagnostics, "Clear stored key" button. No key → feature hidden. First enable shows the privacy notice (§4).
- **Reliability:** durable per-page queue in SQLite (`Pending → InFlight → Done | Failed(n≤3) | Skipped`); survives restarts; auto-resumes when online; never blocks scanning/OCR/export; passive "N pages awaiting description" indicator. Exponential backoff + jitter on 429/5xx; on the first 429 the global concurrency halves.
- **Quality controls:** prompt asks for ~700 characters covering document type → legible names/letterhead → dates/reference numbers → subject → physical characteristics; "do not guess, do not transcribe"; `BLANK PAGE` sentinel; `maxOutputTokens: 400`; **code-enforced truncation at a sentence boundary ≤1,000 chars** (LLMs cannot count characters — the limit is enforced in C#, the prompt only aims). Pages where Tesseract found <5 words skip the API call entirely (5–15 % savings).
- Safety: current Gemini models default safety filters to off for these categories; the app still checks `finishReason` and records `Failed(safety)` gracefully.
- The provider sits behind `IChatClient` (Microsoft.Extensions.AI) — a local Ollama vision model can be added later as a fully offline option without touching call sites.

### 5.7 Retro-processing an existing directory

- "Process Existing Folder…": pick a directory → it becomes (or updates) a Group → images (and PDFs, rendered to pages) are registered exactly as if scanned → the same pipeline applies (field entry, OCR, AI, index export, commit).
- **Duplicate detection by checksum:** a file already registered (even under a new name) is recognized and not double-rowed; a warning lists duplicates.
- **Reconcile:** re-running on a group directory reports rows whose files vanished and files that have no row, and offers fixes.
- Re-process options per run: skip already-OCRed / redo OCR (replaced `.md` goes to Trash) / only failed AI / all — so a better model or fixed setting can be applied selectively.
- Optional **watch folder** per profile (v2, §7): drop files into `<group>\inbox\`, pipeline runs automatically.

### 5.8 NAPS2 feature-parity checklist

Research produced a complete inventory of NAPS2 8.3.2. FG Scanner v1 commitments, by area — **[F]** = full parity, **[P]** = partial (noted), **[D]** = deliberately different, **[V2]** = deferred:

| Area | Status | Notes |
|---|---|---|
| WIA driver (1.0/2.0 selectable) | [F] | via NAPS2.Sdk |
| TWAIN (32-bit worker, DSM options, transfer modes) | [F] | via NAPS2.Sdk worker |
| eSCL network scanning incl. manual IP | [F] | via NAPS2.Sdk |
| SANE / Apple ICA | [D] | Windows-only app; not applicable |
| Device picker, capability-aware profile UI | [F] | |
| Profiles: source/DPI (incl. custom)/bit depth/page size (incl. custom presets)/align/scale/brightness/contrast | [F] | |
| Advanced: quality/max-quality, blank-page thresholds, deskew, WIA offset, force page size/crop, flip duplex, delay between scans | [F] | |
| Auto-save with placeholders + separators | [F] | placeholder engine extended with metadata tokens |
| Native scanner UI passthrough | [P] | TWAIN native UI via SDK; low priority |
| Keyboard shortcuts (rebindable; F2–F12 profiles, Ctrl+Enter scan…) | [F] | same defaults as NAPS2 |
| Editing: crop, brightness/contrast, hue/sat, BW threshold, sharpen, split, combine, rotate/flip/deskew/custom angle | [F] | via SDK transforms |
| Document correction mode, edit-with-external-app | [P] | v1.1 |
| Reorder: move/interleave/deinterleave/reverse; manual duplex | [F] | |
| Undo/redo, thumbnails w/ size + page numbers, drag-drop reorder, preview window | [F] | |
| PDF export: PDF/A-1b/2b/3b/3u, metadata, encryption + 8 permission flags | [F] | PDFsharp + Tesseract text layer |
| Searchable PDF via OCR | [F] | better: also emits .md (NAPS2 can't export OCR text at all) |
| Image export: JPEG/PNG/TIFF (Auto/LZW/CCITT4/None) /BMP, multi-page TIFF, quality | [F] | |
| Split output: per page / per scan / Patch-T / every N pages | [F] | Patch-T in phase 10 with barcode work |
| Email (MAPI/SMTP/Gmail/Outlook/Thunderbird), print, clipboard, drag-out | [P] | v1: MAPI default client + print + clipboard; OAuth providers v1.1 |
| Import PDF (incl. password) / images / ZIP; file associations; "Scan with…" AutoPlay + StillImage button | [F] | Import feeds the retro-process pipeline |
| OCR: 100+ languages on-demand, fast/best, multi-language, after-scan mode, timeout | [F] | |
| Batch scan dialog (single/multi-prompt/multi-delay, count, interval) | [F] | integrated with Groups |
| CLI (NAPS2.Console parity: ~60 options) | [P] | v1: scan/import/output/OCR/profile flags + FG additions (`--group`, `--write-index`, `--ai`); full parity v1.1 |
| Profiles.xml-style admin config, setting lock-down | [P] | JSON equivalent; enterprise lockdown v2 |
| Crash recovery (scratch folder + lock + index) | [F] | same proven design |
| Session restore, single instance, update check | [F] | |
| Localization (46 languages) | [P] | architecture localized from day 1 (resx); ship English, accept community translations |
| Scanner sharing (ESCL server) | [V2] | SDK supports it; not core to FG's mission |
| Dark/light theme | [F] | WPF Fluent ThemeMode |
| Portable ZIP build | [F] | |
| MS Store listing | [V2] | |

Known NAPS2 pain points FG Scanner fixes by design: no OCR text export (#765) → our `.md` files; no metadata prompt at save (#113) → our index grid; no post-save hook (#106, top-voted) → our "run command after commit" hook (§7 A34); PDF embedded at wrong DPI (#843) → correct DPI embedding is an explicit test case; concurrent autosave name collisions (#823) → DB-issued names.

---

## 6. Consolidated Decisions — RESOLVED 2026-08-19

All ten open decisions were reviewed and the recommendations accepted as written. They are now binding for the build:

| # | Decision | Resolution |
|---|---|---|
| 1 | App license | **MIT** |
| 2 | v1 rows | **1 image = 1 row**; schema supports multi-page documents, UI for them is v2 |
| 3 | Database layout | **Central SQLite DB** + `manifest.json` per group folder |
| 4 | `OCRed` column | **Yes / No / Failed / Pending** states |
| 5 | AI column | Index carries the **description text** + `AIStatus`; Yes/No is the profile toggle |
| 6 | Field system | **`list` type + required/default/sticky** added to text/date/number |
| 7 | App name | **FG Scanner** |
| 8 | GitHub repository | **Public from day 1** (enables SignPath free signing + winget) |
| 9 | AI provider | **Gemini-only v1**, `IChatClient` seam for Ollama/Claude later |
| 10 | Keyboard shortcuts | **NAPS2 defaults** kept |

Post-review additions (2026-08-19, incorporated throughout this v1.1 document):

| # | Addition | Where |
|---|---|---|
| 11 | Index export in **CSV, Excel (XLSX), XML, and JSON**, selectable per profile | §5.2, phases 3 & 10, Prompt 3 |
| 12 | **SQLite DB as a first-class reusable deliverable**: documented schema, stable views, backups | §5.1, Prompt 2 |
| 13 | **Trash with 30-day retention** fully committed to v1 | §5.2, Prompt 3 |

---

## 7. Features You May Have Missed (Research-Grounded)

Direct prior art exists for your core idea: **Epson Document Capture Pro** is the only mainstream desktop product with per-profile typed index fields written to CSV/XML (with append mode and per-document rows) — and its weaknesses (opaque format options, model-gated features, no database behind the file, Windows-only docs) plus Canon CaptureOnTouch's broken CSV writer define exactly the gap FG Scanner fills. *Correct, documented, database-backed CSV is itself a selling point.*

**Adopted into the v1 spec above** (from the research's top-10 plus the 2026-08-19 review): SQLite behind the index files · document/page/group model · pick-list + required + sticky fields · AI/OCR status columns with retry queue and cost estimate · blank-page policy · post-scan review/commit screen · checksum duplicate detection + reconcile · CSV hardening · **XLSX/XML/JSON index formats** · **Trash with 30-day retention** · metadata filename tokens with slugify.

**Recommended v1.1–v2 backlog** (what it is — who does it — effort S/M/L):

*Capture*
1. Patch-T + barcode document separation with printable separator sheets — NAPS2/Epson DCP/Paperless-ngx — M
2. Barcode value → index field (a barcode on the page fills a column) — Epson DCP, Kofax — M
3. Multi-feed/page-count verification (expected vs. scanned) — Kofax QC — M
4. Rescan-in-place ("replace this page") — Kofax — S
5. Double-sided collation for simplex scanners (odd pass + even pass, auto-interleave) — Paperless-ngx — M

*Indexing*
6. Lookup auto-fill from a CSV/database table (enter customer #, address fills itself) — Kofax Database Validation — M
7. Batch-level fields stamped on every row (box number, operator) — Kofax — S
8. Per-field zoom that follows data entry (image region shown while typing) — Kofax Express — M
9. Tags column (multi-valued, `;`-separated) — Paperless-ngx — S
10. Operator identity per row (Windows username) — Kofax — S

*OCR / AI*
11. Zonal OCR → field (draw a box once per profile) — Epson DCP, Kofax Express — L
12. AI field extraction (AI fills the 12 columns, human confirms) — Rossum, Azure DI — L
13. Auto-classification picks the profile from the document image — Azure DI custom classifier — L
14. Confidence-threshold review queue (auto-accept above, human below) — Rossum (0.975 default) — M
15. Local VLM description via Ollama (no cloud) — Docling picture-description — L
16. Gemini Batch API mode for big backfills (50 % cost) — M
17. Handwriting detection warning (route to AI path) — Azure DI — M

*Output / automation*
18. ~~XML/JSON index output alongside CSV~~ — **moved into v1 (§5.2)**
19. Webhook / run-command on group commit — Paperless-ngx workflows, NAPS2 #106 — S
20. Watch folders (drop file → pipeline runs) — Paperless-ngx consumption dir — M
21. ~~`.xlsx` export with real cell types~~ — **moved into v1 (§5.2)**
22. Saved searches + full-text search UI over OCR text (SQLite FTS5 — the index is already there) — M
23. Rules engine ("if OCR contains X, set field Y") — Paperless-ngx, Hazel — L

*Reliability / compliance*
24. ~~Trash with 30-day retention for deleted pages~~ — **fully committed to v1 (§5.2)** per 2026-08-19 review
25. Audit log of field changes (who/when/from/to) — Paperless-ngx, Teedy — M
26. Retention rules ("delete originals N days after commit") — S
27. PII-flagged documents excluded from cloud AI — L
28. Verification/double-key pass for high-stakes archives — Kofax — L
29. Productivity stats (pages/hour, auto-accept rate) — Rossum, Kofax Express — M

---

## 8. Architecture

```
┌─ FgScanner.App (WPF, MIT) ──────────────────────────────────┐
│  Views: Scan | Groups | IndexGrid | Review/Commit | Settings │
│  MVVM (CommunityToolkit.Mvvm), Microsoft.Extensions.Hosting  │
├─ FgScanner.Core (no UI deps) ───────────────────────────────┤
│  Domain: Profile, IndexSchema, Group, Document, Page         │
│  Services: GroupService, IndexExporter (CSV·XLSX·XML·JSON),  │
│            NamingEngine, TrashService, PipelineOrchestrator, │
│            JobQueue (SQLite-backed)                          │
├─ FgScanner.Scanning ────────────────────────────────────────┤
│  IScanService → NAPS2.Sdk (ScanController, Win32 worker)     │
│  FakeScanService for tests (canned ProcessedImage lists)     │
├─ FgScanner.Ocr ─────────────────────────────────────────────┤
│  TesseractRunner (process pool) → TSV/hOCR/PDF               │
│  MarkdownReconstructor (geometric, Tier 0)                   │
├─ FgScanner.Ai ──────────────────────────────────────────────┤
│  IDescriptionProvider → GeminiProvider (Google.GenAI)        │
│  CredentialStore (Windows Credential Manager), CostEstimator │
├─ FgScanner.Data ────────────────────────────────────────────┤
│  EF Core 10 + SQLite; JSONB column for custom field values;  │
│  FTS5 virtual table for OCR text; migrations + backup        │
└─ FgScanner.Cli (console) ───────────────────────────────────┘
```

Key patterns (proven in NAPS2, confirmed by research):
- **Images live on disk, not in memory or IPC messages** — scratch/recovery folder with lock-file crash detection.
- **Hardware seam:** one `IScanService` interface; production implementation wraps NAPS2.Sdk; tests use a fake returning canned images. All business logic is testable without a scanner.
- **Durable queues:** OCR and AI jobs are SQLite rows, not in-memory tasks — restartable, visible, retryable.
- **Index files are projections:** any change (missed page, re-OCR, edit, restore from Trash) mutates the DB and triggers an atomic re-export of every enabled format (CSV/XLSX/XML/JSON) through one `IndexExporter` pipeline with pluggable format writers.

Publish configuration: self-contained, ReadyToRun, `win-x64`, **not** single-file, **not** trimmed (WPF cannot trim; single-file would break LGPL separation and native DLL loading).

---

## 9. Build Order — Ten Phases

Each phase is a vertical slice ending in something runnable, with tests green and the installer still building. Estimated at solo-developer pace with Claude Code; adjust freely.

| # | Phase | Contents | Exit criteria |
|---|---|---|---|
| 0 | **Walking skeleton** | Repo (`src/tests/docs/build`, .slnx, Directory.Build.props, CPM, analyzers, CLAUDE.md, CI: build+test+CodeQL), empty WPF shell with DI/logging/theme, publish profile, Inno installer stub, GitHub Release workflow | Installer from CI installs and launches an empty window; `dotnet test` green in CI |
| 1 | **Scan core** | NAPS2.Sdk integration: device list (WIA/TWAIN/eSCL), minimal profile, scan → images on disk, thumbnail list, crash-recovery folder, FakeScanService + tests | Real scanner produces pages in the UI; kill -9 mid-scan recovers pages on restart |
| 2 | **Domain & storage** | EF Core schema (Profiles, IndexSchemas+versions, Groups, Documents, Pages, Jobs), JSONB custom fields, FTS5 table, migrations + backup-on-migrate, group create/open (directory ↔ group), checksums | Unit + migration-fixture tests green; create group, scan into it, reopen after restart |
| 3 | **Index schema & exports** | Schema editor (12 typed fields: text/date/number/list, required/default/sticky), data-entry grid (before & after scan), validation, review/commit screen, IndexExporter with four writers — CSV (BOM/RFC4180/injection-escape), XLSX (ClosedXML, typed cells), XML (+XSD), JSON — all atomic with lock-retry, per-profile format selection, manifest.json, add-missed-page, **Trash view with restore + 30-day purge** | Golden-file snapshot tests for all four formats; Excel-open-lock test; append + reorder + restore-from-trash scenarios pass |
| 4 | **Editing & export parity** | Transforms (rotate/crop/deskew/brightness/…), undo/redo, reorder, PDF export (PDF/A, metadata, encryption), TIFF/JPEG/PNG, naming placeholder engine (+ `$(group)`, `$(field:X)`), print, import PDF/images | Parity checklist §5.8 items for editing+export ticked; PDF snapshot tests (scrubbed) |
| 5 | **OCR pipeline** | TesseractRunner (ArgumentList, OMP_THREAD_LIMIT=1, pool, timeout/kill), language pack downloader, TSV→Markdown reconstructor, .md sidecars with front matter, searchable PDF integration, durable OCR queue, confidence flagging, OCRed column states | Scan → .md + searchable PDF; low-confidence page flagged; queue survives restart |
| 6 | **AI descriptions** | Google.GenAI integration behind IChatClient, BYO-key settings + Credential Manager + privacy notice, durable AI queue with backoff, cost estimator + spend tracking, blank-page short-circuit, sentence-boundary truncation, CSV columns | Mocked-HTTP tests for all failure modes; live smoke test ≤$0.01; estimate shown before run |
| 7 | **Retro-processing** | Process Existing Folder (images + PDFs→pages), duplicate detection, reconcile command, selective re-run (skip done / redo / only failed) | Point at a folder of old scans → correct CSV + .md + descriptions; second run is idempotent |
| 8 | **Batch & CLI** | Batch scan dialog (modes/count/interval), profile import/export, keyboard shortcuts, CLI (`fgscanner scan -p X --group Y -o ... --write-index --ocr --ai`), single instance, session restore | CLI drives a full scan-to-index run headless; shortcut map matches NAPS2 defaults |
| 9 | **Ship it** | Installer complete (associations, AutoPlay/StillImage, per-machine), NetSparkle auto-update + appcast, SignPath signing in CI, winget manifest, first-run wizard, privacy policy page, THIRD-PARTY-NOTICES, user docs, portable ZIP | Signed installer on GitHub Releases; update from n−1 works; winget PR submitted |
| 10 | **Differentiators (v1.1)** | Patch-T/barcode separation + printable sheets, blank-page policies, FTS search UI, webhook-on-commit, batch-level fields, watch folder | Each behind its own flag; backlog §7 groomed for v2 |

Dependency notes: 3 depends on 2; 5–7 depend on 2–3; 4 can run parallel to 3; 8 needs 1+3; 9 hardens everything; 10 is optional scope.

---

## 10. Claude Code Prompts (Copy-Paste, One Per Phase)

Ground rules baked into every prompt: plan mode first, TDD (superpowers skill), no scope beyond the phase, update FEATURE-PARITY.md and ADRs as decisions land. Each prompt assumes the repo contains this plan at `docs/PLAN.md` and the research digests at `docs/research/`.

> **Prompt 0 — Walking skeleton**
> Read docs/PLAN.md §3, §4, §8, §9 (phase 0) and CLAUDE.md. Create the FG Scanner solution: .slnx with projects FgScanner.App (WPF, net10.0-windows), FgScanner.Core, FgScanner.Scanning, FgScanner.Ocr, FgScanner.Ai, FgScanner.Data, FgScanner.Cli, plus matching xunit.v3 test projects under tests/. Add Directory.Build.props (nullable, analyzers, warnings-as-errors in Release), Directory.Packages.props with central package management, .editorconfig, global.json. App shell: Microsoft.Extensions.Hosting DI, Serilog logging, WPF Fluent theme with light/dark, an empty main window with placeholder navigation (Scan | Groups | Settings). Add publish profile (self-contained, ReadyToRun, win-x64, no single-file, no trim), build/installer/setup.iss Inno stub that packages the publish output, and GitHub Actions: ci.yml (build, test, dotnet format --verify-no-changes, CodeQL) and release.yml (publish → installer → GitHub Release, signing steps stubbed). Licensing guards: MIT LICENSE, THIRD-PARTY-NOTICES.md seeded per PLAN §4; never reference NAPS2.Images.ImageSharp, FluentAssertions ≥8, or iText. Acceptance: dotnet test green; installer builds locally; CI green on push. Out of scope: any scanning, any data model.

> **Prompt 1 — Scan core**
> Read docs/PLAN.md §5.8, §8 and docs/research/research-2-stack.md. In FgScanner.Scanning define IScanService (ListDevicesAsync per driver, ScanAsync(ScanProfileOptions) → IAsyncEnumerable of ScannedPage) and implement Naps2ScanService using NAPS2.Sdk 1.3.0 + NAPS2.Sdk.Worker.Win32 + NAPS2.Images.Gdi (ScanningContext.SetUpWin32Worker; WIA default, TWAIN, ESCL; gate TWAIN off on ARM64). Implement FakeScanService returning canned images from test fixtures, mirroring NAPS2's MockScanBridge pattern (docs/research/research-4-delivery.md testing section). Storage: pages written to a recovery folder (%APPDATA%\FGScanner\recovery\<random>\) with .lock file + throttled index, NAPS2-style; on startup, offer recovery of orphaned folders. UI: device picker (driver tabs, refresh), minimal profile editor (source, DPI, bit depth, page size, brightness/contrast), Scan button streaming thumbnails into a virtualized list. TDD with FakeScanService; hardware smoke-test checklist added to docs/manual-tests.md. Acceptance: scan from a real WIA or TWAIN device lands pages as files + thumbnails; process kill mid-scan recovers on restart; all logic tests pass without hardware. Out of scope: groups, CSV, OCR, editing.

> **Prompt 2 — Domain and storage**
> Read docs/PLAN.md §5.1–§5.3 and docs/research/research-4-delivery.md (data store section). In FgScanner.Data implement EF Core 10 + SQLite (Microsoft.Data.Sqlite; explicitly reference SQLitePCLRaw.bundle_e_sqlite3 3.x): entities Profile, IndexSchema (versioned) + FieldDefinition (Text|Date|Number|List; Required, Default, Sticky, ListChoices), Group (directory path, state Scanning|Indexing|Committed, profile+schemaVersion), Document (GUID, sequence), Page (file name, SHA-256 checksum, sequence, blank flag, OcrStatus, AiStatus, mean confidence), JobQueue. Custom field values: JSONB column on Document with json_extract generated-column indexing helper. Add FTS5 external-content table + triggers for OCR text via raw-SQL migration. Migrations run at startup with backup-before-migrate; keep versioned fixture .db files in tests and a migration test per fixture. GroupService: create group (create dir or adopt existing dir; sanitize/slugify names per Windows rules incl. reserved names), open group, adopt scanned pages into documents (v1: one page = one document per PLAN decision #2). The database is a first-class deliverable (PLAN §5.1): create stable read views v_index, v_pages, v_ocr_text alongside the tables; generate docs/db-schema.md from the model and add a CI check that fails when schema and doc drift; add automatic backup-before-migrate plus a "Back up database…" command. Acceptance: unit + fixture-migration tests green; scan into a new group, restart app, group reopens with pages intact; the views return correct data when queried with a raw SQLite client. Out of scope: index export, UI polish beyond a basic Groups list.

> **Prompt 3 — Index schema, exports, and Trash**
> Read docs/PLAN.md §5.2–§5.4 carefully — it is the product's core. Implement: (a) Schema editor UI per profile — up to 12 fields, four types incl. List, with Required/Default (tokens $(today) $(group) $(counter) $(user))/Sticky flags, plus per-profile selection of index formats (CSV default-on, XLSX, XML, JSON — any combination); (b) data-entry grid (WPF DataGrid, dynamic columns from schema, thumbnail pane, keyboard-first navigation, per-type editors and validation — dates ISO, numbers invariant, list = ComboBox, invalid cell highlight with reason); entry works both before scanning (pending rows attach to incoming pages) and after; (c) Review & Commit screen (rows + thumbnails + validation summary; required blocks commit, warnings don't); (d) IndexExporter in FgScanner.Core with one pipeline and four format writers, each atomic (temp+File.Replace) with lock-retry + non-blocking UI notice, column/row order Group, ImageName, OCRed, AIDescription, AIStatus, then custom fields: CsvWriter (UTF-8 BOM, CRLF, RFC 4180 quoting, configurable delimiter, formula-injection prefixing), XlsxWriter (ClosedXML — MIT, verify current version; typed cells: dates as dates, numbers as numbers; frozen header; auto-filter), XmlWriter (schema per PLAN §5.2 with an XSD committed to docs/), JsonWriter (embedded manifest + rows array, ISO dates); (e) manifest.json exporter (profile, schema version, field defs, enabled formats, app version); (f) add-missed-page flow (insert at chosen position; sequence lives in DB; re-export all enabled formats); (g) TrashService + Trash view per PLAN §5.2: delete-page moves image + .md sidecar to app trash with origin metadata; Restore returns files and row and re-exports; Delete-permanently and a background purge job honoring the configurable retention (default 30 days); re-OCR routes replaced .md files through the same trash. TDD: golden-file snapshot tests (Verify) for ALL FOUR formats incl. quoting/injection/unicode/1000-char-description cases; XLSX assertions read back typed cell values; a test that holds the CSV open (FileShare.None) and asserts retry + DB commit; append/insert/delete/restore scenarios; purge-job clock-injection test. Acceptance: full scan → enter fields → commit → correct index files in every enabled format + manifest; add missed page updates all formats; delete → restore round-trips perfectly; Excel-open never loses data. Out of scope: OCR/AI columns beyond static placeholders.

> **Prompt 4 — Editing and export parity**
> Read docs/PLAN.md §5.8 (parity table) and docs/FEATURE-PARITY.md. Implement page editing via NAPS2.Sdk transforms: rotate L/R/180/custom, flip, deskew, crop, brightness/contrast, hue/saturation, BW threshold, sharpen, split, combine; undo/redo stack (deletions excluded, matching NAPS2); reorder (move/interleave/deinterleave/reverse); apply-to-selected. Export: PDF via the SDK/PDFsharp path — PDF/A-1b/2b/3b/3u options, metadata (author/title/subject/keywords), encryption with owner/user passwords + permission flags; images JPEG (quality)/PNG/TIFF (Auto/LZW/CCITT4/None, multi-page default)/BMP; print; clipboard; drag-out. Import: PDF (incl. password) and images through the same page-adoption path as scanning. Naming engine: NAPS2 tokens $(YYYY) $(MM) $(DD) $(hh) $(mm) $(ss) $(n..nnnn) plus $(group) $(doc) $(page) $(field:Name) $(barcode reserved), slugified, collision-suffixed. Verify-based PDF snapshot tests with CreationDate/ModDate/ID scrubbers; image golden tests with tolerance. Update FEATURE-PARITY.md statuses. Acceptance: every [F] editing/export row in the parity table §5.8 demonstrably works. Out of scope: OCR text layer (phase 5), email OAuth providers.

> **Prompt 5 — OCR pipeline**
> Read docs/PLAN.md §5.5 and docs/research/research-3-ocr-ai.md (Part A). Implement TesseractRunner in FgScanner.Ocr: child processes of tesseract.exe from NAPS2.Tesseract.Binaries; ProcessStartInfo.ArgumentList only; absolute exe path; whitelist language codes ^[a-z]{3}(\+[a-z]{3})*$; TESSDATA_PREFIX + OMP_THREAD_LIMIT=1 on child env only; concurrent stdout/stderr drains; kill on timeout; pool = physical cores (SemaphoreSlim). One pass per page: pdf + hocr + tsv renderers, --dpi from scan metadata, --oem 1, --psm 3. Language manager: tessdata_fast on-demand download with SHA-256 verify (eng bundled). MarkdownReconstructor: parse TSV (level 5 words; conf −1 = structure); geometric structure — column detection via vertical ink-projection valleys with column-major reordering, headings via line-height >1.25× body median bucketed to #/##/###, lists via marker regex + hanging-indent confirmation, paragraphs via gap analysis; tables and figures emitted as fenced blocks; NO font-based heuristics (LSTM emits none). Write <imagebase>.md beside the image with YAML front matter (engine, tier, mean_confidence, duration_ms). Searchable PDF: use Tesseract's own pdf output for the text layer, merged/assembled with the phase-4 exporter (invisible text layer preserved; correct DPI so text aligns — regression test for NAPS2 issue #843's bug class). Durable OCR queue (Jobs table): survives restart, per-page status feeds the OCRed CSV column (Yes/Failed/Pending), mean-confidence <65 flags the page for review in the grid. Tests: real Tesseract against fixture images (deterministic — do not mock the engine), reconstructor unit tests on synthetic TSV, queue-restart tests. Acceptance: scan → .md + searchable PDF; text layer selectable in a PDF viewer and aligned with the ink; queue resumes after kill. Out of scope: GLM-OCR/Azure tiers (interfaces only), AI descriptions.

> **Prompt 6 — AI descriptions**
> Read docs/PLAN.md §5.6, §4 (privacy) and docs/research/research-3-ocr-ai.md (Part B). In FgScanner.Ai implement IDescriptionProvider over Microsoft.Extensions.AI IChatClient; GeminiProvider using Google.GenAI (model configurable, default gemini-2.5-flash-lite; temperature 0.2; maxOutputTokens 400; on Gemini-3 models set thinking_level minimal explicitly). Key management: Settings → AI pane; key pasted by user, validated with a 1-token test call, stored via Windows Credential Manager (CredWrite/CredRead P/Invoke; DPAPI CurrentUser fallback), never logged, "Clear stored key"; feature hidden when absent; first enable shows the privacy notice from PLAN §4 and records consent. Pipeline: durable queue (Pending→InFlight→Done|Failed(reason,n≤3)|Skipped); bounded concurrency 4 with global halving on first 429; exponential backoff + jitter on 429/408/5xx, never retry 400/403; blank-page short-circuit (Tesseract word count <5 → BLANK PAGE locally, no API call); prompt per PLAN §5.6 aiming at 700 chars; code-enforced sentence-boundary truncation ≤1000 chars; check finishReason (SAFETY/RECITATION/MAX_TOKENS) and map to Failed states; log usageMetadata per call. Cost: pre-run estimate dialog (pages × ~1,032 image tokens formula from research + output estimate, priced from a config table) and cumulative spend tracking in Settings. Index: AIDescription text + AIStatus column update on completion, re-export of all enabled formats throttled. Tests: RichardSzalay.MockHttp for success/429-retry/safety-block/truncation/max-tokens/network-loss; queue persistence tests; NO live API calls in CI; one manual smoke-test doc entry (≤$0.01). Acceptance: with a real key, a scanned group gets descriptions; pulling the network mid-run leaves a resumable queue; estimate within 20% of actual on the smoke test. Out of scope: Ollama provider (leave IChatClient seam), batch API mode.

> **Prompt 7 — Retro-processing**
> Read docs/PLAN.md §5.7. Implement Process Existing Folder: directory picker → adopt as Group (or open if already one via manifest.json); enumerate images (jpg/png/tif/bmp) and PDFs (render pages at native/300 DPI via PDFtoImage) through the same page-adoption path as scanning; SHA-256 duplicate detection (already-registered content is reported, not re-rowed); then the standard pipeline (field entry, OCR, AI, commit/CSV). Reconcile command on any group: report rows-without-files and files-without-rows, offer re-match by checksum (handles renames), removal, or adoption. Selective re-run dialog: all / only OCRed=No|Failed / only AIStatus=Failed|Pending / redo everything (previous .md to trash). Idempotence is the acceptance bar: running retro-process twice on the same folder changes nothing the second time. Tests: fixture folder scenarios (fresh, partial, renamed files, duplicate content, foreign index.csv present → warn per PLAN §B16). Out of scope: watch folders (v2).

> **Prompt 8 — Batch and CLI**
> Read docs/PLAN.md §5.8 (batch + CLI rows) and docs/research/research-1-naps2.md (CLI section). Batch dialog: single scan / multiple-with-prompt / multiple-with-delay, count, interval, target group, profile; integrates with commit flow. Profile import/export (.fgprofile JSON, schema-versioned). Keyboard shortcuts: rebindable map, NAPS2 defaults (Ctrl+Enter scan, F2–F12 profiles, Ctrl+Z undo, Ctrl+Shift+arrows rotate, etc.). FgScanner.Cli (System.CommandLine): fgscanner scan -p <profile> --group <dir> [--source glass|feeder|duplex --dpi N --bitdepth c|g|bw -n count]; fgscanner process <dir> [--ocr --ai --write-index]; fgscanner export --group <dir> -o out.pdf [--pdfcompat A2-b ...]; fgscanner list-devices [--driver wia|twain|escl]; exit codes + --verbose; headless (no WPF reference — Core/Scanning only). Single-instance + session-restore in the app. Tests: CLI integration tests with FakeScanService via DI; shortcut-map unit tests. Acceptance: a scheduled task can run a scan-to-index pipeline with no UI. Out of scope: full 60-flag NAPS2 parity (track the diff in FEATURE-PARITY.md).

> **Prompt 9 — Ship it**
> Read docs/PLAN.md §4, §9 (phase 9) and docs/research/research-4-delivery.md. Complete build/installer/setup.iss: per-machine, file associations (.pdf/.jpg/.jpeg/.png/.tiff/.tif/.bmp OpenWithProgids), WIA AutoPlay handler + StillImage registration ("Scan with FG Scanner"), Start Menu + optional desktop icon, license + privacy policy pages with the Gemini opt-out checkbox wired to a setting, [InstallDelete] hygiene, silent-install flags documented. Auto-update: NetSparkleUpdater with Ed25519-signed appcast hosted on GitHub Pages/Releases; update check honoring a NoUpdatePrompt setting; installer run /VERYSILENT /NORESTART on accept. CI release.yml: publish → sign payload (SignPath action; secrets documented in docs/release.md) → build installer → sign installer → SHA256SUMS → attest-build-provenance → GitHub Release; winget.yml with winget-releaser. First-run wizard: language, theme, default profile creation, optional AI setup. Docs: README (screenshots), docs/user-guide.md, THIRD-PARTY-NOTICES.md completed and verified against Directory.Packages.props, PRIVACY.md. Portable ZIP artifact (config beside exe). Acceptance: clean Win11 VM → download installer → install → scan → commit CSV, no SmartScreen hard-block path undocumented; upgrade from previous version preserves DB (migration fixture proves it). Out of scope: MS Store.

> **Prompt 10 — Differentiators (v1.1)**
> Read docs/PLAN.md §7 items 1, 19, 22 and §C1–C2 of docs/research/research-5-features.md. Implement behind individual feature flags: (a) Patch-T detection (ZXing.Net) as document separator with per-profile policy (drop/keep separator page) + "Print separator sheets" menu; (b) blank-page policy per profile: drop (logged in group journal) / flag (excluded from OCR/AI/index) / treat-as-separator; (c) FTS5 search UI: query box over OCR text + fields with snippet highlighting, results open group/page; (d) commit hook: optional command line + optional webhook POST (JSON payload = manifest + rows) on group commit. (XLSX/XML/JSON export shipped in phase 3.) Tests per feature; FEATURE-PARITY.md and README updated. Acceptance: each feature demonstrable and individually disableable.

---

## 11. Claude Code Project Structure

**CLAUDE.md** (repo root — written in phase 0, kept short):
- Stack summary + the §3 avoid-list (the licensing guardrails matter most: no NAPS2.Lib/GPL code copying, no ImageSharp variant of NAPS2.Images, no FluentAssertions ≥8, no iText, LGPL DLLs never merged).
- Build/test/run commands (`dotnet build`, `dotnet test`, `dotnet run --project src/FgScanner.App`, installer build).
- Architecture map (the §8 diagram, one screen).
- Coding standards: comments explain *why* only; validate at boundaries; no features beyond the task; hardware access only through `IScanService`; all file writes atomic; every user-visible string in .resx.
- Testing rules: business logic never requires hardware (use FakeScanService); OCR tests use real Tesseract; AI tests use MockHttp, never live keys; CSV/PDF via Verify snapshots.
- Pointer to docs/PLAN.md (this file), docs/FEATURE-PARITY.md, docs/adr/.

**docs/ layout:**
```
docs/
  PLAN.md                ← this document
  FEATURE-PARITY.md      ← §5.8 table as a living checklist
  research/              ← the five research digests (already written)
  adr/                   ← 0001-dotnet-over-python.md, 0002-sdk-not-fork.md,
                           0003-sqlite-behind-csv.md, 0004-tesseract-shellout.md,
                           0005-byo-gemini-key.md (seeded from this plan's §3–§5)
  manual-tests.md        ← hardware smoke-test checklist per release
  release.md             ← signing/release runbook
```

**.claude/ setup:**
- `settings.json`: allow `dotnet build/test/run/format`, `git`, ISCC; deny network-fetch of secrets.
- Skills worth creating once the repo exists:
  - `release` — version bump → changelog → publish → installer → sign → tag (encodes docs/release.md).
  - `parity-check` — walks FEATURE-PARITY.md against the codebase and reports drift.
  - `hardware-smoke` — interactive checklist runner for docs/manual-tests.md before a release.
- Existing global skills (superpowers TDD/debugging, code-review) cover the rest — no need to duplicate them per-repo.

**Git workflow:** feature branch per phase (`phase-1-scan-core`), PR to main with CI green, tag `v0.<phase>` at each phase exit. Phase prompts are designed to be run in fresh sessions — each re-reads its inputs, so context loss between sessions costs nothing.

---

## 12. Risks & Mitigations

| Risk | Impact | Mitigation |
|---|---|---|
| Scanner-driver quirks (the eternal TWAIN lottery) | Support burden | NAPS2.Sdk absorbs most; hardware smoke-test checklist; WIA default + TWAIN fallback mirrors NAPS2's proven order |
| NAPS2.Sdk abandonment | Scanning layer orphaned | LGPL — we can fork the SDK layer itself; NTwain (MIT) exists as a rebuild path; seam is one interface |
| Gemini pricing/model churn (prices rise Jan 2027) | AI cost surprises | Model + price table are config, not code; cost estimator; BYO-key means user sees their own bill; IChatClient seam allows local models |
| SignPath application rejected/slow | Unsigned releases hit SmartScreen | Azure Artifact Signing fallback ($9.99/mo); winget distribution builds reputation |
| WPF DataGrid performance with 12 dynamic columns × large groups | UI sluggishness | Virtualization on by default; test with 5,000-row fixture in phase 3 |
| Scope creep (this document lists 60+ tempting features) | v1 never ships | Phases 0–9 are the contract; §7 items enter only via explicit decision |
| Group folders inside OneDrive/Dropbox | Locks/sync conflicts during atomic writes | File.Replace pattern + retry; documented guidance; test case in phase 3 |
| EF Core 10 SQLite DateTimeOffset breaking change | Subtle timestamp bugs | Store UTC ticks explicitly; migration fixture tests |

---

## 13. Appendix A — Gemini Cost Reference (observed 2026-08-19)

A Letter/A4 page at any scan DPI ≈ **1,032 image tokens** (4 tiles × 258). With ~250 output tokens per description:

| Model | $/1M in | $/1M out | ≈ $ per 1,000 pages | 5,000 pages |
|---|---|---|---|---|
| **gemini-2.5-flash-lite** (default) | 0.10 | 0.40 | **0.21** | 1.04 |
| gemini-3.1-flash-lite | 0.25 | 1.50 | 0.65 | 3.23 |
| gemini-3.5-flash-lite | 0.30 | 2.50 | 0.95 | 4.75 |
| gemini-2.5-flash | 0.30 | 2.50 | 0.95 | 4.75 |
| Claude Haiku 4.5 (future option) | 1.00 | 5.00 | 3.78 | 18.90 |
| Local Ollama VLM (future option) | — | — | 0 (hardware) | 0 |

Batch API halves Gemini figures. Prices rise for Gemini 3.6/3.7 models on 2027-01-01. Free tier is excluded on privacy grounds (§4).

## 14. Appendix B — Key References

- NAPS2: naps2.com · github.com/cyanfish/naps2 (app GPL-2.0+, SDK LGPL-2.1+; SDK NuGet 1.3.0)
- TWAIN 2.5 spec: twain.org · eSCL: mopria.org/mopria-escl-specification
- Tesseract: github.com/tesseract-ocr/tesseract (5.5.3) · tessdoc ImproveQuality · NAPS2.Tesseract.Binaries (NuGet)
- Gemini: ai.google.dev/gemini-api/docs/{pricing, image-understanding, thinking, batch-api}, /terms · github.com/googleapis/dotnet-genai
- PDFsharp 6.2.4 (MIT): pdfsharp.net · CsvHelper · ClosedXML (MIT, XLSX) — avoid EPPlus ≥5 (PolyForm Noncommercial) · RFC 4180
- .NET 10 LTS support policy: dotnet.microsoft.com · WPF Fluent themes
- Inno Setup 7: jrsoftware.org · NetSparkleUpdater · SignPath Foundation: signpath.org/terms · Azure Artifact Signing
- Competitor prior art: Epson Document Capture Pro v3.3 webhelp (Index step, CSV/XML index, append mode) · Kofax Capture/Express docs (field types, sticky, DB validation) · Paperless-ngx docs (workflows, barcodes, filename templates, trash, dual storage) · Rossum (confidence-threshold review) · SQLite FTS5
- Full research digests with all URLs: `docs/research/` in the repository.

---

*End of plan. All decisions are resolved (§6) and the v1.1 changes are folded in throughout. Next step: initialize the Git repository and run Prompt 0.*
