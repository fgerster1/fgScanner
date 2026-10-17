# ADR-0019 — A backup is a whole station pack that never moves the round trip's baseline

Date: 2026-10-17 · Status: Accepted · Decided by: Franz (Round A, every recommendation) · Spec: SPEC-SCANNER-2026-011

## Context

Jim's scans are legal evidence that took weeks to make, and until now the only copies left his
laptop when Franz ran a round trip. Franz asked for a backup to a USB stick from inside the app,
so that an update can never lose his work, and for the same copy to reach Franz's PC for testing.

A station pack (ADR-0017) is already a whole station that can be checked: a manifest of every
file's SHA-256, an 8-character code to read out, and a re-verification after writing. But every
pack also records itself as the round trip's baseline (the freeze record), which the next
return from Franz is checked against.

## Decisions

1. **A backup is a third pack kind, `backup`.** It is always the whole station, never based on
   another pack, so each one restores on its own. It is checked after writing like every pack,
   and it **never writes or changes the freeze record or the sent manifest**: one backup between
   Jim's send and Franz's return would otherwise refuse the return.
2. **The newest three of a station's backups stay on the stick.** An old one is removed only after
   a new one is written; a folder with no manifest (the stick was pulled) is cleared; a folder whose
   files are missing is kept for Franz and never counted. Retention checks files by size, not by
   hash — each backup was hash-checked when written, and re-reading three every run takes minutes.
3. **The Back up button runs inside the open app.** The closed-station check of `pack` cannot
   hold there (EF keeps the database in WAL mode, and the live scan session keeps an index), so the
   app answers it instead: no paper in hand, no unsaved pages on the Scan page, no page files from
   an earlier session. Background reading is held by a `WorkGate` for the copy; the database is
   copied through SQLite's backup API.
4. **The update offer names the last backup and offers one first.** Jim may skip it; a backup that
   does not finish installs nothing.
5. **A test copy opens on Franz's PC beside the station it came from.** `station apply --test-copy
   --evidence <folder>` takes apply's untrusted-database checks, proves every stored path lies in
   the pack's own evidence folder, then moves those paths to the folder Franz names. It writes
   `test-copy.json`; a test copy says so in its title and can never send or return, so scanning in
   it is allowed.
6. **Restore is `station apply --restore` into an empty station** — a Teams procedure, never a
   button. It takes only a `-backup` folder and keeps the backup's Settings except every `Hook.*`
   (Revision C, CR-3, Franz): an ordinary apply into an empty station takes none, which would turn
   keep-originals off on Jim's evidence station.

## Alternatives rejected

- **A plain folder copy (robocopy).** No receipt, no code to read out, no proof the copy is whole.
- **Incremental backups.** Faster after the first, but a restore would need a chain of folders,
  any one of which may be the one that is lost. Revisit past about 20 GB.
- **Teaching `apply` to place a pack's files under another folder.** "A pack never chooses where
  files land" is the round trip's security boundary; a test-only switch inside it weakens every
  real apply. The test copy is a separate opener instead.

## Consequences

- The stick holds evidence unencrypted (Round A, Q7); Jim keeps it with the boxes.
- `StationManifestBuilder.Sha256Of` reads files shared for writing, because the open app holds
  its database for writing; a pack records its own copy's hash and is re-verified after writing.
