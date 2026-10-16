# SPEC-SCANNER-2026-010 — Jim's remote update and his data

| | |
|---|---|
| **Status** | Approved — Revision C approved in Round C 2026-10-16 (every recommendation taken) |
| **Revision** | C — 2026-10-15: the P1 rehearsal found auto-update broken in 0.5.4 and 0.7.1, so Jim's 0.8.0 arrives as a guided manual install, 0.8.0 fixes the updater (test first, rehearsed without reaching Jim), and processing moves from the NucBox to Franz's PC. B — 2026-10-09: rebuilt around Round A; approved in Round B with the Google Drive transport |
| **Tier** | System — it changes the software on Jim's live evidence station and moves evidence between machines |
| **Author** | Claude, for Franz Gerster |
| **Date** | 2026-10-09 |
| **Project** | FG Scanner (station side) with JimsStuff (receiving side) |
| **Parent** | JimsStuff SPEC-2026-007 P12 step 7; JimsStuff SPEC-2026-009 P10 (Jim's real import) |
| **Related** | A storage platform (Seafile/Nextcloud, archive, offsite) — **deferred by Franz** (Q-B2 = c); he researches it himself first |

---

## 01 · Management summary

- **What we are building** — a repeatable **round trip** for Jim's FG Scanner station. Jim
  presses one button, "Send my scans to Franz"; his station's data and only his *new or
  changed* images travel to Franz. Franz does the slow and skilled work on his fast machine —
  reading every page, fixing document numbers and sticky-note sheets, committing — and
  sends it back. Jim presses one button, "Install Franz's update", and his laptop ends up
  exactly as Franz left it, on the new software version. The first round trip also takes Jim
  from 0.5.4 to the new release — by a **guided install on Teams**, because the rehearsal
  (2026-10-15) proved the app's own auto-update cannot install anything on 0.5.4 or 0.7.1.
  The new release repairs auto-update, so every later version can arrive by itself.
- **Why** — Jim is in Ohio on a slow laptop, not comfortable with the computer, and Franz
  can only reach him over Teams. Waiting for his laptop is the problem; overnight transfers
  are not. And the program will change many more times before this case is done, so every
  cycle must be the same two buttons.
- **What it costs** — 13 prompts across 3 phases (three already done), roughly **three to four days** of build,
  one new FG Scanner release (0.8.0), plus two calls with Jim (setup, first cycle). Files
  travel through a Google Drive folder shared between Franz and Jim. Transfers: about 15
  minutes per 1.6 GB from Jim at his 20 Mbps; the return is mostly the database (a few MB)
  plus the pages that changed.
- **What could go wrong** — (1) Jim scans while his data is with Franz → the return refuses
  rather than lose anything (§12). (2) The install fails on his laptop → the same install was
  rehearsed on a copy of his data (it passed: nothing lost, every image identical), and his
  data is verified on Franz's side before he installs. (3) Franz's work changes
  Jim's settings or stamps Franz's name on Jim's evidence → the return keeps Jim's settings
  and a change log records what Franz did (§07).
- **Decided (Round B, 2026-10-09)** — Google Drive shared folder; no storage platform for
  now; the station verbs built into 0.8.0; Jim straight from 0.5.4 to 0.8.0.
- **Decided in Revision C (approved Round C, 2026-10-16)** — Jim installs 0.8.0 by hand with Franz on
  Teams; 0.8.0 repairs the updater and that repair is rehearsed without any release reaching
  Jim; pages are read and processed on Franz's PC (the NucBox no longer runs FG Scanner).
  Cost: one more prompt (13), about half a day.

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
     Franz's side before he installed it.
  6. A station on 0.8.0 that is offered a newer release and answers Yes ends up on that
     release — rehearsed on the spare under an account whose profile path has a space.

## 03 · Scope and non-goals

**In scope**

1. **A station workspace switch** in FG Scanner (`--data-dir`): Franz runs Jim's station on his
   PC (FGERSTERPC) in its own folder, never mixed with his own.
2. **Station verbs in the CLI** (`fgscanner station pack | verify | apply`), shipped with every
   release so they never drift from the database schema; a content-addressed manifest
   (path, size, SHA-256) with renames paired by hash.
3. **Two shortcuts on Jim's desktop** — "Send my scans to Franz", "Install Franz's update" —
   each ending in one line he can read out on Teams: `OK 1234` or `NOT OK — call Franz`.
4. **A first-cycle sender** for 0.5.4 (a PowerShell script; 0.5.4 has no station verbs).
5. **The transport** — a Google Drive folder shared between Franz and Jim (Q-B1 = c), and its
   setup with Jim. The station verbs only read and write a folder, so a later platform
   replaces the transport without changing them.
6. **Release 0.8.0** with the above, rehearsed 0.5.4 → 0.8.0 on Jim's own data before Jim, on
   the spare WIN-G31UIM7G129.
7. **A CI test that migrates a database from every shipped schema** (0.5.4 onward) to the
   current one, so a release that would break Jim's upgrade fails to build.
8. **Runbooks** for both sides; CLAUDE.md rules.
9. **A working updater in 0.8.0** (Revision C): after Yes, FG Scanner starts the downloaded
   installer itself and closes; the install no longer depends on NetSparkle's generated
   script. Rehearsed end to end without any release reaching Jim (§17).

**Non-goals**

- **A storage platform** (Seafile, Nextcloud, archive tier, offsite backup, the other apps'
  files) — deferred: Franz researches it himself and comes back to it (Q-B2 = c). The
  research in §04 stays on record for that day.
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
two are Defamation pages turned on the NucBox on 10-03/10-05 during development, after the
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

**The update path** — NetSparkle 3.1.0 (`UpdateService.cs`, identical in 0.5.4 and 0.7.1)
reads `releases/latest/download/appcast.xml` at every start (Ed25519 strict), asks, downloads,
then calls `InstallUpdate`, which writes a `.cmd` script that waits for the app to exit and then
runs the installer `/VERYSILENT /NORESTART` (admin → UAC). `RelaunchAfterUpdate = false`. The
appcast's download link is `releases/latest/download/fgscanner-<ver>-win-x64.exe`
(`release.yml:140`), so a release's installer is reachable only while it is the Latest. First
start of a new version copies the database to `fgscanner.db.bak-<version>` before migrating.

**The rehearsal (P1, 2026-10-15) — the updater does not work.** Run on the spare
WIN-G31UIM7G129 (the NucBox was busy) over Jim's 9/25 copy: 0.5.4 offered 0.7.1, Yes was
pressed, and nothing visible happened — no UAC, the app stayed open, still 0.5.4. Three defects,
all in how the install step is wired, none in the download or the signature check:
1. **The app never closes.** Nothing handles NetSparkle's close request, so the script's wait
   for the app's process never ends.
2. **The script gives up after 90 seconds** (`exit /b 1`) and nothing is logged.
3. **The installer is run by an unquoted path with no `.exe`** — the download lands in
   `%LOCALAPPDATA%\Temp\<guid>` with no extension, and under `C:\Users\Franz Gerster\…` the
   script fails with `'C:\Users\Franz' is not recognized`. Jim's laptop account is also named
   "Franz Gerster", so his station fails the same way.

So **published releases never reached a 0.5.4 or 0.7.1 station** (they are offered and then
fail), and Jim's 0.5.4 cannot be updated except by hand. The data half passed: a manual 0.7.1
install migrated Jim's copy (`fgscanner.db.bak-0.7.1` written, `integrity_check` ok, 20 groups /
1,368 pages) and all 1,411 evidence files stayed byte-identical. Franz's NucBox data is
restored; FG Scanner is **uninstalled from the NucBox** (2026-10-15) — it has no scanner and
Franz now works on FGERSTERPC.

