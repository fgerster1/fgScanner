# FG Scanner — Status & Remaining Work

**Reviewed:** 2026-09-13 · **Branch:** `main` @ `605ce9d` · **Reviewer:** Claude (repo audit)
**Supersedes:** the 2026-08-27 review at `ce26c89`. Since then: Phase 19 (batch and row metadata),
note-sheet capture, the Evidence profile as code, release 0.4.0 built and staged on USB, and the
Quick Scan program plan with Phase 0 spikes 1 and 3.

Purpose: one list of everything not finished — Franz's 2026-09-13 requests first, then everything
the review found. Every item cites where it comes from. S = hours, M = 1–3 days, L = more.

---

## 1. Verified state as of today

| Check | Result |
|---|---|
| Test suite | **516 passed, 0 failed** (`dotnet test`, Debug). The Release run could not build: FG Scanner was running from `bin\Release` and locked its DLLs |
| Declared version | `0.4.0` (`Directory.Build.props:6`) |
| Releases | `git tag -l` → **none**; `gh release list` → **none**. Nothing has ever been published |
| Parity checklist | `docs/FEATURE-PARITY.md`: 27 ☑ done, 5 ◐ partial, 0 ☐ todo — work that was never built has no row at all |
| Manual checklist | `docs/manual-tests.md`: **58 open, 7 ticked** |
| Working tree | Clean except new docs: `docs/specs/`, `docs/superpowers/research/` (uncommitted) |
| Station | 0.4.0 installer + portable + guides on USB H: (2026-09-12) |

---

## 2. Franz's 2026-09-13 list — where each item lands

Decided 2026-09-13: evidence Scan page gets viewer + delete; the other Scan-page tools go into Quick
Scan; the work is split into three specs.

| Request | Lands in | Notes |
|---|---|---|
| Scan page: delete scan | [SPEC-2026-003](specs/SPEC-2026-003-scan-viewer-delete-and-quick-scan-tools.md) | Pages not yet saved to a group |
| Scan page: double-click for a bigger page | SPEC-2026-003 | Reuses the Groups page viewer |
| Scan to PDF | Quick Scan Phase 8 (plan amended by SPEC-003) | `PdfExportService` already exists |
| Email PDF / email images | Quick Scan Phase 10 (rewritten) | Share sheet first, MAPI where a client exists, Explorer last (SPEC-003 §05 Q4) |
| Print | Quick Scan Phase 9 | Groups already has Print… |
| Custom page size, typed in | Quick Scan Phase 1, on Scan and Quick Scan (SPEC-003 §05 Q5) | NAPS2 supports it |
| Scan, draw a box, scan to that size | Quick Scan Phases 4–5 | Spike 1: software crop only |
| Crop | Quick Scan Phase 5 | `PageEdit.Crop` exists |
| OCR page(s) | Quick Scan Phase 8 (searchable PDF) + new Phase 8a ("Copy text") | No .txt files (SPEC-003 §05 Q3) |
| Undo, redo, rotate ccw/cw, flip, angle, deskew | Quick Scan Phase 6 (amended) | Edits are `PageEdit`s; `UndoRedoService` is database-free and reusable |
| Scroll bars everywhere on small screens | [SPEC-2026-001](specs/SPEC-2026-001-fit-and-scrolling.md) | Window opens bigger than a 1366×768 screen |
| Groups: preview Fit fits vertically | SPEC-2026-001 | **Bug, measured:** Fit shows a 300-DPI page at ~1/3 size |
| Groups: double-click viewer Fit fits vertically | SPEC-2026-001 | Same bug |
| Groups: CRUD record editor, three resizable panes | [SPEC-2026-002](specs/SPEC-2026-002-group-record-editor.md) | |
| Field name then field; scroll bars; size kept per group | SPEC-2026-002 | |
| Settings: character length 1–100 | SPEC-2026-002 | New field setting |
| Settings: memo length, resizable within the pane | SPEC-2026-002 | |

---

## 3. Added by the review

### 3.1 Found today — fix soon

