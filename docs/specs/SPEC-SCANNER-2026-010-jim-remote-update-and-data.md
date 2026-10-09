# SPEC-SCANNER-2026-010 — Jim's remote update and his data

| | |
|---|---|
| **Status** | Draft |
| **Revision** | A |
| **Tier** | System — it changes the software on Jim's live evidence station and moves evidence between three machines |
| **Author** | Claude, for Franz Gerster |
| **Date** | 2026-10-09 |
| **Project** | FG Scanner (station side) with JimsStuff (receiving side) |
| **Parent** | JimsStuff SPEC-2026-007 P12 step 7 (the FG Scanner release); JimsStuff SPEC-2026-009 P10 (Jim's real import) |
| **Supersedes** | — |

---

## 01 · Management summary

- **What we are building** — a safe way to bring Jim's FG Scanner from 0.5.4 to 0.7.1 from
  Florida, and to get his scanned evidence onto Franz's machines afterwards. He is in Ohio,
  so nothing can go by USB any more: the update comes through the app's own auto-update, and
  the evidence travels over the internet.
- **Why** — Jim has scanned and partly indexed many pages since our last copy (9/25: 20
  groups, 1,368 pages, 1.6 GB). None of it has reached the case file: every group is still
  open. 0.7.1 adds the rules that make his groups importable (document numbers proposed from
  the titles, sticky-note sheets checked, pages read before commit) — and those rules will
  refuse most of his groups until they are fixed (233 sticky-note problems in 16 of 20 groups
  at 9/25).
- **What it costs** — 7 prompts across 3 phases, roughly **two to three days** of work, most
  of it scripts and runbooks rather than app code, plus two sessions with Jim on the phone
  (update day, and the first upload). No new app release is needed for the update itself.
  Google Drive storage for one copy of his data (a few GB).
- **What could go wrong** — (1) the auto-update fails half-way and Jim's app does not start —
  he cannot be reached by USB; mitigated by rehearsing the exact update here first (§17) and a
  full backup before he presses Yes. (2) A page Jim turned by hand gets turned again when 0.7.1
  first reads it (§16 R2). (3) Evidence copied badly in transit — every file is checked by its
  fingerprint on arrival.
- **What we need from you** — where the heavy work happens (Jim's laptop or Franz's
  machines), how the files travel, and whether the update waits for the backup.

## 02 · Outcomes

- **Goal** — Jim runs 0.7.1 with every page and every answer he had before; a verified copy of
  his station exists off his laptop; his groups can be committed and imported into the case
  file; nothing he scanned is lost or altered on the way.
- **Who benefits** — Jim (a working, current station; less manual work); Franz (the evidence in
  the case file, indexing on fast machines); counsel later (a provable chain of custody).
- **How we will know it worked**
  1. Before the update: a backup of Jim's station is on Franz's side and **every file's SHA-256
     matches** the manifest made on Jim's laptop (count = 0 mismatches).
  2. After the update: 0.7.1 starts on Jim's laptop, shows the same number of groups and pages
     as before, and the pre-update database copy `fgscanner.db.bak-0.7.1` exists.
  3. Every committed group that arrives passes the portal's `import_fgscanner.py --dry-run`
     (the end-to-end fingerprint check) before the real import.
  4. No page image changes its bytes during the update itself (checked by re-hashing after the
     first start, before any reading runs).

## 03 · Scope and non-goals

**In scope**

1. **Rehearsal on the NucBox** — Jim's 9/25 data, 0.5.4 installed, real auto-update to 0.7.1,
   checks (§17; started 2026-10-09).
2. **`build/backup-station.ps1`** — a Windows PowerShell 5.1 script (runs on any Windows
   without installing anything) that copies a closed station's data folder and evidence folder
   into a dated folder and writes `MANIFEST-SHA256.txt`; and **`build/verify-station.ps1`**,
   which re-hashes a copy against its manifest. Promotes the 9/23 USB-kit script into the repo.
3. **The transfer channel** (Q2) — from Jim's laptop to Franz's NucBox and back.
4. **The update-day runbook** — `docs/jim-remote-update-runbook.md`: numbered, tagged
   `[Franz]` / `[Jim]` / `[Claude]`, done with Jim on the phone and on screen sharing.
5. **After the update** — getting his groups committable: read the pages, fill Box, Propose
   documents, fix the sticky-note sheets, restore or rescan Dental's missing page; where that
   work happens is Q1.
6. **Hand-rotated pages** (Q4) — protect them from a second automatic turn.

**Non-goals**

- **No app feature for moving a station** (export/relocate). The database holds absolute
  paths; a feature would be a large change to the evidence data model. Scripts + identical
  paths instead.
- **No upload page on the portal.** Cloudflare caps one request at 100 MB and the portal would
  need a resumable chunked uploader built from scratch (recon 2026-10-09). Not now.
- **No change to the evidence contract** (`index.json`, `manifest.json`, field names).
- **No live two-way sync of Jim's evidence folder.** A sync tool must only ever carry a copy.
- **Jim's import itself** (JimsStuff SPEC-2026-009 P10) — this spec only gets the groups there.

## 04 · Current state

Read 2026-10-09: FgmakerScanner `main` 440889b (v0.7.1), tag v0.5.4; JimsStuff `master`
271ce76; the 9/25 copy of Jim's data (`%APPDATA%\FGScanner.rehearsal-jim-laptop-2026-09-25`,
images `D:\Evidence-Scans`); two read-only research passes (scripts in the job folder).