**Franz's PC (FGERSTERPC)** — the development machine; Google Drive for Desktop runs here
(`G:\My Drive`); `C:\Evidence-Scans` does not exist yet (Jim's 9/20 review copy is on
`D:\Evidence-Scans` and stays untouched). Jim's workspace evidence goes to
`C:\Evidence-Scans`, the path his database names, so no path is rewritten.

**Transport research** — no existing channel but USB. Cloudflare Tunnel: 100 MB per request,
CDN terms against bulk pictures. **Tailscale** (built into Unraid 7, free for 6 users) avoids
both. Seafile's sync is block-based (a renamed or unchanged file never re-uploads) with an
audit log in Pro (free for 3 users — to confirm); Nextcloud syncs whole files; Syncthing has
no audit. Offsite: Backblaze B2 $6.95/TB-month with Object Lock. Google Drive: 750 GB/day
upload cap; 5 TB consumer tier (larger tiers exist in-account). Franz's other apps store files
as plain folders (FGMakerEcomFeeder 2.9 GB; FGMakerMaster nearly empty) and need shares, not
sync.

**Google Drive as the transport (Round B)** — Drive for Desktop puts the shared folder under
`G:\My Drive` (a folder *shared with* Jim appears there once he adds a shortcut to it). In its
default *stream* mode a file Jim receives is a placeholder until opened, so the return folder is
marked **Available offline** at setup; uploads resume after sleep or a dropped line. Drive keeps
bytes exactly and never merges, but a re-sent file of the same name becomes `name (1)` — so
every pack goes in its own new folder and is never written twice. Drive gives no "upload
finished" signal a script can read, so Jim's `OK` means *packed*, and Franz's `verify` is the
receipt. Upload cap 750 GB/day (irrelevant here). Franz's PC already runs Drive for Desktop.

