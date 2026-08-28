# Evidence Export — FG Scanner changes for the JimsStuff evidence pipeline

**Status:** approved plan, not yet built. Companion to the master plan in the JimsStuff repo:
`C:\Users\fgers\Visual\JimsStuff\docs\superpowers\plans\2026-08-27-fgscanner-evidence-import.md`
— read that for the full architecture, the legal framework, and the import-side phases. This
document is the FG-Scanner-side half: what changes **in this repo**, why, and the build prompts.

**Context in one paragraph:** Jim will scan his attorney's case file (two bankers boxes of
legal evidence for a Portage County probate case) with FG Scanner on his own machine. The
committed group folders are copied to Franz's dev machine and imported into the JimsStuff
portal, which owns Bates identifiers, provenance, transcripts, and review workflows. FG Scanner
is the capture station; the portal is the system of record. Two FG Scanner changes make the
handoff evidence-grade; both are useful to any archival user, not just this case.

---

## Legal constraints that bind THIS repo

Under Ohio Evid.R. 1001(4)/1003 a scan is a "duplicate," admissible as the original **unless a
genuine question is raised as to authenticity**. Three rules follow for FG Scanner:

1. **Capture bytes must survive.** Today every pixel edit — including background auto-orient —
   re-encodes the JPEG over the original path (`ImageEditor.SaveAtomicAsync` →
   `AtomicFileWriter`; acknowledged in ADR-0002). For evidence, the untouched capture must be
   preserved (Phase 17 below). Until it ships, the evidence workflow runs with
   `Feature.AutoOrient` OFF and a no-edits rule.
2. **The export contract is load-bearing.** The JimsStuff importer parses `index.json` and the
   Evidence profile's field names. A renamed key or field silently breaks a legal pipeline.
   After these phases, treat both as stable external contracts (CLAUDE.md update, below).
3. **No Bates numbers on pixels, ever.** Identifiers live in the portal's register and display
   layer. FG Scanner has zero Bates support today and that is correct — stamped pixels can
   never be reorganized, and re-stamping is evidence alteration.

## The contract (what the importer will rely on)

