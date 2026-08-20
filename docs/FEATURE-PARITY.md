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
| AI descriptions (Gemini, BYO-key, queue, cost) | FG core | ☑ (gemini-2.5-flash-lite; CredMan key; durable queue; estimate + spend tracking; blank-page skip) | 6 |
| Retro-process existing folder + reconcile | FG core | ☑ (in-place adoption, PDF render, checksum re-match, foreign-index warn, selective re-run; idempotent) | 7 |
| Batch dialog + CLI + shortcuts + profiles import/export | [P] | ☑ (scan/process/export/list-devices; batch modes; rebindable NAPS2 defaults; .fgprofile) | 8 |
| Installer, auto-update, signing, winget | [F] | ◐ (installer complete: associations/StillImage/AutoPlay/privacy+AI-opt-out; NetSparkle wired pending keys; SignPath+winget workflows ready pending accounts) | 0,9 |
| Email (MAPI) / print / clipboard | [P] | ◐ (print + clipboard + drag-out done; MAPI email in 9) | 4,9 |
| Crash recovery + session restore + single instance | [F] | ☑ | 1,8 |
| Dark/light theme | [F] | ☑ | 0 |
| Walking skeleton (solution, CI, installer stub) | — | ☑ | 0 |
