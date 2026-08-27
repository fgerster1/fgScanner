# ADR-0003 — Preserve the untouched capture before the first pixel edit

**Status:** accepted, 2026-08-27

## Context

Every pixel-modifying operation — manual rotate/deskew/crop/brightness, auto-orient in
`OcrPipeline.UprightAsync`, split, combine — re-encodes the JPEG over the original path via
`ImageEditor.SaveAtomicAsync` + `AtomicFileWriter`. The prior bytes are destroyed; ADR-0002
accepted that for auto-orient. That is fine for archival convenience and fatal for legal
evidence: under Ohio Evid.R. 1001(4)/1003 a scan is a "duplicate," admissible as the original
*unless a genuine question is raised as to authenticity*. A challenged page whose capture-time
bytes no longer exist has no answer to that question. The JimsStuff evidence pipeline (see
`docs/spec-evidence-export.md`) needs the capture to survive, and any archival user benefits.

## Decision

A new flag, `Feature.PreserveOriginals`, **default OFF**. When on, the first pixel-modifying
write to a page image first copies the current bytes — `File.Copy`, never a re-encode — to
`<groupDir>\originals\<same filename>`. First write wins: if the archive file already exists it
is never touched again; that copy IS the original. The guard lives at the single seam every
edit passes through, `ImageEditor.SaveAtomicAsync` (manual edits, the auto-orient rotator,
split and combine all funnel there), so it cannot care how the page arrived — scanned, adopted
or retro-processed pages behave identically. A write target that does not exist yet (the second
half of a split) is a new file, not an edit of a capture, and is not archived.

`Pages.OriginalChecksum` (nullable TEXT) records the SHA-256 of the archived bytes, set exactly
once by `ReorderService.RefreshChecksumAsync` — the hook every edit path already calls — and
never updated after. Null means "never edited: the live file is the original." The value
travels in `index.json` as `originalChecksum` (Phase 16 contract), and the archive file itself
travels with the page: cross-group moves rename it alongside (`GroupService.MoveFileAndSidecars`),
and Trash moves it under an `originals\` prefix inside the trash item (flat would collide with
the page's own filename) and restores it the same way.

## Consequences

- With the flag on, every edited page costs double its disk space. That is the point for
  evidence work and waste for everyone else — hence default OFF, in Settings next to
  Auto-orient as "Keep an untouched copy of every image before its first edit (for evidence work)".
- The JimsStuff evidence runbook can now re-enable `Feature.AutoOrient` for evidence groups:
  auto-orient's rewrite goes through the same seam, so the capture is archived first.
- The archive holds the bytes as they were *before the first edit with the flag on*. Pages
  edited before the flag was enabled have already lost their capture; the flag cannot recover
  the past, so evidence groups must have it on from the start.

## Alternatives rejected

**Archive at scan time instead of first edit.** Copies every page including the never-edited
majority, doubling disk for pages whose live file already is the original. `originalChecksum:
null` states that case explicitly and verifiably.

**Store originals in the database or a global vault.** The group folder is the handoff unit for
the evidence pipeline; an archive outside it would not travel with a copied folder and would
re-introduce the "order and integrity live only in the local DB" problem Phase 16 removed.