- **Evidence profile fields.** No longer hand-entered: `FgScanner.Core.Evidence.EvidenceProfile`
  is the definition and `ProfileService.EnsureEvidenceProfileAsync` creates or repairs the
  profile from it, because a typo in a name the importer parses is indistinguishable
  downstream from an absent field. Four fields were added for the annotated-sheet protocol —
  `NoteState` (List: `as-found`/`note-face`/`clean`, **never sticky**), `NoteAuthor` (Text,
  sticky), `NoteBasis` (List: `stated`/`handwriting`/`signed`/`none`, sticky) and `NoteWhen`
  (Text, sticky — ISO date, `unknown`, or `case-prep`). The original nine:
  `DocNo` (Number, required, sticky — document boundary), `DocDate` (Text — permits the
  portal's `~` approximate-date notation, which the strict Date type would forbid), `DocType`
  (List — the portal's 13-value vocabulary), `Title`, `Parties` (Text, `;`-separated),
  `Operator` (Text, sticky, default `$(user)`), `Redact` (List: `identifier`/`phi`),
  `Box` (Text, required, sticky — which physical box/folder the sheet came from), `Notes`.
  These **names** are parsed by the importer.
- **`index.json`** row keys after Phase 16: existing `group`, `imageName`, `ocRed`,
  `ocrConfidence`, `aiDescription`, `aiStatus`, `fields` **plus** `sequence` (int),
  `pageId` (GUID), `checksum` (SHA-256 lower-hex), `isBlank` (bool), and after Phase 17
  `originalChecksum` (string|null). `manifest.json` gains `"evidenceExport": 1`.
- **`originals\<filename>`** after Phase 17: byte-identical pre-edit capture, travels with the
  page as a sidecar.

Rationale for Phase 16: page order, checksums, GUIDs, and blank flags currently live **only in
the SQLite DB** on the scanning machine, and blank-flagged pages are excluded from every index
file — so a copied group folder is not self-contained, and file names deliberately do not
encode order (`scan_00001.jpg` is the adoption counter). The folder must carry everything.

---

## Phase 16 — evidence-grade `index.json`  (branch `phase-16-evidence-index`, size S)

```
Repo: C:\Users\fgers\Visual\FgmakerScanner. Branch phase-16-evidence-index. Read CLAUDE.md and
docs/PLAN.md §5.2 first.

Extend the JSON index export so a committed group folder is self-contained for an external
evidence importer. Today page order, checksums, page GUIDs and blank flags live only in the
SQLite DB; blank-flagged pages are excluded from every index file.

Changes, additive only — no existing key changes name or type:
1. In src/FgScanner.Core/Index/IndexPayload.cs, each row gains:
   "sequence" (int, Document.Sequence), "pageId" (string, Page.Id GUID),
   "checksum" (string, Pages.Checksum lower-hex SHA-256), "isBlank" (bool).
2. index.json (and the identical commit-hook webhook body) now INCLUDES rows whose first page
   is blank-flagged, with "isBlank": true. CSV/XLSX/XML stay filtered exactly as today —
   they are human-facing; JSON is the machine contract. The filtering currently happens in
   IndexingService.LoadDocumentsAsync (src/FgScanner.Data/IndexingService.cs:449-454); thread
   the blank rows through to the JSON writer only, without changing what the other three
   writers receive. Keep the "sequence" semantics of index.xml untouched (it renumbers 1..n
   over exported rows and that is documented behavior).
3. manifest.json gains "evidenceExport": 1 so an importer can detect a pre-Phase-16 folder
   and refuse it with a clear message.
4. Update the Verify snapshots (Json_output_matches_snapshot, Manifest_matches_snapshot).
   docs/index-schema.xsd is NOT touched (it describes index.xml only — say so in the commit
   message if you were tempted).
5. Tests: a blank-flagged document appears in index.json with isBlank true and does NOT
   appear in index.csv; checksum in the JSON row equals a recomputed SHA-256 of the file on
   disk; sequence reflects Document.Sequence after a reorder, not filename order.

Constraints: central package versions only; warnings-as-errors; atomic writes already handled
by AtomicFileWriter — do not bypass it; no other features. Update docs/FEATURE-PARITY.md.
```

## Phase 17 — original preservation  (branch `phase-17-preserve-originals`, size M)

```
Repo: C:\Users\fgers\Visual\FgmakerScanner. Branch phase-17-preserve-originals. Read CLAUDE.md,
docs/adr/0002-auto-orient-every-angle.md, and src/FgScanner.Scanning/Editing/ImageEditor.cs first.

Problem: every pixel-modifying operation (manual rotate/deskew/crop/brightness, auto-orient in
OcrPipeline.UprightAsync, SplitAsync, CombineAsync) re-encodes the JPEG over the original path
via ImageEditor.SaveAtomicAsync + AtomicFileWriter. The prior bytes are destroyed. For legal
evidence (Ohio Evid.R. 1003 — a duplicate is admissible unless authenticity is questioned),
the capture-time bytes must survive.

Build "original preservation":
1. New setting Feature.PreserveOriginals (FeatureFlags pattern, src/FgScanner.Data/FeatureFlags.cs),
   default OFF, toggle in Settings next to Auto-orient with the caption
   "Keep an untouched copy of every image before its first edit (for evidence work)".
2. When ON: before the FIRST pixel-modifying write to any page image, copy the current bytes to
   <groupDir>\originals\<same filename> (create the subfolder; File.Copy, not re-encode). If
   originals\<name> already exists, do nothing — first write wins, that IS the original. This
   hook belongs at the single choke point ImageEditor.ApplyAsync / SplitAsync / CombineAsync
   pass through before saving, plus the auto-orient path (ImageEditorPageRotator) which reaches
   ImageEditor the same way — verify there is exactly one seam and guard it there.
3. Record Pages.OriginalChecksum (new nullable TEXT column, EF migration + regenerate
   docs/db-schema.md with FGSCANNER_UPDATE_SCHEMA_DOC=1): SHA-256 of the archived bytes, set
   once when the archive copy is made. RefreshChecksumAsync keeps updating Pages.Checksum as today.
4. Phase 16's index.json row gains "originalChecksum" (string|null; null = never edited, the file
   IS the original). Update snapshots.
5. GroupService.MoveIntoGroup / cross-group moves / TrashService must treat originals\<name> as a
   sidecar: it travels with the page (same basename logic as .md sidecars,
   GroupService.MoveFileAndSidecars and TrashService.cs:21).
6. Retro-processed and adopted pages participate identically — the seam in (2) does not care how
   the page arrived.
7. Tests: rotate → originals/ holds byte-identical pre-edit content and OriginalChecksum matches
   it; second edit does not overwrite the archive; flag OFF → no archive (current behavior);
   auto-orient path archives too (fake a rotator, assert the archive exists before rotation
   lands); move/trash carries the archive.
8. ADR: docs/adr/0003-preserve-originals.md — decision, the evidence rationale, and why default
   OFF (non-evidence users double their disk for nothing).

Update docs/FEATURE-PARITY.md. After this merges, the JimsStuff evidence runbook re-enables
Feature.AutoOrient for the evidence profile.
```

---

## Documentation updates in this repo (fold into the phase that triggers them)

- **CLAUDE.md** gains a section "Evidence work for JimsStuff" (with Phase 17's merge): the
  Evidence profile field names and the `index.json` evidence keys are **stable external
  contracts** parsed by `JimsStuff/pipeline/import_fgscanner.py` — renaming either breaks a
  legal pipeline silently; `Feature.PreserveOriginals` stays ON for evidence groups; FG Scanner
  deliberately has no Bates support and none should be added to the capture path.
- `docs/FEATURE-PARITY.md` — new rows per phase, per the standing process rule.
- `docs/adr/0003-preserve-originals.md` — written in Phase 17.

## What FG Scanner deliberately does NOT do

No upload, no portal credentials, no network awareness of JimsStuff (the importer is pull-based
on the dev machine — same reasoning that kept the old scan-bridge credential-free). No Bates
stamping. No knowledge of SCAN ids, provenance, or redaction workflows — those are the
portal's; FG Scanner's whole obligation is an honest, self-contained, byte-faithful folder.
