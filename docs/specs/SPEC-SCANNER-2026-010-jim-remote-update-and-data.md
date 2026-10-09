# SPEC-SCANNER-2026-010 — Jim's remote update and his data

| | |
|---|---|
| **Status** | Draft |
| **Revision** | B — 2026-10-09: rebuilt around Franz's Round A (round trip as a delta; research first); in review (Round B) |
| **Tier** | System — it changes the software on Jim's live evidence station and moves evidence between machines |
| **Author** | Claude, for Franz Gerster |
| **Date** | 2026-10-09 |
| **Project** | FG Scanner (station side) with JimsStuff (receiving side) |
| **Parent** | JimsStuff SPEC-2026-007 P12 step 7; JimsStuff SPEC-2026-009 P10 (Jim's real import) |
| **Related** | A separate **storage-platform spec** (Seafile on the Tower, Tailscale, offsite) — Q-B2 |

---

## 01 · Management summary

- **What we are building** — a repeatable **round trip** for Jim's FG Scanner station. Jim
  presses one button, "Send my scans to Franz"; his station's data and only his *new or
  changed* images travel to Franz. Franz does the slow and skilled work on his fast machine —
  reading every page, fixing document numbers and sticky-note sheets, committing — and
  sends it back. Jim presses one button, "Install Franz's update", and his laptop ends up
  exactly as Franz left it, on the new software version. The first round trip also takes Jim
  from 0.5.4 to the new release, by the app's own auto-update.
- **Why** — Jim is in Ohio on a slow laptop, not comfortable with the computer, and Franz
  can only reach him over Teams. Waiting for his laptop is the problem; overnight transfers
  are not. And the program will change many more times before this case is done, so every
  cycle must be the same two buttons.
- **What it costs** — 12 prompts across 3 phases, roughly **three to four days** of build,
  one new FG Scanner release (0.8.0), plus two calls with Jim (setup, first cycle). The
  storage platform is its own spec. Transfers: about 15 minutes per 1.6 GB from Jim at his
  20 Mbps; the return is mostly the database (a few MB) plus the pages that changed.
- **What could go wrong** — (1) Jim scans while his data is with Franz → the return refuses
  rather than lose anything (§12). (2) The auto-update fails on his laptop → rehearsed first
  on his own data, with a verified backup before he presses Yes. (3) Franz's work changes
  Jim's settings or stamps Franz's name on Jim's evidence → the return keeps Jim's settings
  and a change log records what Franz did (§07).
- **What we need from you** — the Round B decisions: the transport for the first cycle, the
  storage platform as its own spec, what the release builds in, where reading happens from
  now on, and five smaller ones.

## 02 · Outcomes

- **Goal** — Jim's station and Franz's processing copy can be exchanged safely and repeatably,
  with only changed material moving, nothing lost, every change accounted for.
- **Who benefits** — Jim (two buttons, no waiting on his laptop); Franz (all heavy work on fast
  machines; evidence in the case file); counsel (a provable chain of custody: what was
  captured, by whom, and what Franz changed).
- **How we will know it worked**
  1. A cycle's manifest re-verifies on the receiving side with **0 mismatches** (every file's
     SHA-256), both directions.
  2. A second cycle with nothing new moves **only the database** (bytes moved ≤ 10 MB).
  3. After "Install Franz's update", Jim's station has the same groups/pages as Franz's copy,
     Jim's own settings unchanged, and `PRAGMA integrity_check` = ok.
  4. A cycle in which Jim scanned during the freeze **refuses to install** and changes nothing.
  5. The first cycle leaves Jim on the new release with his pre-update data verified on
     Franz's side before he pressed Yes.

## 03 · Scope and non-goals

**In scope**

1. **A station workspace switch** in FG Scanner (`--data-dir`): Franz runs Jim's station on the
   NucBox in its own folder, never mixed with his own.
2. **Station verbs in the CLI** (`fgscanner station pack | verify | apply`), shipped with every
   release so they never drift from the database schema; a content-addressed manifest
   (path, size, SHA-256) with renames paired by hash.
3. **Two shortcuts on Jim's desktop** — "Send my scans to Franz", "Install Franz's update" —
   each ending in one line he can read out on Teams: `OK 1234` or `NOT OK — call Franz`.