**Not examined:** Jim's current data; his account type; whether Jim has a Google account
(he uses Yahoo Mail — a Google account can be made on an existing Yahoo address); real reading
speed on his laptop.

## 05 · Questions for Franz

**Round C (2026-10-16, approve):** Q-C1 (a), Q-C2 (a), Q-C3 (a), Q-C4–Q-C8 as stated — every
recommendation, no notes. Raised by the P1 rehearsal (§04); everything Round B decided stands
unless an item below changes it.

*Blocking*

1. **Q-C1 — How Jim's 0.8.0 reaches his laptop.** (a) **Through the Drive folder:** Franz puts
   the 0.8.0 installer (from the release, hash checked against `SHA256SUMS`) in `to-jim\`; on
   Teams with "give control", Jim closes FG Scanner, double-clicks it, clicks UAC, starts it,
   reads out the version — the same steps as the rehearsal that passed. 0.8.0 does not have to
   be published for this. (b) Jim downloads it from the GitHub release page in his browser
   (0.8.0 must be published first, and he navigates GitHub). *Recommended:* (a) — the folder is
   already set up in step 1, it is one double-click, and no release goes live.
2. **Q-C2 — How 0.8.0 repairs the updater.** (a) **Keep NetSparkle for the check, the download
   and the signature; FG Scanner runs the install itself:** after the download is verified it is
   copied to `%TEMP%\FGScanner\update\fgscanner-<ver>-win-x64.exe`, started directly
   (no `cmd` script, no path quoting, Windows shows UAC), and the app closes itself; Inno's
   `CloseApplications` remains the backstop. (b) Wire NetSparkle's own close event and file
   name and keep its script — still a 90-second wait inside a generated `.cmd` nobody logs. (c)
   Remove auto-update; every release is a guided install. *Recommended:* (a): the three defects
   all live in the script, so the script goes; the part that worked (signed check and download)
   stays. Cost of (a): about half a day, one new class with its test.
3. **Q-C3 — How the repaired updater is rehearsed without reaching Jim.** (a) **A pre-release:**
   `release.yml` writes the version's own download link into the appcast
   (`releases/download/v<ver>/…`, not `latest`); 0.8.0 reads an optional `FGSCANNER_APPCAST_URL`
   that is honoured only for this repository's release links (signatures stay strict); v0.8.0
   is first published as a GitHub **pre-release**, which `releases/latest` never serves, so no
   station is offered it; on the spare, the same 0.8.0 code built locally and stamped 0.7.99
   points at it and must reach 0.8.0 by pressing Yes. No extra test version exists; v0.8.0
   becomes the Latest only at Q-C6. (b) No rehearsal: the first real test is Jim's next update, with Franz on
   Teams. *Recommended:* (a): (b) is exactly how the broken updater went unnoticed for two
   months. Cost of (a): a pre-release is public on GitHub like every release (the repository is
   public), and one environment variable that the runbook documents.

*Non-blocking (proceeding as stated unless corrected)*

4. **Q-C4 — Processing on Franz's PC, not the NucBox.** `station verify`, the workspace `Jim`,
   reading, Propose, commit, import and `station pack --kind return` all run on FGERSTERPC; Jim's
   evidence there lives at `C:\Evidence-Scans`. The verified pack is copied to the Tower backup
   as before.
5. **Q-C5 — Rehearsals on the spare.** P11's 0.5.4 → 0.8.0 rehearsal and the updater rehearsal
   run on WIN-G31UIM7G129 (`ssh spare`), which already holds Jim's 9/25 copy; Franz's own
   station is never involved. For P11 the spare is reset: 0.5.4 reinstalled and its data
   folder re-copied from the 9/25 original (hash checked).
6. **Q-C6 — 0.8.0 is published only after Jim is on it.** Jim installs from `to-jim\` (Q-C1 a),
   then 0.8.0 becomes the Latest, so later releases have a working updater to arrive through.
   0.7.1 and 0.6.0 stay drafts (Q-B9 unchanged).
7. **Q-C7 — The read-out code stays 4 characters.** It only confirms both sides read the same
   manifest; every file's SHA-256 does the real checking, and the folder is shared with Jim
   alone. 8 characters (`96da-41c3`) is a one-line change if you prefer it.
8. **Q-C8 — The rule "published releases auto-update stations silently" is corrected** in
   CLAUDE.md and memory: 0.5.4–0.7.1 are offered an update and cannot install it; from 0.8.0
   they can. Release discipline (publish only at a cycle's return) stays.

**What Revision C changed:** §01, §02 (outcome 6), §03 (items 1, 6, 9), §04 (update path,
rehearsal, Franz's PC), §08, §10 (AC-1, AC-10, AC-11, AC-12), §12, §13, §15, §16 (R1, R8),
§17, §18, §19, §20, §22.

**Round B (2026-10-09, approve):** Q-B1 = **(c) Google Drive**; Q-B2 = **(c) not now** —
Franz: "I need to do more research on seafile and nextcloud before i continue with either one of
them. Remove them for now and use a google share I will come back to this at a later date";
Q-B3 (a); Q-B4 (a); Q-B6–Q-B9 and C-B1–C-B3 as stated. Applied: §01, §03, §04, §08, §12, §13,
§17, §19, §20. What it means: Google holds copies of privileged evidence in transit — each pack
is deleted from the folder (and Drive's bin) once it is verified or applied (§13).

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
Jim's laptop                    Google Drive shared folder     Franz's PC (workspace Jim)
"Send my scans to Franz"
  app closed, freeze checks
  hash all → manifest (basedOn)
  pack: db + files Franz lacks ──────▶ to-franz\<pack> ──────▶  station verify (0 mismatches)
  keep "sent" manifest + db hash                                 = Jim's backup (Q3 a)
                                                                 open in workspace (0.8.0 migrates)
                                                                 read · Box · Propose · notes · commit
                                                                 import committed groups → portal
                                                                 station pack --return:
"Install Franz's update"            ◀──── to-jim\<return> ────   db + changed/new files + deletions
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
- **The shared folder** `FGScanner-Jim` (owned by Franz, shared only with Jim's Google account,
  no link sharing) holds `to-franz\` and `to-jim\`. Each pack is a new folder named
  `<station>-<yyyy-MM-dd-HHmm>-<kind>` and is never written to twice; its manifest is written
  **last**, so a receiver that sees no manifest knows the pack is still arriving, and verify
  refuses anything short of every file present with its hash.
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

> **AC-1** — Rehearsal: on Jim's 9/25 data, 0.5.4 is replaced by the new release through the
> install Jim will use (Revision C: a guided manual install), and it starts; 20 groups / 1,368
> pages; `fgscanner.db.bak-<ver>` present; `integrity_check` ok; no image changed by the first
> start. *Manual, §22.* **Passed for 0.7.1 on 2026-10-15**; repeated for 0.8.0 in P11.

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

> **AC-10** — First cycle with Jim: pack verified on Franz's PC before the update; Jim on 0.8.0;
> the return applied; counts match. *Manual, §22.*

> **AC-11** — After Yes, the updater copies the verified download to
> `%TEMP%\FGScanner\update\fgscanner-<ver>-win-x64.exe`, starts it directly with
> `/VERYSILENT /NORESTART` (no `cmd`, no script), and the app shuts down; a profile path with a
> space and a download with no extension both work; a start that fails (UAC refused) is logged
> and the app stays open. Installer only from the release's own link; the appcast override is
> refused unless it is this repository's release link. *`UpdateInstallerTests`.*

> **AC-12** — Updater rehearsal on the spare: the 0.8.0 code built locally as 0.7.99, pointed
> at v0.8.0 published as a **pre-release**, is offered it; Yes → UAC → the app closes; 0.8.0
> starts on the next launch with its data intact; `releases/latest/download/appcast.xml` still
> names 0.5.4 before and after. *Manual, P11.*

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
| Upload interrupted / laptop sleeps | the sender holds a keep-awake while packing; Drive resumes the upload; verify refuses a partial pack (no manifest yet, or a file short) | sender, Drive, verify |
| Return still downloading on Jim's side | the `to-jim` folder is Available offline; apply hashes every file and refuses a placeholder or short file | apply |
| Jim's disk too small for the pack copy in Drive's cache | the sender checks free space (pack size + 1 GB) first and says NOT OK | sender |
| A pack folder name already exists | the sender never reuses a name (minute stamp + counter) | sender |
| Group needs the paper | left open, not Proposed; on the needs-Jim sheet | Q-B8 |
| Leftover `fgscanner.db-journal` | apply refuses | apply |
| Release published mid-cycle | apply refuses on version mismatch | apply, Q-B9 |
| Jim clicks No on the update | nothing changes; asks next start | UpdateService |
| Installer aborts | data untouched; re-run from the release page with Franz on Teams | runbook |
| A release is published while Jim is still on 0.5.4 | he is offered it; Yes downloads ~150 MB and nothing else happens (the P1 defects); no data touched | §04, Q-C6 |
| UAC refused after Yes on 0.8.0 | the install does not start; the app stays open and logs it; asks again next start | AC-11 |
| The app cannot close (a sheet in hand, a modal open) | the updater waits for the normal shutdown path; a capture in hand is never abandoned to install | AC-11, P8b |

## 13 · Security and configuration

Privileged evidence leaves Jim's laptop through Google Drive (Franz's decision, Round B):
encrypted in transit and at rest by Google, but **Google holds a copy while it is in the
folder**. So: the folder is shared with Jim's account only (restricted, no "anyone with the
link"); each pack is deleted from the folder **and from Drive's bin** once it is verified on
Franz's side (`to-franz`) or applied on Jim's (`to-jim`), and the verified copy lives on
Franz's PC and the Tower backup instead. The workspace copy on Franz's PC is kept until the
import, then the Tower copy is the record. The installer placed in `to-jim\` (Q-C1) is checked
against the release's `SHA256SUMS` before and after it travels, and deleted with the folder's
other contents. The appcast override (Q-C3) accepts only
`https://github.com/fgerster1/fgScanner/releases/download/…`; the Ed25519 check is unchanged. No credential in any script, repo or chat; Jim's Google password
is his own, entered by him. Counsel to be told that packs pass through Google Drive.

