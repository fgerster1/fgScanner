# Scope — GAP-1: automatic page-orientation detection

> **Status: implemented 2026-08-25** (branch `phase-11-auto-orientation`).
> Decision #4 below — "rotate on 180 only" — was **overturned by measurement** before
> implementation: exactly one of the two sideways directions reads correctly and the other
> is indistinguishable from an inverted page, so a 180-only rule would leave a landscape
> page on disk that no confidence check could flag. The shipped behaviour rotates to
> whatever angle OSD reports. See `docs/adr/0002-auto-orient-every-angle.md`.
> The blocker in §3 is resolved: `osd.traineddata` now ships in `src/FgScanner.Ocr/tessdata`.

**Status:** implemented 2026-08-25 · **Scoped:** 2026-08-24 · **Origin:** `docs/manual-tests.md` GAP-1

Every number below was measured on this machine against the real upside-down scans from the
2026-08-24 hardware pass — none of it is estimated.

---

## 1. The problem, measured

A page fed into the ADF upside down produces confident-looking garbage, and nothing in the pipeline
notices. `TesseractRunner` hardcodes `--psm 3`, which does **not** include orientation detection.

Same physical page, OCRed through our own pipeline in both orientations:

| | mean_confidence | first line of extracted text |
|---|---|---|
| As scanned (180°) | **29.70** | `smopulM\:D` … (reversed gibberish) |
| Rotated upright | **90.14** | `~~ 1200 Southeast Ave. Tallmadge, Ohio` |

A 741 ms detection pass converts unusable output into correct output. That is the whole case for
doing this.

## 2. Feasibility — already proven, not assumed

Tesseract's OSD mode was run against the actual scans:

```
$ tesseract scan_00003.jpg stdout --psm 0        # the page as scanned
Orientation in degrees: 180
Rotate: 180
Orientation confidence: 10.20

$ tesseract upright.jpg stdout --psm 0           # same page, rotated upright (control)
Orientation in degrees: 0
Rotate: 0
Orientation confidence: 10.62
```

Detects the real fault, and does **not** false-positive on a correct page. Cost: **741 ms/page**,
comparable to a full OCR pass — which is why §4 recommends not running it on every page.

## 3. The blocker: `osd.traineddata` is not shipped

We bundle `eng.traineddata` (4.1 MB) only. `--psm 0` and `--psm 1` both fail without
`osd.traineddata`, so **no orientation work is possible until that file ships**.

Verified against the upstream source `LanguageManager` already uses:

| | |
|---|---|
| URL | `https://raw.githubusercontent.com/tesseract-ocr/tessdata_fast/main/osd.traineddata` |
| Size | 10,562,727 bytes (10.07 MB) |
| SHA-256 | `9cf5d576fcc47564f11265841e5ca839001e7e6f38ff7f7aacf46d15a96b00ff` |

Two delivery options:

- **Bundle it** (recommended). +10.07 MB on a 91.6 MB installer. Works offline, always present, no
  runtime surprise. The app is already 318 MB unpacked, so the relative cost is small.
- **Download on demand** via the existing `LanguageManager` (pinned-hash mechanism already built —
  the hash above drops straight in). Saves installer size, but makes the *first bad page* trigger a
  network fetch. That sits badly with the local-first promise in `PRIVACY.md` and would surprise a
  user scanning offline.

## 4. Recommended design

**Detect lazily, rotate through the existing edit path, re-OCR.**

1. **Trigger only on low confidence.** Run OSD when a page's `mean_confidence` falls below
   `OcrPipeline.LowConfidenceThreshold` (65). Measured separation is wide — 29.70 for the misfed
   page vs 90.14 upright — so the threshold discriminates cleanly. Good pages pay **nothing**;
   running OSD unconditionally would roughly double OCR wall time for no benefit on the 95% case.
2. **Rotate via the existing `PageEdit.Rotate` path**, not a bespoke code path. Phase 4 already
   handles image rewrite, checksum update, thumbnail refresh, re-export, and undo/redo for rotation
   (`docs/manual-tests.md` § Phase 4 covers exactly this). Reusing it means auto-rotation is
   undoable like any manual rotation, and dedup-by-checksum stays correct for free.