**Jim's station (as of 9/25 — newer state unknown):** FG Scanner 0.5.4, installed by USB
2026-09-25. Data at `%APPDATA%\FGScanner` (`fgscanner.db`, journal mode `delete` — not WAL —
plus `trash\`, `recovery\`, `index-drafts\`, `tessdata\`), evidence at `C:\Evidence-Scans`
(the 9/24 kit rewrote `Groups.DirectoryPath` / `Profiles.BaseDirectory` from `D:`). 20
groups, all `State=0` (open, none committed), profile "Evidence" schema v2, `OcrEnabled=0`,
every page unread (`OcrStatus=0`). DocNo blank on 886 of 1,368 rows; `Box` (required, batch)
empty on 18 groups; NoteState as-found 93, note-face 138, clean 857. Dental has a row for
`scan_00001.jpg` with no file (missing since before 9/20). Sizes: DB 1.5 MB; images 1.15 MB
per page average.

**The update path** (`src/FgScanner.App/Services/UpdateService.cs`): on every app start,
NetSparkle reads `releases/latest/download/appcast.xml`, Ed25519 strict, asks "Download and
install now?"; Yes runs the Inno installer `/VERYSILENT /NORESTART`. `PrivilegesRequired=admin`
(UAC, or an admin password on a standard account), `CloseApplications=yes`,
`RelaunchAfterUpdate=false` — Jim restarts the app himself. The installer never touches
`%APPDATA%`. **Never exercised end to end** — no station has been upgraded by it.
Verified 2026-10-09: v0.7.1's appcast and installer signatures are valid for the key in
0.5.4; size and SHA-256 match.

**First start of 0.7.1 over 0.5.4's database** (`DbBootstrapper.cs:22-36`): one pending
migration, `20261005135716_AddPageOrientationSettled` (a bool column, default false; marks
already-read pages settled — none on Jim's). Before migrating it copies the database to
`fgscanner.db.bak-0.7.1`. Nothing writes into group folders on first start unless reading
jobs are pending (Jim's queue was empty).

**What 0.7.1 then asks of each open group** — commit refuses: required fields blank (DocNo,
Box — as in 0.5.4); a malformed sticky-note sequence (`ImportRefusals`, new; 233 refusals in
16 groups at 9/25); a page whose image is missing (new, Revision F); unread pages are counted
and "No" queues them for reading. "Build the Evidence profile" (Settings) turns reading on
capture on. Reading measured ~1.1 s per page here; estimated 3–6 s on Jim's i5 (unmeasured),
one page at a time.

**Auto-orient** (`OcrPipeline.cs:38,52-55`): reading can turn a page (confidence ≥ 5.0),
rewriting the JPG; the original goes to `originals\` (PreserveOriginals is on). Pages Jim
turned by hand in 0.5.4 are not marked settled, so their first read can turn them again.

**Transfer** (recon): no existing channel carries a group folder or station data except USB.
`/intake` takes single images ≤ 32 MB; the index-package loop moves zips down and a ≤ 2 MB
results file up. The importer (`JimsStuff pipeline/fgscanner_group.verify`) already checks a
received committed group end to end (presence, whole JPEG, SHA-256 against `index.json`, no
stray images, originals, note pairs). Lines: Jim ~300↓/20↑ Mbps, Franz ~500↓/20↑.

| Size | Jim → (20 Mbps) | → Jim (300 Mbps) |
|---|---|---|
| 1.6 GB | ~15 min | ~1 min |
| 4.8 GB | ~45 min | ~3 min |

**Not examined:** Jim's current data (size, new groups, any commits, hand-rotated pages);
whether his Windows account is an administrator; his Google account; real reading speed on
his laptop.

## 05 · Questions for Franz

**Blocking**

1. **Q1 — Where does the after-update work happen?** (Reading ~2,000+ pages; Box; Propose
   documents; fixing the sticky-note sheets; Dental.)
   (a) **On Jim's laptop**, Jim and Franz together on screen sharing: reading runs overnight;
   Franz drives Propose and the fixes remotely. Nothing moves but backups and committed groups.
   (b) **Round trip**: Jim stops scanning, uploads his whole station, Franz does everything on
   the NucBox (same `C:\Evidence-Scans` paths), uploads it back, Jim restores it. Fast work,
   but Jim is frozen for the whole round trip and two copies of live evidence exist.
   (c) **Split**: reading runs on Jim's laptop overnight (a)); Franz does the judgement work
   (Propose, notes, DocNo) on the NucBox on the backup copy to *find* every problem and
   prepare the fixes, then applies them with Jim on screen sharing.
   *Recommended:* **(a)** — evidence never leaves the machine that captured it until it is
   committed; the slow part (reading) needs no one present; the backup still gives Franz a
   full copy to study. *Cost:* (b) adds two transfers (~1 h each way at today's size), a
   path-equal restore on Jim's side and a freeze; (c) adds the copy work.
2. **Q2 — How do the files travel?** (a) **A Google Drive folder** shared from Franz's Drive to
   a Google account for Jim (Drive for Desktop resumes; the project already keeps backups
   there); (b) **Tailscale** to the NucBox, copy with Explorer/robocopy (no third party holds
   the data; one install and sign-in for Jim); (c) **OneDrive "Request files" link** (Jim needs
   no account; browser upload, less robust for GBs). *Recommended:* (a), with the evidence
   zipped and encrypted (7-Zip AES) only if counsel asks.
3. **Q3 — Does the update wait for the backup?** (a) **Yes**: the backup is copied and verified
   on Franz's side *before* Jim presses Yes; until then the 0.7.1 release stays a draft (so his
   app offers nothing). (b) Jim updates first (the app copies its own database), backs up
   after. *Recommended:* (a) — the app's own copy is the database only, not the 1.6+ GB of
   images.
4. **Q4 — Pages Jim turned by hand.** (a) Before the first reading, mark every existing page
   "orientation settled" (a one-time step in the backup script's companion, on Jim's database,
   after the update) so 0.7.1 never turns them; (b) turn auto-orient off on Jim's station for
   the backlog, back on for new scans; (c) leave it — originals are kept anyway.
   *Recommended:* (b) — one Settings switch, no database edit, reversible; new scans still
   straighten themselves.

**Non-blocking (proceeding as stated unless corrected)**

5. **Q5 — Screen sharing:** Windows **Quick Assist** (built in; Jim reads Franz a code).
6. **Q6 — After the rehearsal tonight:** put v0.7.1 back to **draft** until update day, so Jim
   is never offered it early (he will not open the app tonight; this covers tomorrow).
7. **Q7 — Box values:** Franz asks Jim which box each group came from on update day; the
   script does not guess.

## 06 · Assumptions

| # | Assumption | If wrong |
|---|---|---|
| A1 | Jim's data still lives at `%APPDATA%\FGScanner` and `C:\Evidence-Scans`, as the 9/25 move left it. | The backup script takes both paths as parameters and lists what it found before copying. |
| A2 | Jim has committed nothing since 9/25. | Committed groups are the easy case: they upload as-is and pass the importer's check. |
| A3 | Jim's data is under ~5 GB. | The upload takes longer (~45 min per 5 GB); overnight is fine. |
| A4 | Jim's Windows account can approve UAC. | He needs the admin password on update day; Franz finds out first. |

## 07 · Data model

No change to FG Scanner's database or the evidence contract. If Q4 = (a), a one-time
`UPDATE Pages SET OrientationSettled = 1` on Jim's migrated database — recorded in the run
log, with the backup taken first.

## 08 · Architecture and approach

```
Jim's laptop (0.5.4)                     Franz's NucBox                  Tower (portal)
  close app                                                                
  backup-station.ps1 ──(Q2 channel)──▶   verify-station.ps1 (SHA-256)     
  [draft → published]                                                     
  start app → Yes → UAC → 0.7.1                                           
  reading overnight (Q1)                                                  
  Propose / notes / Box / commit                                          
  committed group copy ──(Q2)──────────▶ import --dry-run ──▶ import ──▶ register (push)
```

- **Rehearse exactly what Jim will do**, on Jim's own 9/25 data, on the NucBox, before Jim.
- **Scripts, not app features** — they run on 0.5.4 today and on any Windows.
- **Evidence moves only as copies**, always with a manifest, always re-hashed on arrival.
- **Alternatives rejected**: portal upload (100 MB edge cap, new chunked uploader); syncing
  Jim's live folder (a deletion or edit could travel back); an in-app "move station" feature
  (absolute paths throughout the evidence model).

## 08a · AI opportunity assessment

| | Sticky-note sheets and document boundaries |
|---|---|
| **What it would do** | Read the 233 refused note sequences and suggest the fix per sheet. |
| **Why AI beats code here** | It would not: the rule is mechanical (`ImportRefusals`), and Propose documents already fills DocNo from titles. The work is checking the paper, which only Jim can do. |
| **Recommendation** | **None.** Poor fit; say so rather than add spend. |

## 09 · Design system compliance

No UI. Runbook in the repo's plain style.

## 10 · Acceptance criteria

> **AC-1** — The rehearsal: 0.5.4 on Jim's 9/25 data is offered 0.7.1, installs it through the
> real prompt and UAC, starts 0.7.1, and shows 20 groups / 1,368 pages with
> `fgscanner.db.bak-0.7.1` present; every image hash unchanged by the first start.
> *Proven by:* manual, Claude + Franz on the NucBox, §22.

> **AC-2** — `backup-station.ps1` copies a station and writes a manifest; `verify-station.ps1`
> reports 0 mismatches on an exact copy and names a changed, missing or extra file.
> *Proven by:* `build/tests/backup-station.tests.ps1` (Pester-free PowerShell tests, as
> JimsStuff's `backup-pi.tests.ps1`).

> **AC-3** — The backup refuses to run while FG Scanner is open (a database copied mid-write is
> not a backup). *Proven by:* the same tests (a held lock on the db).

> **AC-4** — Jim's backup arrives on Franz's side and verifies with 0 mismatches before the
> update. *Proven by:* manual, update day.

> **AC-5** — After Jim's update: 0.7.1 starts; group and page counts equal the backup's.
> *Proven by:* manual, update day.

> **AC-6** — Every committed group received passes `import_fgscanner.py --dry-run`.
> *Proven by:* manual, per upload.

## 11 · Test strategy

| Feature | Test file | The failing assertion |
|---|---|---|
| Backup | `build/tests/backup-station.tests.ps1` | backing up a fixture station yields a manifest whose line count equals the file count |
| Verify | same | a byte flipped in one copied JPG → exactly that file reported |
| Refuse while open | same | an open handle on `fgscanner.db` → refusal, nothing copied |

Test data: a small fixture station built by the test (a few JPGs, a tiny SQLite file). Real
data only in the rehearsal and on update day.

## 12 · Edge cases and failure modes

| Case | Expected behaviour | Where handled |
|---|---|---|
| Jim clicks No on the update | Nothing happens; it asks again next start | UpdateService |
| Update download fails mid-way | Nothing installed; retried next start | NetSparkle |
| Installer aborts mid-copy | App may not start; data untouched (installer has no `%APPDATA%` entries) → re-run the installer from the release page with Franz on screen sharing | runbook |
| UAC needs a password Jim lacks | Stop; nothing changed | runbook (A4 checked first) |
| Upload interrupted | Drive resumes; verify catches anything partial | channel + verify |
| A file differs on arrival | Re-send that group; never "fix" a copy | verify |
| Jim scans between backup and update | The backup is of the last closed state; the next upload carries the rest | runbook |

## 13 · Security and configuration

Privileged legal evidence leaves Jim's laptop. The channel (Q2) is shared with Franz's account
only; nothing is public; no link without sign-in. No credential or password is written into a
script, the repo or chat. Quick Assist sessions are started by Jim each time.

## 14 · Observability

Every backup writes `MANIFEST-SHA256.txt` and a one-line summary (files, bytes, time). Every
verify writes the mismatch list. The update-day run log records each step with times.

## 15 · Performance and scale

Today ~1.6 GB / ~1,400 files; hashing on Jim's laptop ~1–3 min; upload ~15 min per 1.6 GB.
Reading backlog ~1.5–2.5 h per 1,400 pages on the i5 (estimate) — overnight.

## 16 · Regression risk

| # | What could break | Why | Evidence | Mitigation |
|---|---|---|---|---|
| R1 | Jim's app does not start after the update | auto-update never used end to end; installer replaces DLLs | §04 | Rehearsal (AC-1) on his own data; installer re-run path; backup first |
| R2 | A hand-turned page is turned again | 0.5.4 pages not marked settled | `OcrPipeline.cs:38,52-55` | Q4 |
| R3 | Jim offered 0.7.1 before the backup | published release is offered at every start | `UpdateService.cs` | Q3/Q6: draft until update day |
| R4 | Evidence altered in transit | network copy | — | SHA-256 manifest; importer verify |
| R5 | A sync tool writes into Jim's live folder | two-way sync | recon | copies only, never the live folder |
| R6 | Pre-migration backup overwritten | `bak-0.7.1` uses overwrite | `DbBootstrapper.cs:22-36` | our own full backup first |

## 17 · Migration and rollback

**Rehearsal (NucBox, 2026-10-09):** Franz's station data set aside as
`%APPDATA%\FGScanner.station-aside-2026-10-09`; Jim's 9/25 copy in `%APPDATA%\FGScanner`,
images copied to `C:\Evidence-Scans`; 0.5.4 installed; 0.5.4 started on it, no update offered
while 0.5.4 is Latest. Then: Franz publishes v0.7.1 (Latest) and v0.6.0 (not latest); 0.5.4
started → Yes → UAC → 0.7.1; AC-1 checks; Franz's data restored.

**Update day (Jim):**
1. `[Franz]` Confirm Jim's account can approve UAC; arrange the time; Quick Assist.
2. `[Jim]` Close FG Scanner. `[Franz via Quick Assist]` Run `backup-station.ps1`.
3. `[Jim]` Upload the backup (Q2). `[Claude]` `verify-station.ps1` on arrival — 0 mismatches.
4. `[Franz]` Publish v0.7.1 (`gh release edit v0.7.1 --draft=false --latest`).
5. `[Jim]` Start FG Scanner → Yes → UAC Yes → wait → start it again.
6. `[Franz]` Check: version 0.7.1, group/page counts, `fgscanner.db.bak-0.7.1` present.
7. `[Jim + Franz]` Q4 setting; tick Index; set Indexer name; Build the Evidence profile.
8. `[Jim]` Start "Read all unread pages" and leave it overnight (Q1 = a).

**Rollback:** if 0.7.1 will not start — re-run the 0.7.1 installer from the release page; if
that fails, install 0.5.4 from its release page and restore `fgscanner.db` from
`fgscanner.db.bak-0.7.1` (0.5.4 ignores the one extra column, inferred — confirmed in the
rehearsal). The evidence folder is never touched by either installer.
**Point of no return:** Jim's first commit under 0.7.1 (it writes `index.json` and renames
files). Before that, the backup restores him exactly.

## 18 · Documentation updates

- **New `docs/jim-remote-update-runbook.md`** (update day, 2am-clear, tagged steps).
- **CLAUDE.md** (FG Scanner) — one line under Evidence work: stations update only by
  auto-update now; backup + verify before; never sync a live evidence folder.
- **`docs/release.md`** — auto-update rehearsal result; publish 0.6.0 as not-latest.
- **JimsStuff `docs/fgscanner-import-runbook.md`** — committed groups arrive by the Q2
  channel, not the transfer drive; run the dry run first.
- **Memory** — the rehearsal result and the channel.

## 19 · Phase plan

### Phase 1 — Prove the update (tonight)
- **Objective** — the auto-update works on Jim's own data. **Delivers** — AC-1, rehearsal log.
- **Not in this phase** — anything on Jim's laptop.

### Phase 2 — Tools and runbook
- **Objective** — backup, verify, channel, runbook ready. **Delivers** — scripts + tests (AC-2,
  AC-3), channel set up and tested with a dummy folder, runbook, code/security review.
- **Not in this phase** — Jim's data.

### Phase 3 — Update day and first upload
- **Objective** — Jim on 0.7.1 with a verified backup; first committed groups imported.
- **Delivers** — AC-4..AC-6.

## 20 · Prompt pack

Written after approval, in
[SPEC-SCANNER-2026-010-jim-remote-update-and-data-PROMPTS.md](./SPEC-SCANNER-2026-010-jim-remote-update-and-data-PROMPTS.md).
Planned: P1 rehearsal (in progress) · P2 backup/verify scripts · P3 channel · P4 runbook ·
P5 code review · P6 update day · P7 first upload and import.

## 21 · Definition of done

- [ ] All acceptance criteria met
- [ ] Failing tests written first, now passing
- [ ] Full suite green
- [ ] `/code-review max` run, findings resolved or accepted in writing
- [ ] Security review passed (scripts that move privileged evidence)
- [ ] Documentation updated per §18
- [ ] Jim on 0.7.1, verified
- [ ] Rollback tested (the rehearsal's 0.5.4 restore) or explicitly waived by Franz

## 22 · Sign-off

| | |
|---|---|
| **Review round answered** | ☐ Round A |
| **Franz approved** | ☐ date: |
| **Built** | ☐ date: |
| **Verified in production** | ☐ date: |