## 14 · Observability

Every pack/verify/apply writes a one-line result (`OK 1234 · 2,140 files · 3.1 GB`) and a log
beside the manifest; every return carries `changes.jsonl` and the needs-Jim sheet.

## 15 · Performance and scale

Hashing ~2–3 min on Jim's laptop for ~2 GB; cycle 1 upload ~15 min per 1.6 GB; later cycles the
database plus new pages (~1.15 MB each). Reading measured on the NucBox at ~1.1 s per page;
Franz's PC not yet measured (P11 records it).

## 16 · Regression risk

| # | What could break | Why | Mitigation |
|---|---|---|---|
| R1 | Jim's app does not start after the update | a new version on his data | the same manual install rehearsed on his data (passed for 0.7.1); backup verified first; re-run path |
| R2 | Evidence lost when the database is swapped | new pages on Jim's side not in Franz's copy | freeze check refuses |
| R3 | Jim's settings overwritten | settings live in the database | apply keeps them; test |
| R4 | Franz's account name stamped on Jim's evidence | `CapturedBy`/Operator default to the Windows user | no captures in Franz's workspace; Operator set explicitly; change log |
| R5 | A committed group imported twice | committed on Franz's PC, later again from Jim | the portal ledger refuses a sealed batch; tested in P12 |
| R6 | Schema skew | releases between cycles | version/migration gate; CI ladder |
| R7 | Workspace switch breaks Franz's own station | new path plumbing | default unchanged when no `--data-dir`; test |
| R8 | The repaired updater closes the app mid-capture, or installs something unsigned | it now owns the install and the shutdown | the download is used only after NetSparkle's strict check; shutdown goes through the normal close path; AC-11 tests; AC-12 rehearsal |
| R9 | A rehearsal release reaches Jim | a published release is offered to every station | v0.8.0 is a pre-release until Jim runs it, and `releases/latest` never serves a pre-release; the checkpoint reads the Latest appcast before and after |