4. **A first-cycle sender** for 0.5.4 (a PowerShell script; 0.5.4 has no station verbs).
5. **The transport** for cycle 1 (Q-B1) and its setup with Jim.
6. **Release 0.8.0** with the above, rehearsed 0.5.4 → 0.8.0 on Jim's own data before Jim.
7. **A CI test that migrates a database from every shipped schema** (0.5.4 onward) to the
   current one, so a release that would break Jim's upgrade fails to build.
8. **Runbooks** for both sides; CLAUDE.md rules.

**Non-goals**

- **The storage platform** (Seafile, archive tier, offsite backup, the other apps' files) —
  its own spec (Q-B2).
- **No live sync of Jim's evidence or data folders.** Transfers are packs, verified, applied.
- **No portal upload page** (Cloudflare caps a request at 100 MB). **No AI** (§08a).
- **No change to the evidence contract** (`index.json`, `manifest.json`, field names).
- **Jim's import into the case file** itself (JimsStuff SPEC-2026-009 P10) — this spec
  delivers committed groups to it.

## 04 · Current state

Read 2026-10-09: FgmakerScanner `main` 440889b (v0.7.1; 0.6.0 and 0.7.1 drafts), tag v0.5.4;
JimsStuff `master` 271ce76; Jim's 9/25 data (`%APPDATA%\FGScanner.rehearsal-jim-laptop-2026-09-25`,
images copied to `C:\Evidence-Scans`); three research passes (scripts in the job folder).

