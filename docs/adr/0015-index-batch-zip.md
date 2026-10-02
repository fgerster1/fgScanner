# ADR-0015 — An index batch arrives as one zip, extracted out of Jim's way

Date: 2026-10-02 · Status: Accepted · Spec: JimsStuff SPEC-2026-007 §05 Q3 / §08.6 (case-index phase 5)

## Context

From phase 5 the portal serves each index batch as ONE download: a stored
(uncompressed) zip of the package folder, entries at the zip's root, built from the
finished folder so the manifest's checksums cover exactly what is inside. Until now FG
Scanner opened folders only. Asking Jim to extract the zip first has a trap Windows
sets: it lets you browse *into* a zip as if it were a folder, the folder picked there is
not on disk, and the reader refuses with "no manifest.json" — a sentence that tells Jim
nothing about what he did.

## Decisions

1. **"Open batch…" picks the zip itself**, defaulting to Downloads; "Open folder…"
   stays for a batch already unpacked. Picking the file means the inside-the-zip trap
   is never offered.

2. **The zip is extracted to `%LOCALAPPDATA%\FGScanner\index-packages\<packageId>\` and
   opened by the UNCHANGED `PackageReader`** (`ZipPackageOpener`, Core). The zip is
   transport, never trusted on its own: every check the reader makes — marker, format
   version, every checksum, coherence — runs on what was extracted. Local, not roaming,
   AppData: a 300 MB batch must not follow a roaming profile around.

3. **Extraction is all-or-nothing.** It writes to a temporary sibling folder and renames
   it into place only after every entry landed inside it. An entry naming a path outside
   the package (zip-slip: `..`, rooted, drive-qualified) refuses the whole zip, in the
   reader's own words for the same fault, and leaves nothing behind. A folder the reader
   then refuses is deleted, so the next open never tries it first.

4. **Opening the same zip again reuses the folder** — after a restart, say — because the
   reader re-verifies every checksum on each open, so reuse skips no check. Reuse needs
   the SAME build: the folder's package checksum must equal the zip manifest's, because a
   rebuilt batch under the same id verifies against its own manifest too and would open
   stale pages under the old draft. A reused folder the reader refuses (damaged on disk
   since) or an older build is replaced from the zip. *(Build check added 2026-10-02,
   SPEC-2026-007 Prompt 10 review.)*

5. **The answers file is written beside the zip, not beside the extracted folder.** The
   phase-4 rule was "results beside the package, never inside it"; for a zip, beside the
   extracted folder would be a folder Jim never sees, and he must find the file to upload
   it. Beside the zip — usually Downloads — is where he will look. Opening a folder keeps
   the phase-4 behaviour exactly.

6. **After export the message says where the file goes**: "upload this file on the
   portal — Case Index → your batch → Upload answers".

## Consequences

- No contract change: the package format is untouched, so reader-first release rules
  are not triggered.
- "Remove package from this computer" deletes the extracted folder and the draft; the
  downloaded zip stays in Downloads, as any download does. Jim's runbook says when the
  portal confirms it is safe to delete either (JimsStuff SPEC-2026-007 §05 Q7).
- **A zip opened with no draft restores the answers file beside it** (Franz, 2026-10-02,
  SPEC-2026-007 Prompt 10 review). Because the zip outlives the draft, re-opening it used
  to start empty, and the next export overwrote the answers file — possibly before it was
  uploaded. Now that file is read back when its `packageId` and `packageChecksum` match
  this build, its answers keep their original `decidedAt`, the screen says they were
  restored from the file, and the next export holds them again. A file for another build
  is left alone, with a notice that the next export replaces it.
- `Feature.IndexMode` stays default OFF (SPEC-2026-007 §05 Q13).