## 17 · Migration and rollback

**Phase 1 — rehearsal: done 2026-10-15** on the spare WIN-G31UIM7G129 (§04). Data half passed;
auto-update failed; both releases back to draft (0.5.4 is the Latest). Franz's NucBox data
restored and FG Scanner uninstalled there. Repeated with 0.8.0 in P11, on the spare.

**Phase 2 — the updater rehearsal (P11, Revision C):**
1. `[Franz]` Publish v0.8.0 as a **pre-release** (Claude gives the command:
   `gh release edit v0.8.0 --draft=false --prerelease`).
2. `[Claude]` Confirm `releases/latest/download/appcast.xml` still names 0.5.4.
3. `[Claude]` On the spare: install the local 0.7.99 build of the same code; set
   `FGSCANNER_APPCAST_URL` to v0.8.0's own appcast for that account only.
4. `[Franz]` Start FG Scanner on the spare → Yes → UAC. The app closes by itself.
5. `[Claude]` Start it again: 0.8.0, data intact, log shows the install. Remove the variable.
6. `[Claude]` Confirm the Latest appcast still names 0.5.4. v0.8.0 stays a pre-release.

**Phase 3 — the first cycle:**
1. `[Franz]` On Teams with Jim: Jim's Google account (made on his Yahoo address if he has
   none — he types his own password); Drive for Desktop installed and signed in; Franz shares
   `FGScanner-Jim` with that account; Jim adds it to his Drive; `to-jim` marked Available
   offline. Confirm his account can approve UAC. Collect Box values.