**Jim's station at 9/25** — 0.5.4; data `%APPDATA%\FGScanner` (`fgscanner.db`, journal mode
`delete`, `trash\`, `recovery\`, `index-drafts\`, `tessdata\`); evidence `C:\Evidence-Scans`. 20
groups, all open; every page unread; DocNo blank on 886 of 1,368; Box empty on 18; 233
sticky-note refusals in 16 groups; Dental missing `scan_00001.jpg`. `CapturedBy` reads "Franz
Gerster" on his rows (his laptop account was set up under that name).

**Content identity** — `Pages.Checksum` is the file's SHA-256, set at adoption, refreshed only
by `ReorderService.RefreshChecksumAsync` (edits, auto-rotate, CLI). It is **a claim, not a
measurement**: on the 9/25 copy against today's images, 1,365 match, 2 differ, 1 missing — the
two are Defamation pages turned on this NucBox on 10-03/10-05 during development, after the
snapshot. `OriginalChecksum` is NULL although 3 `originals\` copies exist. `ImageHash` is a
perceptual hash, not identity. **Re-hashing is cheap**: 1.64 GB in 13 s here.

**What changes a group folder** — adoption adds `scan_NNNNN.jpg` (name fixed then); commit and
re-export write only `index.*` and `manifest.json` (manifest carries an export time); reading
writes `.md` sidecars (bytes differ every read); auto-orient and pixel edits rewrite the JPG and
add `originals\`; moving documents between groups renames files; trash moves files to
`%APPDATA%\FGScanner\trash`; reorder tools change only the database.

**What binds a database to a machine** — `Groups.DirectoryPath`, `Profiles.BaseDirectory`
(`C:\Evidence-Scans` on both sides — no rewrite); `TrashItems.TrashFolderPath` (user profile
path); `Settings` rows travel inside the database (`Feature.AutoOrient`, `Feature.IndexMode`,
`Email.SendWith`, `Index.DeciderName`, theme, update prompt); outside it: the Gemini key
(Credential Manager), `tessdata`, `recovery`, `index-drafts`, logs. The scanner device is not
stored. `CapturedBy` and the Operator default `$(user)` take the Windows account name.

**One station per Windows account** — the app hard-wires `%APPDATA%\FGScanner`; only the CLI
has a hidden `--db` (`CliRunner.cs:31`). A `StationPaths` record resolved once at startup is
the minimal change. The single-instance lock stays shared.

**The update path** — NetSparkle reads `releases/latest/download/appcast.xml` at every start,
asks, runs the installer `/VERYSILENT` (admin → UAC), does not relaunch. Never exercised end to
end; 0.7.1's signatures verified 2026-10-09 against 0.5.4's key. First start of a new version
copies the database to `fgscanner.db.bak-<version>` before migrating.

**Rehearsal (Phase 1) state** — on the NucBox, Franz's station data set aside as
`%APPDATA%\FGScanner.station-aside-2026-10-09`, Jim's 9/25 copy in place, 0.5.4 installed and
started (no update offered); publishing 0.7.1 is Franz's command (the session may not publish).

**Transport research** — no existing channel but USB. Cloudflare Tunnel: 100 MB per request,
CDN terms against bulk pictures. **Tailscale** (built into Unraid 7, free for 6 users) avoids
both. Seafile's sync is block-based (a renamed or unchanged file never re-uploads) with an
audit log in Pro (free for 3 users — to confirm); Nextcloud syncs whole files; Syncthing has
no audit. Offsite: Backblaze B2 $6.95/TB-month with Object Lock. Google Drive: 750 GB/day
upload cap; 5 TB consumer tier (larger tiers exist in-account). Franz's other apps store files
as plain folders (FGMakerEcomFeeder 2.9 GB; FGMakerMaster nearly empty) and need shares, not
sync.

**Not examined:** Jim's current data; his account type; real reading speed on his laptop.

## 05 · Questions for Franz

**Round A (2026-10-09, revise):** Q1 = (b) round trip as a delta; Q2 = research first; Q3 = (a)
backup verified before update; Q4 = (b) auto-orient off for the backlog; Q5 Quick Assist; Q6
0.7.1 back to draft; Q7 ask Box on the day; C1–C3 agreed. General: deep research, check for
contradictions, automate, check nothing is missed.

**What Revision B changed, and why** (a contradiction check of the Round A answers):

| Round A | Conflict found | Revision B |
|---|---|---|
| Q1 b vs §01/§03/§08/§17 (written for Q1 a) | the whole plan assumed work on Jim's laptop | rebuilt around the cycle pack → verify → process → package → apply |
| Q1 b "send back an install" vs auto-update | two update channels; a release published mid-cycle could leave Jim on a different version from the one that migrated his data | apply checks the version and the last migration; releases are published only at a cycle's return (Q-B9) |
| Q3 a vs Q1 b | the same step twice | merged: the verified pack **is** the backup, taken with the app closed, before the update |
| Q4 b vs round trip | `Feature.AutoOrient` lives in the database, so "off" would travel back and stop Jim's new scans straightening | auto-orient off only in Franz's workspace; apply keeps Jim's settings (Q-B5) |
| C1 "scripts, not app features" vs "updated a lot" | scripts drift from each release's schema | scripts for the first cycle only; station verbs ship in the app (Q-B3) |
| C2 "never sync live folder" vs Q2 sync platforms | a sync client on Jim's evidence folder could carry a deletion back | the shared folder is only an outbox/inbox; pack and apply refuse a path inside the live folders |
| "images don't change, only the data" | reading writes sidecars, commit writes index files, moves rename, trash removes | the return manifest states the **final** state of every group folder; apply reaches it and re-verifies |
| Q7 Box asked on the day | Franz needs Box before he processes | collected before processing (Q-B6) |
| Q5 Quick Assist | Jim already uses Teams; a second tool is a second thing to learn | Teams "give control" (Q-B7) |

**Blocking**

1. **Q-B1 — Transport for the first cycle.** (a) **Tailscale + a plain share on the Tower** —
   an hour to set up, one install for Jim, no third party holds the data, plain files fit the
   importer and our manifests; (b) **Seafile now** — wait 2–3 days for the platform spec first;
   (c) **Google Drive** now. *Recommended:* (a); the platform can replace the share later
   without changing the station verbs.
2. **Q-B2 — The storage platform as its own spec.** (a) **Seafile Pro on the Tower (3 free users),
   Tailscale, offsite to Backblaze B2 with Object Lock**, archive libraries on the array; the
   other apps keep plain shares, backed up offsite by the same job; (b) Nextcloud instead
   (plain files, heavier, whole-file sync); (c) not now. *Recommended:* (a), written next.
3. **Q-B3 — What release 0.8.0 builds in.** (a) the workspace switch, the station verbs, the two
   shortcuts, the CI migration test — scripts only for cycle 1's sender; (b) scripts only, app
   later. *Recommended:* (a): every later cycle is then two buttons that match the release.
4. **Q-B4 — Reading from now on.** (a) **Off on Jim's laptop**: his new scans are read on the
   NucBox each cycle (his laptop never grinds); (b) read on capture on his laptop.
   *Recommended:* (a) — it is exactly the waiting Franz wants to avoid; the cost is that
   search on Jim's own station only covers pages read so far.

**Non-blocking (proceeding as stated unless corrected)**

5. **Q-B5 — Auto-orient** off only in Franz's workspace for the backlog; apply keeps Jim's own
   settings rows (his new scans still straighten).
6. **Q-B6 — Box values** collected before processing: Jim tells Franz on Teams which box each
   group came from; Franz enters them.
7. **Q-B7 — Screen sharing** through Teams "give control" (Jim clicks UAC himself in any tool).
8. **Q-B8 — Commit on the NucBox.** Franz commits each group he has fixed and imports it into
   the case file from there; the return carries the committed state. A group Franz cannot
   finish (it needs the paper — an orphaned as-found sheet, Dental) is left open, *not*
   Proposed, and listed in a "needs Jim" sheet sent back with the package.
9. **Q-B9 — Jim goes straight from 0.5.4 to 0.8.0.** 0.7.1 and 0.6.0 stay drafts; a release is
   published only when a cycle's return needs it.

## 06 · Assumptions

| # | Assumption | If wrong |
|---|---|---|
| A1 | Jim's data still lives at `%APPDATA%\FGScanner` and `C:\Evidence-Scans`. | The sender takes both as parameters and lists what it found before packing. |
| A2 | Jim has committed nothing since 9/25. | Committed groups simply pack and verify like the rest. |
| A3 | Jim's data is under ~5 GB. | Cycle 1 takes longer (~45 min per 5 GB); overnight is fine. |
| A4 | Jim's account can approve UAC. | He needs the admin password; Franz finds out first. |
| A5 | Jim can follow two desktop shortcuts with Franz on Teams. | Franz drives through Teams "give control". |

## 07 · Data model

No change to FG Scanner's database or the evidence contract. New files only:

- **`station-manifest.json`** (written atomically, one per pack): header `{format, station,
  sentAt, appVersion, lastMigration, dbSha256, evidenceRoot, basedOn}`; `files[]` `{rel, size,
  sha256, kind: image|original|sidecar|index|trash, groupId?, pageId?, dbChecksum?}`.
  `basedOn` is the previous manifest's hash, so "what Franz already has" is known without a
  second round trip.
- **`changes.jsonl`** in each return package — the chain-of-custody record of what Franz
  changed: a row-level diff of the database before and after processing (table, key, column,
  old, new), with Franz's name and time. Archived with the package; travels to the portal with
  the import (it does not alter `capturedBy`, which is never rewritten — ADR-0005).
- **Kept on apply (never overwritten):** Jim's `Settings` rows, profile scan settings,
  `index-drafts\`, `recovery\` (apply refuses if it holds anything), Credential Manager.

## 08 · Architecture and approach

```
Jim's laptop                         transport (Q-B1)          Franz's NucBox (workspace Jim)
"Send my scans to Franz"
  app closed, freeze checks
  hash all → manifest (basedOn)
  pack: db + files Franz lacks ──────▶ outbox/inbox ─────────▶  station verify (0 mismatches)
  keep "sent" manifest + db hash                                 = Jim's backup (Q3 a)
                                                                 open in workspace (0.8.0 migrates)
                                                                 read · Box · Propose · notes · commit
                                                                 import committed groups → portal
                                                                 station pack --return:
