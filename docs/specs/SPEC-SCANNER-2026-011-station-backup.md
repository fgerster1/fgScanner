# SPEC-SCANNER-2026-011 — Back up a station to a USB stick, and send Franz a test copy

| | |
|---|---|
| **Status** | Approved |
| **Revision** | C — 2026-10-17: the Prompt 7/8 reviews' 13 findings (no open items; restore keeps Settings minus commands, Franz) · B — 2026-10-17: Round A approved, every recommendation taken |
| **Tier** | Feature |
| **Author** | Claude, for Franz Gerster |
| **Date** | 2026-10-17 |
| **Project** | FgmakerScanner |
| **Supersedes** | — |
| **Builds on** | SPEC-SCANNER-2026-010 (station packs, ADR-0017; the updater, ADR-0018) |

---

## 01 · Management summary

- **What we are building** — a **Back up** button in FG Scanner that copies everything on the
  station (the database and every scanned image) to a USB stick plugged into the laptop, and
  checks the copy. The same button can also put a copy in the shared Google Drive folder, so
  Franz can open Jim's current work on his own PC to test with.
- **Why** — Jim's scans are legal evidence that took weeks of work; today the only copies leave
  the laptop when Franz runs a cycle. An update, a failed disk or a lost laptop could cost him
  everything since the last cycle. With this, Jim keeps his own checked copy at home, and every
  update can start from a fresh one.
- **What it costs** — 9 prompts across 3 phases: two to four days of build, plus a rehearsal on
  the spare PC. No recurring cost; a USB stick of 8 GB or more (about $10).
- **What could go wrong** — (1) a backup that *looks* fine but is not: every backup is re-read
  and checked against its own receipt before it counts, and a half-written one is never counted.
  (2) A backup that interferes with the Franz–Jim round trip: backups are a separate kind that
  the round trip ignores, proven by a test.
- **What we need from you** — answers to the questions in §05 (one review page), and Jim's
  Windows edition if you want the stick encrypted (Q7).

**Already done for the first update (P12), without this spec:** on the Teams call, the existing
0.5.4 sender script writes a full checked copy to Jim's stick before the Drive send
(`docs/station-round-trip.md`, Cycle 1 step 0; rehearsed 2026-10-17). This spec is the in-app
button for every cycle after that.

## 02 · Outcomes

- **Goal** — Jim never loses work: at any moment there is a checked copy of his station, no
  older than his last session, that does not depend on his laptop, on Drive, or on Franz's PC.
