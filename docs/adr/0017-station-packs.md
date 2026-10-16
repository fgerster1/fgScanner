# ADR-0017 — Stations travel as verified, content-addressed packs

Date: 2026-10-16 · Status: Accepted · Decided by: Franz · Spec: SPEC-SCANNER-2026-010 (Revisions B–D)

## Context

Jim captures evidence on a slow laptop in Ohio; Franz processes it on his own PC in Florida.
The only link is a Google Drive folder shared between the two (Round B). Evidence must arrive
exactly as captured, Franz's work must come back without overwriting anything Jim did since,
and each cycle must be the same two shortcuts for Jim.

## Decisions

1. **A pack is a folder: `fgscanner.db`, the files the receiver lacks, and
   `station-manifest.json` written last.** The manifest lists the station's whole final state
   (path, size, SHA-256, kind); a file the receiver already holds carries `from` (its path there)
   and is not shipped, so renames travel as renames and a cycle with nothing new moves only the
   database. No manifest means "still arriving".
2. **The read-out code is the first 8 hex characters of the manifest's SHA-256** (`xxxx-xxxx`),
   and Jim confirms it before apply changes anything; apply is told the confirmed code.
3. **The freeze check compares content, not the database file.** Opening the app and every
   migration rewrite the file without changing a row, so apply compares a SHA-256 over the
   evidence rows (in the sender's columns, without station-owned values) and the evidence files
   against what the receiver recorded when it last sent.
4. **Everything arriving is untrusted.** Apply refuses links, paths outside the evidence root,
   tables or indexes this version does not have, and a manifest naming a file outside the pack;
   it rebuilds triggers and views from the build's own definitions, keeps the receiver's Settings
   (and takes none into an empty station), and runs SQLite with `trusted_schema = OFF`.
5. **Nothing is erased.** Apply stages beside the station, moves replaced and deleted files into
   `apply-backup-<stamp>\` under a plan written first, restores itself on any failure, and
   `--undo` takes back exactly the apply the freeze record names.

## Consequences

- The station verbs ship in every release (`cli\`), so the pack format never drifts from the
  schema; a CI ladder migrates every shipped schema.
- A pack is refused whole, never trimmed; Jim's laptop never runs a merge.
- Packs carry no signature: authenticity rests on the folder being shared with Jim alone and on
  the code he confirms (D3); signing is the upgrade path if that changes.