- **Keyboard shortcuts act on the Groups page from other screens.** Delete, Undo, Redo and Rotate
  are bound at window level and run on the open group whatever section is showing
  (`src/FgScanner.App/Views/ShellWindow.xaml.cs:99-104,119-130`). Found by reading, not yet
  reproduced. Fixed by SPEC-2026-003. — S
- **Scan and note-sheet shortcut keys also work from every screen**
  (`ShellWindow.xaml.cs:95-98`), so a scan can start while Groups is showing. Possibly intended;
  decide. — S

### 3.2 Station safety — before relying on the station

- **Evidence setup walkthrough never done in the GUI**; no Evidence profile has been built on the
  dev machine and JSON export is off on every profile (`docs/manual-tests.md:268-288`). — S, high stakes
- **Hardware smoke test**: 58 boxes open — setup, WIA (no WIA device here), duplex, cancel, empty
  feeder, crash recovery, TWAIN specifics, lingering worker processes, Phases 4–10 rows
  (`docs/manual-tests.md`). Parity row still "hardware smoke pending" (`FEATURE-PARITY.md:7`). — M–L
- **Phase 19 GUI walkthrough** unwalked (`manual-tests.md:136-142`). — S
- **Shipped features with no manual-test rows**: note-sheet capture, duplicate review, delete group,
  move pages between groups, profile rename/delete/base folder, group-scoped search, page viewer,
  Trash multi-select, layout upgrade. — S each
- **Quick Scan spike 2** (preview timing) needs the scanner
  (`docs/superpowers/research/2026-08-30-quick-scan-spikes.md`). — S

### 3.3 Release pipeline — never run

- **Cut the first release** and watch `release.yml` run end to end (never executed). — M
- **Tag vs `<Version>` guard**: `release.yml:21-24,30,37,87` overrides the props version and nothing
  checks they agree; a mismatch writes a wrong `appVersion` into evidence manifests. — S
- **Auto-update never observed working** (`manual-tests.md:123`). — S
- **Code signing** gated on unset `SIGNPATH_ENABLED` (`release.yml:42,91`); SignPath application
  not made. — L (waiting time)
- **winget first submission** still manual (`winget.yml:14-18`). — S

### 3.4 Planned, not built

- **Quick Scan program, Phases 1–10 and 8a** (`docs/superpowers/plans/2026-08-30-quick-scan-program.md`,
  amended 2026-09-14 with the 2026-09-13 Scan-page requests — see its §1a), plus its support work: project skill `fgscanner-wpf-section`, CLAUDE.md section, ADRs 0006–0008,
  `docs/spec-quick-scan.md`, `QUICK-SCAN-HOWTO.txt`. — L
- **Scan section becomes scan-to-folder** (`docs/spec-scan-section.md`) — **superseded 2026-09-13**
  by the Quick Scan plan (SPEC-003 §05 Q2); Save to group stays on Scan. — closed
- **Scan settings reset every launch**: `Profile.ScanSettingsJson`, `OcrLanguages`,
  `AiDescriptionEnabled` exist but nothing reads them (`src/FgScanner.Data/Entities.cs:58,69,71`).
  — M
- **Watch folders** — Phase 10 promise, never built, no parity row. — M
- **Reopen a committed group** (`docs/roadmap-v0.2.md:138`). — M
- **"Back up database…"** — `DbBootstrapper.BackupDatabase` exists, nothing calls it
  (`src/FgScanner.Data/DbBootstrapper.cs:40`). — S
- **Search's 50-result cap is invisible** — status shows the capped count as the total
  (`SearchViewModel.cs:69-71`, `SearchService.cs:36`). — S
- **Generalise the Evidence profile** so a second case can have its own field contract
  (`quick-scan-program.md:932-946`). — L, when a second case appears