- **Who benefits** — Jim (his work is safe; restoring is a call to Franz, not a re-scan);
  Franz (an update or a test can always be undone from a known copy, and he can test against
  Jim's real current data without waiting for a cycle).
- **How we will know it worked**
  - Jim presses Back up, and within a few minutes reads `OK xxxx-xxxx` off the screen; Franz's
    `fgscanner station verify` on that folder prints the same code.
  - On the spare, the stick's latest backup restored into an empty station reproduces the
    station exactly (same group/document/page counts, same file hashes) — AC-6.
  - Every update offer says when the last backup was made and offers one first.
  - A Drive test copy opens on Franz's PC beside Jim's round-trip workspace without touching it.

## 03 · Scope and non-goals

**In scope**

1. A third pack kind, `backup`: always the whole station (never a delta), checked after writing,
   and **never** recorded as the round trip's baseline (no freeze record is written or changed).
2. Retention on the stick: keep the newest N verified backups; delete the oldest only after the
   new one has verified (Q1).
3. CLI: `fgscanner station backup --to <folder> [--keep N]`, for scripts and for Franz.
4. App: a **Back up** button (Q2) opening a small window: pick the stick (removable drives listed,
   any folder via Browse, last choice remembered), progress, then `OK xxxx-xxxx` or a plain
   NOT OK reason. A "Last backup: <when>" line.
5. App: the update offer names the last backup and offers **Back up first** (Q3, Q4).
6. **Send a test copy to Franz**: the same backup written to `<Drive folder>\to-franz\copies`.
7. Franz's side: open a test copy into its own workspace with its evidence moved to a folder
   Franz names, marked as a test copy, which can never pack a send or a return (Q5).
8. Restore: Jim's station rebuilt from a backup = the existing `station apply` into an empty
   station, documented as a 2am procedure (§17).

**Non-goals**

- No automatic or scheduled backups in the background — every backup is a button press (or a
  prompt before an update). A silent background copy that fails silently is worse than none.
- No incremental (delta) backups: each backup restores on its own, with nothing else needed.
- No cloud backup service; Drive is used only for the test copy Franz asked for.
- No restore button in the app. Restore is rare and dangerous (it replaces a station), so it
  stays a Franz-guided `station apply` on Teams.
- No backup of Franz's own station or of the round-trip workspace from the button (the CLI can).
- No change to the evidence contract, to `capturedBy`, or to the round trip's send/return.
- No encryption unless Q7 says otherwise.

## 04 · Current state

- **Stack and conventions** — `docs/specs/_project-brief.md` (refreshed for this spec's area
  only). WPF + CommunityToolkit MVVM; views and view models together in `src/FgScanner.App/Views`;
  WPF-free logic in Core; atomic writes (temp + replace); UI strings inline English.
- **Station packs (SPEC-SCANNER-2026-010)** — `src/FgScanner.Core/Station/`:
  - `StationPacker.Pack` (`StationPacker.cs:55`) refuses a station not at rest (`StationRest`),
    copies the DB through a caller-supplied `copyDatabase` (the CLI uses SQLite's backup API,
    `CliRunner.cs:900`), copies files, writes `station-manifest.json` last, **re-verifies the
    whole pack** (`StationManifestVerifier.Verify`), deletes the folder on any failure, and then
    **writes the freeze record** (`StationPacker.cs:138`) — the baseline the next return is
    checked against.
  - Pack kinds today: `send`, `return` (`CliRunner.cs:774`). The kind only names the folder
    (`<station>-<yyyy-MM-dd-HHmm>-<kind>`, `StationPacker.cs:209`); the manifest has no kind field.
  - `StationRest.Problem` refuses when FG Scanner is open (its single-instance mutex
    `FGScanner.SingleInstance`, `App.xaml.cs:43`), when a `-journal`/non-empty `-wal` exists, or
    when `recovery\` holds unsaved scans.
  - `PendingReadingJobs > 0` refuses a pack ("let reading finish").
  - `StationApplier.GateProblem` (`StationApplier.cs:361`): into an **empty** station (no DB) a
    full pack is accepted if the evidence folder and trash are empty — this is the restore path.
    Into a non-empty station a pack must be `--based-on` this station's last send, so **a backup
    applied to a live station is already refused**. The evidence root must equal the manifest's
    `evidenceRoot` exactly (`StationApplier.cs:363`).
  - The 8-character code = first 8 hex of the manifest's SHA-256.
- **Stored paths** — `Group.DirectoryPath` (`Entities.cs:187`), `GroupDirectoryPath` (`:321`) and
  `TrashFolderPath` (`:331`) on trash rows, `Profile.BaseDirectory` (`:65`): absolute. Apply
  already rebinds `TrashFolderPath`. Page files are named relative to their group folder.
- **The app while running** — `ScanViewModel.CaptureInHand` (`ScanViewModel.cs:601`) = scanning,
  annotated sheet or duplex pass in hand. Background readers: `Services/OcrWorker.cs`,
  `Services/AiWorker.cs`; **neither can be paused today**. Settings are key/value rows
  (`AppSettingsService`).
- **The update offer** — `UpdateService.cs:72`, a Yes/No MessageBox, then `UpdateInstaller`.
- **Jim's station** — Windows laptop, C: only; evidence in `C:\Evidence-Scans`, station in
  `%APPDATA%\FGScanner`. 9/25 snapshot: 20 groups, 1,368 pages, 1,413 files, 1.54 GB; a full
  pack to a local folder takes ~22 s on the spare.
- **Franz's PC** — the round-trip workspace for Jim (SPEC-010) also uses `C:\Evidence-Scans`,
  because a pack's evidence root is fixed. **So a test copy cannot use that folder** — the reason
  for item 7 of §03.
- **Not examined** — Inno Setup changes (none expected); the JimsStuff import (untouched);
  Jim's Windows edition (Q7); the actual stick Jim will use (speed, FAT32/exFAT).

## 05 · Questions for Franz

**Answered 2026-10-17 in Round A** (https://claude.ai/artifact/Qdunwnh6FxJBVM984KjiNj): every item (a) — Q1 newest 3 · Q2 left column · Q3 Last backup + update offer · Q4 offered, skippable · Q5 own workspace, relocated, TEST COPY · Q6 capture allowed in a test copy · Q7 no encryption; my calls C1–C5 agreed. Already answered on 2026-10-17 in the terminal:
- **Timing** — P12 goes ahead now with the USB copy made by the existing script; this button
  ships in the first release after P12 (with the About button).
- **Test copy** — the same backup, delivered through the shared Google Drive folder.

**Blocking**

1. **Q1 How many backups stay on the stick?** (a) the newest 3, older ones deleted after a new
   one verifies *(recommended)*; (b) only the newest; (c) all until the stick is full. — (a)
   costs ~3× the space (≈5 GB today, so a 16 GB stick); (b) means a backup taken *after* a
   mistake replaces the only good one; (c) eventually fails mid-backup on a full stick.
2. **Q2 Where is the button?** (a) bottom of the left column, beside About, always visible
   *(recommended)*; (b) on the Settings page. — (a) is one click from anywhere and shows the
   "Last backup" line all the time; (b) hides it where Jim rarely goes.
3. **Q3 What reminds Jim?** (a) the "Last backup" line, plus the update offer naming the last
   backup with a Back up first button *(recommended)*; (b) (a) plus a reminder when closing the
   app if the last backup is over 7 days old; (c) nothing but the button.
4. **Q4 Must an update wait for a backup?** (a) offered, Jim can skip *(recommended)*; (b) the
   update refuses until a backup from today exists. — (b) is safest but blocks an update whenever
   the stick is missing, and Jim cannot get past it without Franz.
5. **Q5 Where does a test copy open on Franz's PC?** (a) its own workspace with the evidence
   moved to a folder Franz types (e.g. `C:\Evidence-Scans-test`); the paths in the copy's database
   are rewritten to match, and it is marked TEST COPY *(recommended)*; (b) only on another PC
   (the NucBox), where `C:\Evidence-Scans` is free, with no path rewriting.
6. **Q6 May Franz scan into a test copy?** (a) yes — it can never send or return, so nothing
   captured there reaches Jim or the portal; the title bar says TEST COPY *(recommended)*;
   (b) no, refused like the round-trip workspace.
7. **Q7 Encrypt the stick?** (a) no — Jim keeps the stick with the boxes *(recommended for now)*;
   (b) BitLocker To Go on the stick, set up by Franz on Teams — needs Windows Pro on Jim's laptop
   to turn on (Home can only unlock); (c) decide later. — The stick holds legal evidence in the
   clear; losing it exposes it.

**Non-blocking** — proceeding as stated unless corrected.

1. While a backup runs the app is blocked by its progress window (no scanning) — *proceeding as
   if:* yes; a backup of a station changing under it would not verify.
2. Background reading pauses for the backup instead of refusing it — *proceeding as if:* the
   backup waits for the current page to finish, holds new jobs, and releases them after.
3. A test copy brings Jim's Settings with it — *proceeding as if:* no; an empty station takes no
   Settings (SPEC-010 rule), so the test copy uses Franz's defaults.

**Revision C — the Prompt 7/8 reviews** (2026-10-17)

`/code-review` (cold subagent) and a security review (cold subagent) over `origin/main..a2cdd82`,
plus the automated commit review on P5. Every finding was checked at its line. All hold. One went
to Franz (restore and Settings, answered in the terminal 2026-10-17: **keep the backup's Settings
except any that runs a command**); every other one is a bug against this spec or SPEC-010.

| # | Finding (file:line) | Kind | Fix | Prompt |
|---|---|---|---|---|
| CR-1 | `AiWorker.cs:106` a failed claim leaks a gate pass; the next backup hangs, readers stop | bug against §08 (WorkGate) | one pass per drain cycle, disposed on every path; hold with a timeout → NOT OK | 7a |
| CR-2 | `BackupService.cs:117`, `BackupViewModel.cs` RunAsync: non-IO exceptions (UNC `DriveInfo`, SqliteException, gate held) crash the app | bug against §14 / AC-4 | catch all in the service, log, NOT OK; refuse UNC/network targets | 7a |
| CR-3 | restore = apply into empty station drops all Settings (PreserveOriginals OFF, Yahoo, …) | **spec wrong** (§03 item 8) — Franz: (a) | `station apply --restore`: a `*-backup` pack into an empty station keeps its Settings except `Hook.*` | 7c |
| CR-4 / SR-2 / auto | `TestCopyOpener.cs:105-125` marker written last; failure leaves an unmarked station; placed files not cleaned | bug against §13, Q6 | marker first; on failure remove the database and every placed file; post-place hash check | 7b |
| CR-5 | group import/commit/export in flight is not blocked; a backup can name files it lacks | bug against §16 #2 | long group operations enter the WorkGate; the backup waits for them | 7a |
| CR-6 | `OcrWorker.cs:65`, `AiWorker.cs`: a hold between jobs leaves a committed group's `index.json` stale | bug against §16 #3 | a worker that sees a hold requested stops claiming, re-exports inside its pass, then yields | 7a |
| CR-7 | `Backup.Keep` never read; stranded folders deleted regardless of age; damaged backups never reported; evidence drive offered | bug against §07, AC-3, §08 | read `Backup.Keep`; delete only stranded folders older than the new backup; result names damaged backups; leave out the drives holding the station and evidence | 7a |
| SR-1 / auto | `TestCopyOpener.cs:75-112` `--evidence` equal to / around the workspace lets a pack plant `fgscanner.db(-wal)` | bug against §13 | refuse overlap with the workspace (both ways); refuse WAL/SHM present; a placed file may never land on a station file name | 7b |
| SR-3 | `TestCopyOpener.cs:80` trusts `manifest.EvidenceRoot` as text; a test copy can land inside the round-trip workspace's evidence | bug against Q6 | also refuse targets inside/around any evidence folder named by this PC's stations (`%APPDATA%\FGScanner*`) | 7b |
| SR-4 | `StationPacker.cs:173` a backup from a test copy carries no marker; restored, it can send | bug against Q6 | a test copy packs nothing, backups included | 7b |
| SR-5 | `CliRunner.cs` `--test-copy` ignores `--expect-code` | bug against SPEC-010 D3 | same refusal as apply | 7b |
| SR-6 | `StationPacker.IsInside` text-only; Browse accepts UNC, Drive, junctions | bug against §12 | resolve link targets before the inside check; Browse refuses network and Google Drive | 7a |
| SR-7 | `Pages.FileName`, `TrashItems.FilesJson`/`PayloadJson` from an untrusted database never checked (pre-existing, SPEC-010) | bug against SPEC-010 §13 (everything in a pack is untrusted) | `PathOutside` refuses a file name that is rooted, has a drive, or a `..` segment | 7b |

**My calls:** the fixes above. **Open:** none (CR-3 answered).
New ACs: AC-11 a stuck reader turns a backup into NOT OK within the timeout, never a hang (CR-1);
AC-12 a restore keeps PreserveOriginals and Email.SendWith and drops `Hook.*` (CR-3);
AC-13 a crafted pack cannot place a file on a station file name, nor a page file name outside its
group (SR-1, SR-7).

## 06 · Assumptions

| # | Assumption | If wrong |
|---|---|---|
| A1 | SQLite's backup API gives a consistent copy while the app has the database open (EF opens connections per operation; no long transaction) | Backup must close the app first (CLI path only); §08 alternative B |
| A2 | Jim's stick is ≥ 8 GB, exFAT or FAT32; no file in a station reaches FAT32's 4 GB limit (largest today: the DB, well under 100 MB) | A FAT32 stick refuses at that file; detect and say "reformat as exFAT" |
| A3 | A USB stick can appear as Removable or (USB SSD) as Fixed | The list shows both kinds of non-system drives; Browse covers the rest |
| A4 | Jim's evidence stays under one root (`C:\Evidence-Scans`) | Pack already refuses a group outside the root (`CliRunner.cs:803`) |
| A5 | The Drive folder from setup (`station\drive-folder.txt`) is where `to-franz` lives | The test copy asks for the folder |

## 07 · Data model

No schema change, no migration.

- **Settings rows** (`AppSettingsService`, key/value):
  - `Backup.Folder` — string, the last folder chosen; default empty. Per station.
  - `Backup.Keep` — int, default 3 (Q1). Not shown in the UI.
- **The last backup** is read from the stick and from a small record in the station folder,
  `last-backup.json` (`{ "folder", "code", "at" }`, written atomically after verification), so
  the "Last backup" line works with the stick unplugged. It is information only; nothing trusts it.
- **Test-copy marker** — `test-copy.json` in a workspace root (`{ "from": "<pack>", "code",
  "evidenceRoot", "at" }`). Its presence makes `station pack` refuse and the title read TEST COPY.
- Documented in code: doc comments on `StationBackup`, `LastBackupRecord`, `TestCopyMarker`.

## 08 · Architecture and approach

**Core (`FgScanner.Core.Station`)**

- `StationPacker` gains kind `backup`: refuses `BasedOnManifest`, packs everything, verifies, and
  **skips the freeze record** — the one line that separates a backup from a send. Everything else
  (at-rest check, verify-after-write, delete-on-failure, free-space check) is reused unchanged.
  `isAppOpen` and the at-rest checks are supplied by the caller, as today.
- `StationBackup` (new): wraps the packer for the backup kind, then applies retention — lists
  `*-backup` folders in the target, keeps the newest N **that verify**, never deletes the one just
  made, never deletes a folder it cannot verify unless it is older than the newest verified one
  *and* has no manifest (a stranded half-copy). Writes `last-backup.json`.
- `TestCopyOpener` (new, Franz's side): `station apply` into an empty workspace with
  `--test-copy --evidence <folder>`; applies as today with the manifest's evidence root, then
  rewrites the absolute paths (`Group.DirectoryPath`, trash `GroupDirectoryPath`, `TrashFolderPath`,
  `Profile.BaseDirectory`) from the old root to the new, and writes `test-copy.json`.
  *Alternative:* teach apply to place files under another root directly — rejected, because
  apply's "a pack never chooses where files land" check is the security boundary of SPEC-010 and
  a test-only flag inside it weakens every real apply.

**CLI** — `fgscanner station backup --to <folder> [--keep N] [--data-dir]`, holding the station
lock like pack. `station apply … --test-copy`. `station pack` refuses in a test copy.

**App**

- `BackupService` (App/Services): runs inside the app, which already holds the instance lock. It
  checks its own "at rest": no `CaptureInHand`, no unsaved pages on the Scan page, recovery empty;
  holds the OCR/AI workers (a new `WorkGate` they check before claiming a job; the current page
  finishes first); copies the DB with `DbBootstrapper.BackupDatabase` (`DbBootstrapper.cs:40`,
  the backup API); runs `StationBackup` on a background thread; releases the gate.
- `BackupWindow` (Views/Dialogs): drive list + Browse, Back up, progress (files and MB), result.
  "Send a test copy to Franz" is a second button that targets `<drive folder>\to-franz\copies`.
- `ShellViewModel` exposes `LastBackupText`; the left column shows it under the button (Q2).
- `UpdateService` offer (Q3/Q4): "Your last backup was <when>. [Back up first] [Install now] [Later]".

**Alternatives considered**

- *A. Plain folder copy (robocopy) to the stick* — no receipt, no code to read out, no proof the
  copy is whole; the pack format already gives all three. Rejected.
- *B. Back up only from the CLI with the app closed (desktop shortcut, like Send)* — simpler and
  already nearly possible, but Franz asked for it inside the app, and a shortcut is easy to
  forget. Kept as the CLI verb; the button is the main path.
- *C. Incremental backups (delta against the last one)* — faster after the first, but a restore
  then needs a chain of folders, any one of which can be missing. Full copies take ~1–3 minutes
  at today's size; revisit past ~20 GB (§15).

**Data access** — no MCP, index or cache; the data is files and one SQLite file.
**System evolution** — nothing reusable as a skill; the pack format is already the reusable part.

## 08a · AI opportunity assessment

AI does not fit: copying and checking files must be exact and deterministic, and there is no
classification or judgement in it. Not worth it.

## 09 · Design system compliance

The app's own WPF Fluent theme (the Organic web kit does not apply to this WPF app, per the
project brief). The Back up button matches the About button's style; the window uses the existing
dialog pattern (`Views/Dialogs`, `DialogFit`). Keyboard: every control reachable by Tab, Enter
starts the backup, Esc closes when no backup runs; the progress and result are text, not colour
alone.

## 10 · Acceptance criteria

> **AC-1** — A backup pack contains the DB and every evidence and trash file, verifies with
> `station verify`, and the station's freeze record and sent manifest are byte-identical before
> and after. *Proven by:* `StationBackupTests` → "a backup never moves the round trip's baseline"

> **AC-2** — A send made after a backup is still accepted by the return built from the previous
> send (the round trip does not notice backups). *Proven by:* `StationBackupTests` → "a backup
> between send and return changes nothing the return is checked against"

> **AC-3** — With Keep = 3 and four backups, the oldest is deleted only after the fourth
> verifies; a failed fourth deletes nothing; a folder with no manifest older than the newest
> verified backup is removed. *Proven by:* `StationBackupTests` → retention cases

> **AC-4** — A backup refuses while a capture is in hand, while the Scan page holds unsaved
> pages, and with a non-existent target, each with a plain reason. *Proven by:*
> `BackupServiceTests`

> **AC-5** — During a backup no reading job starts; the job in progress finishes; held jobs run
> after. *Proven by:* `WorkGateTests`

> **AC-6** — The newest backup applied into an empty station on the spare reproduces the station:
> same group, document and page counts, identical file hashes, and FG Scanner opens it. *Proven
> by:* manual, Claude on the spare (Prompt 9)

> **AC-7** — A test copy opens into a workspace with evidence under the folder Franz named; every
> stored path points inside it; `station pack` there refuses; the title says TEST COPY; Jim's
> round-trip workspace's files and database are unchanged. *Proven by:* `TestCopyTests` +
> manual on Franz's PC

> **AC-8** — The update offer shows the last backup and Back up first runs a backup before the
> install. *Proven by:* `UpdateOfferTests` (view-model level) + manual on the spare

> **AC-9** — "Last backup" reads from `last-backup.json` with the stick unplugged, and says
> "Never" on a station with none. *Proven by:* `ShellTests`

> **AC-10** — Pulling the stick mid-backup leaves the station unchanged and reports NOT OK; the
> next backup succeeds and the stranded folder is never counted as a backup. *Proven by:*
> `StationBackupTests` (simulated IOException) + manual on the spare

## 11 · Test strategy

**11.1 — The failing tests to write first**

| Feature | Test file | The failing assertion |
|---|---|---|
| Backup kind | `tests/FgScanner.Core.Tests/Station/StationBackupTests.cs` | freeze record unchanged after a backup |
| Retention | same | oldest of 4 removed only after the 4th verifies |
| CLI verb | `tests/FgScanner.Cli.Tests/StationBackupVerbTests.cs` | `station backup --to` prints `OK xxxx-xxxx` and exit 0 |
| Work gate | `tests/FgScanner.App.Tests/WorkGateTests.cs` | no job claimed while held |
| App backup | `tests/FgScanner.App.Tests/BackupServiceTests.cs` | refuses with a capture in hand |
| Last backup | `tests/FgScanner.App.Tests/ShellTests.cs` | "Last backup: Never" on a fresh station |
| Test copy | `tests/FgScanner.Cli.Tests/TestCopyTests.cs` | every group path under the new root; pack refuses |
| Update offer | `tests/FgScanner.App.Tests/UpdateOfferTests.cs` | the offer text names the last backup |

**11.2 — Test data** — the existing station fixtures used by `StationPackerTests` (temp folders
built per test); the ladder fixtures are untouched. Rehearsal data: the spare's copy of Jim's 9/25
station only — never Jim's or Franz's real data.

**11.3 — Verification suite** — `dotnet build -c Release` (0 warnings), `dotnet test -c Release`,
`dotnet format --verify-no-changes`; the rehearsal on the spare (Prompt 9): backup to a real USB
stick, verify, restore into an empty station, compare.

## 12 · Edge cases and failure modes

| Case | Expected behaviour | Where handled |
|---|---|---|
| No stick plugged in | List empty, "Plug in your USB stick" + Browse | BackupWindow |
| Stick full | NOT OK with MB needed vs free; nothing deleted to make room | StationPacker free-space check |
| Stick pulled mid-copy | NOT OK; station untouched; half folder left without manifest, never counted, cleaned next time | StationPacker catch + StationBackup retention |
| Laptop sleeps mid-copy | Kept awake for the run (as pack does) | BackupService |
| Capture in hand / unsaved pages | Refused with the reason | BackupService |
| Reading jobs running | Held, then released | WorkGate |
| Target inside the station's own folders | Refused (existing check) | StationPacker |
| FAT32 and a file ≥ 4 GB | NOT OK "reformat the stick as exFAT" | StationBackup |
| Two backups in the same minute | `-2-backup` suffix (existing naming) | StationPacker |
| Drive folder never set up (test copy) | "Ask Franz to set up the Drive folder" | BackupWindow |
| Test copy folder not empty | Refused, as apply into an empty station | StationApplier |
| Backup folder applied to Jim's live station | Refused (not based on his last send) — existing | StationApplier |

## 13 · Security and configuration

Not web-facing. Two points:
- A test copy arrives through Drive and is untrusted, exactly like a send: it is verified, and
  applied only into an empty workspace; path rewriting only replaces the manifest's own evidence
  root prefix with Franz's typed folder and refuses any stored path outside that root.
- The stick holds evidence unencrypted unless Q7 (b). No new environment variables, no secrets.

## 14 · Observability

- Logged (Serilog, Information): backup start (target, kind), result (code, files, bytes,
  seconds) or refusal reason; retention deletions by folder name. Never file contents.
- Shown: `OK xxxx-xxxx` or `NOT OK — <reason>` in the window; "Last backup" line always.
- Silent failure would be "no backups for weeks": the always-visible Last backup line and the
  update offer make it visible. Franz sees it on Teams at every cycle.

## 15 · Performance and scale

Today 1.54 GB / 1,413 files: ~22 s to a local disk; a USB 3 stick ~1–2 min, USB 2 ~3–5 min.
Jim adds a few hundred pages a week; at 10× (15 GB) a full backup takes ~10–20 min on USB 3 —
that is the threshold to revisit incremental backups (§08 C). Retention × 3 needs ~3× the station
size free.

## 16 · Regression risk

| # | What could break | Why | Evidence | Mitigation |
|---|---|---|---|---|
| 1 | The round trip refuses Franz's return | A backup writing the freeze record would make the next return "not made from this station's last pack" | `StationPacker.cs:138` | Backup kind skips it; AC-1, AC-2 |
| 2 | A backup that is not consistent | The app has the DB open and may write during the copy | app holds it; `DbBootstrapper.cs:40` | Backup API + window blocks the UI + WorkGate; verify-after-write |
| 3 | OCR/AI writing during a copy fails the verify | Workers cannot be paused today | `OcrWorker.cs`, `AiWorker.cs` | WorkGate (AC-5) |
| 4 | Retention deletes the only good backup | Wrong ordering or counting an unverified folder | new code | Delete only after the new one verifies; count only verified; AC-3 |
| 5 | Pack's existing callers change behaviour | `StationPacker` is shared with send/return | `StationPacker.cs` | Kind check confined to the freeze write and basedOn refusal; existing pack tests stay green |
| 6 | Test copy damages Jim's round-trip workspace on Franz's PC | Both live on Franz's PC | `StationApplier.cs:363` | Separate `--data-dir` and evidence folder; AC-7 checks the workspace's hashes before/after |
| 7 | The update offer changes the updater | `UpdateService.cs:72` | updater rehearsed in SPEC-010 | Only the dialog changes; `UpdateInstaller` untouched; AC-12 of SPEC-010 re-run in Prompt 9 |

## 17 · Migration and rollback

No data migration. Ships in the first release after P12 (with the About button), as a
**pre-release**, rehearsed on the spare, made Latest only at a cycle's return (ADR-0018).

**Rollback** — the previous installer; nothing in this feature changes the database schema, so
an older version opens the station unchanged. Backups on the stick stay readable by any 0.8.x
`station verify`/`apply`.

**Restoring Jim's station from his stick (2am procedure)** — on Teams, Franz guiding:
1. **[Jim]** Plug in the stick. Close FG Scanner.
2. **[Franz]** On "give control", open a command window.
3. **[Franz]** Move the broken station aside: rename `%APPDATA%\FGScanner` to
   `FGScanner.broken-<date>` and `C:\Evidence-Scans` to `Evidence-Scans.broken-<date>`.
4. **[Franz]** Run `"C:\Program Files\FGScanner\cli\fgscanner.exe" station verify
   "<stick>:\FGScanner-Backup\<newest>"` → `OK xxxx-xxxx`; compare with the code on the label or
   in Last backup.
5. **[Franz]** Run `... station apply "<stick>:\FGScanner-Backup\<newest>" --evidence
   C:\Evidence-Scans` → `OK`.
6. **[Jim]** Start FG Scanner; check the group count with Franz.
7. **[Franz]** Only after Jim has worked a day: delete the `.broken-<date>` folders.

## 18 · Documentation updates

- **CLAUDE.md** — the "Stations exchange data only as verified packs" rule gains: a `backup` pack
  never writes the freeze record; a test copy can never pack; restore is apply into an empty station.
- **docs/station-round-trip.md** — Back up in each cycle; the restore procedure (§17); the test copy.
- **docs/adr/0019-station-backup.md** — backup as a pack kind (why not robocopy, why full copies).
- **docs/user-guide.md** — Back up for Jim, in plain steps with the code check.
- **docs/FEATURE-PARITY.md** — a backup row.
- **Memory** — update the SCANNER-010 handoff; a memory "backups never move the round-trip
  baseline".

## 19 · Phase plan

### Phase 1 — Backup in the core and CLI
- **Objective** — a verified full backup to any folder, from the CLI.
- **Delivers** — `backup` kind, `StationBackup` with retention, `station backup` verb.
- **Not in this phase** — any UI; test copies.
- **Done when** — AC-1, AC-2, AC-3, AC-10 (automated part).

### Phase 2 — The button
- **Objective** — Jim backs up from inside FG Scanner.
- **Delivers** — WorkGate, BackupService, BackupWindow, Last backup line, update offer.
- **Not in this phase** — test copies; any change to `UpdateInstaller`.
- **Done when** — AC-4, AC-5, AC-8, AC-9.

### Phase 3 — Test copy, docs, review, rehearsal
- **Objective** — Franz opens Jim's current work safely; everything reviewed and rehearsed.
- **Delivers** — Send a test copy, `--test-copy` apply, TEST COPY marker; docs per §18; reviews;
  pre-release rehearsed on the spare with a real stick.
- **Not in this phase** — making the release Latest (that is a cycle's return).
- **Done when** — AC-6, AC-7, AC-10 (manual), and the reviews' findings resolved.

## 20 · Prompt pack

`docs/specs/SPEC-SCANNER-2026-011-station-backup-PROMPTS.md` (written after approval).
Planned: 1 backup kind · 2 retention + CLI verb · 3 WorkGate + BackupService · 4 window, button,
Last backup, update offer · 5 test copy · 6 docs · 7 `/code-review max` (cold subagent) ·
8 security review of the test-copy path (cold subagent) · 9 release pre-release + rehearsal on the
spare with a USB stick. **Franz stops:** approval of this spec; plugging a USB stick into the
spare (physical); UAC on the spare in Prompt 9.

## 21 · Definition of done

- [ ] All acceptance criteria met
- [ ] Failing tests written first, now passing
- [ ] Full suite green
- [ ] `/code-review max` run, findings resolved or accepted in writing
- [ ] Security review of the test-copy path passed
- [ ] Documentation updated per §18
- [ ] Pre-release rehearsed on the spare (backup, restore, test copy, update offer)
- [ ] Rollback (previous installer opens the station) checked on the spare

## 22 · Sign-off

| | |
|---|---|
| **Review round answered** | ☑ 2026-10-17 — https://claude.ai/artifact/Qdunwnh6FxJBVM984KjiNj |
| **Franz approved** | ☑ 2026-10-17 (Round A, verdict approve) |
| **Built** | ☐ date: |
| **Verified in production** | ☐ date: |