"Install Franz's update"            ◀──────────────────────────   db + changed/new files + deletions
  version + migration check                                      + final-state manifest + changes.jsonl
  freeze check (db hash = sent)                                  + needs-Jim sheet
  stage → verify → swap atomically
  keep Jim's settings · re-verify
  "OK 1234"
```

- **Same verbs on both sides**, shipped in the app's `cli\`, so the format and the schema can
  never drift apart. The first sender (0.5.4) is a PowerShell port of `pack` only.
- **Content-addressed**: a renamed file is matched by hash and renamed in place, never resent.
- **Evidence moves only as verified packs**; the transport folder is never inside a live folder.
- **Alternatives rejected**: work on Jim's laptop (Round A); live sync of the station (a
  deletion could travel back); one Inno package carrying evidence (an elevated installer can
  see the wrong user profile); portal upload (100 MB cap).

## 08a · AI opportunity assessment

| | Sticky-note sheets, document boundaries |
|---|---|
| **What it would do** | Suggest fixes for the 233 refused note sequences. |
| **Why not** | The rule is mechanical (`ImportRefusals`); Propose documents fills DocNo; what remains needs the paper. |
| **Recommendation** | **None.** |

## 09 · Design system compliance

No app UI beyond two Start-menu shortcuts and console output in plain words.

## 10 · Acceptance criteria

> **AC-1** — Rehearsal: 0.5.4 on Jim's 9/25 data is offered the release, installs it through the
> real prompt and UAC, starts it; 20 groups / 1,368 pages; `fgscanner.db.bak-<ver>` present;
> no image changed by the first start. *Manual, §22.*

> **AC-2** — `station pack` → `station verify` on an exact copy: 0 mismatches; a flipped byte, a
> missing file and an extra file are each named. *`StationManifestTests`.*

> **AC-3** — A renamed file is shipped as a rename (0 bytes), an unchanged one not at all.
> *`StationPackTests.RenamesAreNotResent`, `SecondPackWithNoChangesShipsOnlyTheDatabase`.*

> **AC-4** — `pack` refuses while the app is open, while `recovery\` holds a session, or with
> reading jobs pending. *`StationPackTests`.*

> **AC-5** — `apply` refuses on a version or migration mismatch, on a database that differs from
> the one sent (Jim worked during the freeze), and with the app open; refusing changes nothing.
> *`StationApplyTests`.*

> **AC-6** — `apply` keeps the receiving station's `Settings` rows and `index-drafts`, reaches the
> manifest's final state (adds, changes, renames, deletions moved aside — never erased), passes
> `integrity_check`, and keeps the replaced database and files for rollback. *`StationApplyTests`.*

> **AC-7** — `--data-dir` puts the database, trash, recovery, drafts and logs in that folder and
> nothing in `%APPDATA%\FGScanner`. *`StationPathsTests`.*

> **AC-8** — CI migrates a fixture database from every shipped schema (0.5.4 onward) and opens
> it. *`MigrationLadderTests`.*

> **AC-9** — `changes.jsonl` lists every changed cell between two databases.
> *`StationChangeLogTests`.*

> **AC-10** — First cycle with Jim: pack verified on the NucBox before the update; Jim on 0.8.0;
> the return applied; counts match. *Manual, §22.*

## 11 · Test strategy

Failing tests first, in `tests/FgScanner.Core.Tests/Station/` (pure manifest/pack/apply logic
against temp folders and a temp SQLite database) and `tests/FgScanner.Data.Tests/` (migration
ladder: one fixture `.db` per shipped schema, created from the tagged release). The first-cycle
PowerShell sender is checked by a test that packs a fixture and verifies it with the C# verb
(the two must agree byte for byte). Real data only in the rehearsals and with Jim.

## 12 · Edge cases and failure modes

| Case | Expected behaviour | Where |
|---|---|---|
| Jim scans or edits during the freeze | apply refuses ("NOT OK — call Franz"); his new pages stay; Franz packs again | apply (db hash) |
| A file differs on arrival | verify names it; that pack is re-sent; never patched by hand | verify |
| Upload interrupted / laptop sleeps | the sender holds a keep-awake; transport resumes; verify catches partial | sender, transport |
| Group needs the paper | left open, not Proposed; on the needs-Jim sheet | Q-B8 |
| Leftover `fgscanner.db-journal` | apply refuses | apply |
| Release published mid-cycle | apply refuses on version mismatch | apply, Q-B9 |
| Jim clicks No on the update | nothing changes; asks next start | UpdateService |
| Installer aborts | data untouched; re-run from the release page with Franz on Teams | runbook |

## 13 · Security and configuration

Privileged evidence leaves Jim's laptop: over Tailscale (encrypted, no third party) to a share
on the Tower that only Franz's and Jim's accounts reach; packs kept on the Tower until the
platform spec's archive and offsite rules apply, and the NucBox workspace copy deleted after
each cycle's import (§17). No credential in any script, repo or chat. Counsel to be told where
copies of the evidence live (the platform spec records it).

## 14 · Observability

Every pack/verify/apply writes a one-line result (`OK 1234 · 2,140 files · 3.1 GB`) and a log
beside the manifest; every return carries `changes.jsonl` and the needs-Jim sheet.

## 15 · Performance and scale

Hashing ~2–3 min on Jim's laptop for ~2 GB; cycle 1 upload ~15 min per 1.6 GB; later cycles the
database plus new pages (~1.15 MB each). Reading on the NucBox ~1.1 s per page.

## 16 · Regression risk

| # | What could break | Why | Mitigation |
|---|---|---|---|
| R1 | Jim's app does not start after the update | auto-update never used end to end | rehearsal on his data; backup verified first; re-run path |
| R2 | Evidence lost when the database is swapped | new pages on Jim's side not in Franz's copy | freeze check refuses |
| R3 | Jim's settings overwritten | settings live in the database | apply keeps them; test |
| R4 | Franz's account name stamped on Jim's evidence | `CapturedBy`/Operator default to the Windows user | no captures in Franz's workspace; Operator set explicitly; change log |
| R5 | A committed group imported twice | committed on NucBox, later again from Jim | the portal ledger refuses a sealed batch; tested in P12 |
| R6 | Schema skew | releases between cycles | version/migration gate; CI ladder |
| R7 | Workspace switch breaks Franz's own station | new path plumbing | default unchanged when no `--data-dir`; test |

## 17 · Migration and rollback

**Phase 1 — rehearsal (NucBox, in progress):** finish 0.5.4 → 0.7.1 on Jim's 9/25 data
(Franz publishes; the session may not), then put 0.7.1 back to draft and restore Franz's own
data folder. Repeat with 0.8.0 before Jim.

**Phase 3 — the first cycle:**
1. `[Franz]` Set up Tailscale with Jim on Teams; confirm UAC rights; collect Box values.
2. `[Jim]` FG Scanner closed → run the 0.5.4 sender (placed by Franz) → "OK 1234".
3. `[Claude]` `station verify` on the NucBox: 0 mismatches = Jim's backup.
4. `[Franz]` Publish 0.8.0. `[Jim]` start FG Scanner → Yes → UAC → start again.
5. `[Claude/Franz]` Process in workspace Jim; commit what is finishable; import into the portal;
   `station pack --return`.
6. `[Jim]` "Install Franz's update" → "OK 5678". `[Franz]` check counts on Teams.

**Rollback:** apply keeps the replaced database and files aside; restoring them is one command.
Before Jim's first commit the verified pack restores him exactly. **Point of no return:** a
group's commit (renames and index files) — already the case today.

## 18 · Documentation updates

- **CLAUDE.md (FG Scanner)** — stations exchange data only by `station pack/verify/apply`;
  never sync a live folder; releases published only at a cycle's return; `--data-dir` for a
  second station; no captures in another station's workspace.
- **New** `docs/station-round-trip.md` (both sides, tagged steps) and an ADR for the manifest.
- **`docs/release.md`** — every release is rehearsed on Jim's latest pack before publishing.
- **JimsStuff `docs/fgscanner-import-runbook.md`** — committed groups arrive from the NucBox
  workspace, with `changes.jsonl`.
- **Memory** — the cycle and the transport.

## 19 · Phase plan

### Phase 1 — Prove the update path (now)
Finish the 0.7.1 rehearsal; record. **Not here:** anything on Jim's laptop.

### Phase 2 — Build 0.8.0
Workspace switch; station verbs; first-cycle sender; shortcuts; CI ladder; reviews; release
drafted and rehearsed 0.5.4 → 0.8.0 on Jim's 9/25 data. **Not here:** Jim's data.

### Phase 3 — The first cycle
Transport with Jim, pack, verify, update, process, import, return, apply.

## 20 · Prompt pack

Written after approval. Planned (12): P1 finish rehearsal · P2 `StationPaths`/`--data-dir` ·
P3 manifest + verify · P4 pack (delta, renames, freeze record) · P5 apply (gates, settings kept,
atomic, rollback) · P6 change log + needs-Jim sheet · P7 first-cycle sender (PowerShell) +
shortcuts · P8 CI migration ladder · P9 code review · P10 security review · P11 release 0.8.0
draft + rehearsal · P12 the first cycle with Jim.

## 21 · Definition of done

- [ ] All acceptance criteria met
- [ ] Failing tests written first, now passing
- [ ] Full suite green; Release build 0 warnings; format clean
- [ ] `/code-review max` run, findings resolved or accepted in writing
- [ ] Security review passed
- [ ] Documentation updated per §18
- [ ] Jim's first cycle complete and verified
- [ ] Rollback rehearsed

## 22 · Sign-off

| | |
|---|---|
| **Review round answered** | ☑ Round A 2026-10-09 (revise) — https://claude.ai/artifact/LTKoCtnKXZZTsoEZQVNBpV · ☐ Round B published 2026-10-09 — https://claude.ai/artifact/EjYRfaTz1UDJ1zQHuQzEPi (doc SPEC-SCANNER-2026-010-rB) |
| **Franz approved** | ☐ date: |
| **Built** | ☐ date: |
| **Verified in production** | ☐ date: |