- **PLAN §7 backlog still open** (`docs/PLAN.md:278-315`): barcode → field (#2), multi-feed
  verification (#3), rescan-in-place (#4), simplex collation (#5), lookup auto-fill (#6), per-field
  zoom (#8), tags (#9), zonal OCR / AI extraction / auto-classification / confidence review /
  local VLM / Gemini Batch / handwriting warning (#11–17), rules engine (#23), audit log of field
  changes (#25), retention / PII exclusion / double-key verification / productivity stats (#26–29).
- **PLAN §5.8 v1.1/v2 items**: document correction mode and external editor (`PLAN.md:218`), OAuth
  email providers (`:225`), fuller CLI parity (`:229`), enterprise lockdown (`:230`), eSCL sharing
  server (`:234`), MS Store (`:237`), multi-page document UI (`:125`); OCR tiers 1–2 (`:181`),
  Ollama provider (`:191`).

### 3.5 Engineering debt

- **FlaUI** declared in CLAUDE.md but not referenced anywhere; CI collects no coverage
  (`.github/workflows/ci.yml:20-21`). Build it or strike it. — M
- **Repo hygiene**: no `CHANGELOG.md`, `SECURITY.md`, `CONTRIBUTING.md`, `dependabot.yml`, and no CI
  check for the forbidden packages in CLAUDE.md. — S–M
- **ADR backfill**: 8 decisions without an ADR (SDK-over-fork, .NET/WPF, one page = one document,
  Gemini-only, Tesseract shell-out, SQLite views, signed appcast, evidence contract). — M
- **Feature flags without a terminal state**: `Search` on, `PatchT` off, `BlankPolicy` off,
  `CommitHook` off (`src/FgScanner.Data/FeatureFlags.cs`). — S
- **Batch/row field rule reimplemented** in validation and search instead of the shared helper
  (`docs/adr/0004-field-scope.md:51`). — S
- **CLI needs the .NET 10 runtime** (framework-dependent publish, `release.yml:35`). — S
- **Clutter**: `ss1.html` in the repo root; stale 0.1.0 publish folder (`manual-tests.md:240-244`). — S
- **Bookkeeping**: the Phase 19 plan shows 74 unticked boxes though merged. — S

### 3.6 Documentation gaps

- **FEATURE-PARITY.md** has no rows for watch folders, Quick Scan, duplicates, delete/move group,
  profile management or note sheets.
- **Stale status headers**: `spec-evidence-export.md:3` and `spec-batch-row-metadata.md:3` say "not
  yet built" (both shipped); `roadmap-v0.2.md:3` says nothing built (slices 0–5 shipped);
  `spec-scan-section.md:113-115` treats localization as undecided (ADR-0001 settled it).
- **README**: "220+ tests" (516 now), links an empty Releases page, omits recent features.
- **user-guide.md** omits Search, Trash, Patch-T, blank policy, commit hook, feature flags,
  auto-orient, preserve originals, batch values, note sheets, duplicates, group delete/move, profile
  management, page viewer, the Evidence profile, and Scan's "Save to group" button.
- **Superseded instructions**: `PLAN.md:422` (.resx — overturned by ADR-0001), `PLAN.md:447` (tag
  every phase — never done), `release.md:6,19` (hand-typed version — replaced by `setup.iss`).
- **Evidence field count** says "nine" in older text; it is thirteen.
- **release-notes-v0.1.0.md:81-100** lists limitations since fixed.

---

## 4. Suggested order

1. **Answer the three spec review rounds** (SPEC-2026-001..003). Nothing is built before approval.
2. **SPEC-2026-001** (Fit bug + scroll bars) — about a day; unblocks SPEC-002.
3. **SPEC-2026-003 Phase 1** — shortcut bug + Scan page viewer/delete — about a day.
4. **Station safety (3.2)** — the Evidence walkthrough in the GUI and the hardware smoke, on the
   station, before more evidence is captured on an unverified build.
5. **SPEC-2026-002** — record editor and field lengths — two to four days.
6. **Release pipeline (3.3)** — version guard, first tag, auto-update proof. Start SignPath now; it
   is the only item with a waiting time.
7. **Quick Scan**, phase by phase, from the amended plan.
8. Debt and docs (3.5, 3.6) alongside, smallest first.