3. **Re-OCR the rotated page**, replacing the `.md` sidecar through the normal trash-the-old route.

Net cost on a clean page: zero. On a misfed page: one OSD pass plus one re-OCR, both bounded.

### Why not the alternatives

- **`--psm 1`** (auto segmentation *with* OSD, single pass, rotates internally) looks cheaper, but it
  changes page segmentation away from the `--psm 3` that phase 5's geometric Markdown reconstruction
  was tuned against. Swapping segmentation modes risks regressing every heading/column/table
  heuristic to save one process launch on rare pages. Not worth it.
- **Brute force** — OCR at 0/90/180/270 and keep the best confidence — needs no new data file, but
  costs 4 OCR passes per bad page instead of one cheap OSD pass, and gives no confidence signal for
  the choice.
- **Rotate at capture time** would slow the scan loop by 741 ms/page and fight the streaming
  thumbnail UX. Orientation is an OCR-stage concern.

## 5. Decisions

### DECIDED 2026-08-24

1. **The stored image gets rotated**, not just the OCR text. Auto-rotation goes through the existing
   phase-4 `PageEdit.Rotate` path so it inherits checksum update, thumbnail refresh, re-export and
   undo/redo. Accepted consequence: this mutates a file the user scanned and puts checksum identity
   and committed-group re-export inside the blast radius — the risk called out in §6 item 4 is
   therefore in scope and needs the heaviest test cover.
2. **`osd.traineddata` is bundled in the installer.** Setup exe grows ~10 MB (91.6 → ~102 MB).
   Orientation detection then works fully offline with nothing to fail at runtime, which keeps the
   local-first promise in `PRIVACY.md` intact. Work items: csproj content item, the win-x64 publish
   profile, `build/installer/setup.iss`, and a `THIRD-PARTY-NOTICES.md` entry (Apache-2.0, same
   upstream as the bundled `eng.traineddata`).

3. **Ships behind `FeatureFlags.AutoOrient`, defaulting ON.** Departs from the phase-10 precedent
   (PatchT/BlankPolicy/CommitHook all default off) because this needs no user setup to be useful —
   off-by-default would mean it never reaches anyone. The flag exists as a field escape hatch, which
   matters more than usual here since decision #1 means the feature rewrites image files. Per the D5
   policy in `docs/STATUS-AND-REMAINING-WORK.md`, if this later graduates to permanent the flag gets
   **deleted**, not left flipped on.
4. **Rotate on 180° only; surface 90°/270° as a review flag.** 180° is unambiguously a misfeed.
   Landscape documents are legitimately fed at 90°, and silently rotating them would fight the user
   and churn checksums for nothing — so sideways pages are reported, and the user decides.

**All four decisions settled 2026-08-24. Ready to implement.**

## 6. Effort

| Work | Size |
|---|---|
| Ship `osd.traineddata` (csproj content, publish profile, installer, THIRD-PARTY-NOTICES) | S |
| `TesseractRunner.DetectOrientationAsync` — `--psm 0`, parse `Rotate:` + confidence | S |
| `OcrPipeline`: low-confidence → OSD → rotate → re-OCR, journaled | M |
| Wire rotation through the phase-4 edit path (checksum, thumbnail, re-export, undo) | M ← the risk |
| Tests: OSD against fixtures with real Tesseract (never mock the engine, per CLAUDE.md); policy unit tests; end-to-end misfed-page test | M |
| Settings toggle + flag | S |

**Total ≈ 2–3 days.** Item 4 carries the risk: it touches committed-group re-export and checksum
identity, which is where a subtle regression would hurt most.

## 7. Test fixtures available now

The 2026-08-24 pass left real misfed scans at `Documents\FGScannerSmokeTest\twain-feeder\` (three
180° pages, invoice content). These are the user's own documents — **use a redacted or synthetic
substitute before committing anything to `tests/`**, but they are ideal for interactive verification.