2. `[Jim]` FG Scanner closed → run the 0.5.4 sender (placed by Franz) → "OK 1234".
3. `[Claude]` `station verify` on Franz's PC: 0 mismatches = Jim's backup.
4. `[Claude]` Download the 0.8.0 installer from the release, check it against `SHA256SUMS`, put
   it in `to-jim\`. `[Jim]` (Franz on "give control") FG Scanner closed → double-click the
   installer → **Yes** on UAC → start FG Scanner → read out the version and group count.
   `[Franz]` Publish 0.8.0 as the Latest (`gh release edit v0.8.0 --prerelease=false --latest`).
5. `[Claude/Franz]` Process in workspace Jim on Franz's PC; commit what is finishable; import
   into the portal; `station pack --return`.
6. `[Jim]` "Install Franz's update" → "OK 5678". `[Franz]` check counts on Teams.

**Rollback:** apply keeps the replaced database and files aside; restoring them is one command.
Before Jim's first commit the verified pack restores him exactly. **Point of no return:** a
group's commit (renames and index files) — already the case today.

## 18 · Documentation updates

- **CLAUDE.md (FG Scanner)** — stations exchange data only by `station pack/verify/apply`;
  never sync a live folder; releases published only at a cycle's return; `--data-dir` for a
  second station; no captures in another station's workspace. **And (Revision C):** 0.5.4–0.7.1
  stations are offered updates but cannot install them; the updater installs by starting the
  verified file itself, never through a generated script; a release is rehearsed as a
  pre-release before it becomes the Latest.
- **New** `docs/station-round-trip.md` (both sides, tagged steps) and an ADR for the manifest,
  plus an ADR for the updater repair (why the script went, why pre-releases).
- **`docs/release.md`** — every release is rehearsed on Jim's latest pack before publishing;
  the pre-release step and `FGSCANNER_APPCAST_URL`.
- **JimsStuff `docs/fgscanner-import-runbook.md`** — committed groups arrive from the workspace
  on Franz's PC, with `changes.jsonl`.
- **Memory** — the cycle and the transport; auto-update broken before 0.8.0 (done 2026-10-15).

## 19 · Phase plan

### Phase 1 — Prove the update path (done 2026-10-15)
The 0.7.1 rehearsal; recorded. It found the updater broken (§04). **Not here:** anything on
Jim's laptop.

### Phase 2 — Build 0.8.0
Workspace switch; station verbs; first-cycle sender; shortcuts; CI ladder; the updater repair;
reviews; release drafted, rehearsed 0.5.4 → 0.8.0 on Jim's 9/25 data (manual install) and
0.7.99 → 0.8.0 through the repaired updater (pre-release). **Not here:** Jim's data; v0.8.0 as
the Latest.

### Phase 3 — The first cycle
Google Drive set up with Jim, pack, verify, update, process, import, return, apply.

## 20 · Prompt pack

[SPEC-SCANNER-2026-010-jim-remote-update-and-data-PROMPTS.md](./SPEC-SCANNER-2026-010-jim-remote-update-and-data-PROMPTS.md)
— 13 prompts: P1 finish rehearsal (done) · P2 `StationPaths`/`--data-dir` (done) · P3 manifest +
verify (done) · P4 pack (delta, renames, freeze record) · P5 apply (gates, settings kept,
atomic, rollback) · P6 change log + needs-Jim sheet · P7 first-cycle sender (PowerShell) +
shortcuts · P8 CI migration ladder · **P8b the updater repair (AC-11; versioned appcast link;
the override)** · P9 code review · P10 security review · P11 release 0.8.0 draft + both
rehearsals on the spare · P12 the first cycle with Jim (manual install from `to-jim\`).
Thirteen prompts is the "too big" line; P1–P3 are already done, so ten remain — roughly three
days.

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
| **Review round answered** | ☑ Round A 2026-10-09 (revise) — https://claude.ai/artifact/LTKoCtnKXZZTsoEZQVNBpV · ☑ Round B 2026-10-09 (approve) — https://claude.ai/artifact/EjYRfaTz1UDJ1zQHuQzEPi (doc SPEC-SCANNER-2026-010-rB) · ☑ Round C 2026-10-16 (approve) — https://claude.ai/artifact/GwvbCJH8cwXBG9KcVAb5yt (doc SPEC-SCANNER-2026-010-rC) |
| **Franz approved** | ☑ date: 2026-10-09 (Round B) · 2026-10-16 (Revision C, Round C) |
| **Built** | ☐ date: |
| **Verified in production** | ☐ date: |
