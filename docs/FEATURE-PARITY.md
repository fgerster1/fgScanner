# FEATURE-PARITY.md — living checklist

Tracks FG Scanner against the NAPS2 8.3.2 inventory (docs/research/research-1-naps2.md) and the PLAN §5.8 commitments. Status: ☐ todo · ◐ partial · ☑ done · — n/a. Update at the end of every phase.

| Area | Target | Status | Phase |
|---|---|---|---|
| WIA / TWAIN / eSCL scanning | [F] | ◐ (logic done; hardware smoke pending) | 1 |
| Profiles (full NAPS2 settings surface) | [F] | ◐ (core: source/dpi/depth/size/brightness/contrast) | 1,4 |
| Groups + index schema + entry grid | FG core | ☑ | 2,3 |
| Index export CSV/XLSX/XML/JSON | FG core | ☑ (incl. manifest.json + XSD) | 3 |
| Trash w/ 30-day retention | FG core | ☑ | 3 |
| Editing transforms + undo/redo | [F] | ☑ (rotate/flip/custom/deskew/crop/adjust/BW/sharpen/split/combine; undo excludes deletes+split/combine) | 4 |
| PDF (PDF/A, metadata, encryption) + images export | [F] | ☑ (PDF/A-1b/2b/3b/3u, 8 permission flags; JPEG/PNG/BMP/TIFF incl. multi-page + CCITT4) | 4 |
| Import PDF/images; file associations | [F] | ◐ (import incl. PDF passwords done; file associations in 9) | 4,9 |
| OCR → .md + searchable PDF + languages | [F]+ | ☑ (Tesseract 5.5 fast; geometric Markdown; durable queue; 9 languages w/ SHA-256 downloads; text layer via exporter) | 5 |
| Auto-orient misfed pages | [F] | ☑ (per-page OSD, rotates stored image to any angle, osd.traineddata bundled; Feature.AutoOrient default on; ADR-0002) | 11 |
| OCR confidence in the index | FG core | ☑ (OCRConfidence column in CSV/XLSX/XML/JSON + XSD; empty, never 0, when unread) | 11 |
| Group field-layout upgrade + bulk fill | FG core | ☑ (move a group to its profile's current layout; apply values to all rows; unchanged layouts stop minting versions) | 12 |
| Scan into a group returns to Groups | FG core | ☑ (auto-saves on success only; cancel/failure stay on Scan; right-click selects the group under the cursor) | 13 |
| Page viewer: zoom, resizable panel, pop-out | [F] | ☑ (Ctrl+wheel + buttons; two GridSplitters with remembered sizes; double-click opens full resolution with first/prev/next/last) | 14 |
| Trash multi-select + confirmed delete | FG core | ☑ (Extended selection, Del key, one confirm for the batch, partial failure keeps going; Restore follows the selection too) | 15 |
| Scan page: check and delete scans before saving | FG core | ☑ (double-click/Enter viewer over unsaved scans; multi-select Delete to the Windows Recycle Bin with a count confirmation, guarded to the session folder; page keys routed per section; SPEC-2026-003) | 21 |
| AI descriptions (Gemini, BYO-key, queue, cost) | FG core | ☑ (gemini-2.5-flash-lite; CredMan key; durable queue; estimate + spend tracking; blank-page skip) | 6 |
| Retro-process existing folder + reconcile | FG core | ☑ (in-place adoption, PDF render, checksum re-match, foreign-index warn, selective re-run; idempotent) | 7 |
| Batch dialog + CLI + shortcuts + profiles import/export | [P] | ☑ (scan/process/export/list-devices; batch modes; rebindable NAPS2 defaults; .fgprofile) | 8 |
| Installer, auto-update, signing, winget | [F] | ◐ (installer complete: associations/StillImage/AutoPlay/privacy+AI-opt-out; auto-update live: Ed25519 keys generated 2026-08-20, appcast enabled; SignPath+winget workflows ready pending accounts) | 0,9 |
| Email / print / clipboard | [P] | ☑ (print + clipboard + drag-out; email from the Scan page and a group: MAPI draft when a client is probed, else Windows Share sheet, else Explorer — or Gmail/Yahoo Mail compose in the browser with the file on the clipboard to paste, chosen in Settings; PDF or the page files as-is; never sends, never logs a recipient; SPEC-2026-007, ADR-0012) | 4,9,26 |
| Crash recovery + session restore + single instance | [F] | ☑ | 1,8 |
| Dark/light theme | [F] | ☑ | 0 |
| Walking skeleton (solution, CI, installer stub) | — | ☑ | 0 |
| Patch-T separator detection + printable sheets | [P]+ | ☑ (ZXing Code 39 "PATCHT"; per-profile drop/keep; NAPS2/Paperless-compatible sheets; feature-flagged) | 10 |
| Blank-page policy per profile | [P]+ | ☑ (drop journaled / flag excluded from OCR+AI+CSV/XLSX/XML (in index.json flagged since 16) / treat-as-separator; feature-flagged) | 10 |
| Full-text search (FTS5) over OCR + fields + AI | FG core | ☑ (Search section; snippet highlighting; results open group/page; feature-flagged) | 10 |
| Commit hook (command + webhook) | FG core | ☑ (cmd line w/ $(group)/$(dir)/$(manifest) tokens; webhook POSTs index.json payload; journaled; feature-flagged) | 10 |
| Evidence-grade index.json | FG core | ☑ (rows carry sequence/pageId/checksum/isBlank; blank-flagged rows included in JSON+webhook only, CSV/XLSX/XML unchanged; manifest evidenceExport:1) | 16 |
| Preserve originals (evidence) | FG core | ☑ (Feature.PreserveOriginals, default off; first edit copies capture to originals\; OriginalChecksum set once; archive travels through move/trash; originalChecksum in index.json; ADR-0003) | 17 |
| Real release number in exports | FG core | ☑ (single <Version> in Directory.Build.props reaches every assembly; installer derives its version from the published exe; manifest.json/index.xml appVersion now name the actual build) | 18 |
| Batch-scoped fields (one value per group, not per row) | FG core | ☑ (FieldScope.Batch; `Group.BatchFieldsJson`; `BatchFieldMerge.Effective` shared by export + entry grid, validation and search reimplement the same split at their own layer; DefaultValue seeds the group once, token-expanded; Evidence profile's Box/Operator now Scope: Batch; ADR-0004) | 19 |
| Per-page capture identity | FG core | ☑ (`Page.CapturedBy`, stamped at the two genuine capture sites, null for retro-processed files; `capturedBy` in index.json; ADR-0005) | 19 |
| Record editor: one page's fields beside its page | FG core | ☑ (modal three-pane window from the Groups toolbar — form, image with the shared zoom/fit, and the group's grid from one shared column builder; pane sizes remembered per group; next/previous keep all three in step; delete/add/import reuse the group's own commands; SPEC-2026-002) | 22 |
| Text field length + memo box | FG core | ☑ (`MaxLength` 1–100, or 1–2000 when `Memo`; Text only; enforced on typing and refused whole on an over-long paste, never `TextBox.MaxLength`; stored values never trimmed; `.fgprofile` v3 only when used; nothing exported; ADR-0009) | 22 |
| Memo chosen in the Type list | FG core | ☑ (Settings offers Text/Memo/Date/Number/List; still `FieldType.Text` plus the flag underneath, so `manifest.json` is unchanged; the separate Memo checkbox is gone; ADR-0009 amendment) | 24 |
| Text fields wrap and grow | FG core | ☑ (every text box in the record editor wraps to 20 lines then scrolls, memo or not; no sideways scrollbar; the pane divider is the width control; the memo resize grip was removed; SPEC-2026-005) | 24 |
| List field sized to its choices | FG core | ☑ (a dropdown no longer stretches the form width; reverses a SPEC-2026-002 non-goal, approved 2026-09-20. Known gap: it tracks the SELECTED choice, not the longest one, so an empty list shows as a stub — AC-4 not fully met) | 24 |
| Duplex capture, one pass | [F] | ◐ (source probed per device via `ScanCaps`, unsupported sources disabled with the reason as text/tooltip/automation name; `FlipDuplexedPages` for scanners that return backs upside down, default off; NAPS2 refusals translated into plain words. **Hardware row 1 of §11.3 is still open — no physical duplex page has been produced by this code**) | 25 |
| Duplex capture, two passes | **beyond [F]** | ☑ (NAPS2 has no equivalent: fronts, turn the stack, backs, interleaved into sheet order *before* adoption so `scan_NNNNN` is the pairing — the Groups-page Interleave button stays as the repair tool. Refuses on a count mismatch rather than guessing; keeps every blank back past both the checksum skip and the blank-page Drop policy; feeder-only; prompt and Cancel visible throughout; recovery says the pairing was lost. ADR-0011, SPEC-2026-006) | 25 |
