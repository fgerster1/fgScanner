# SPEC-SCANNER-2026-010 — Prompt pack

Prompts for [SPEC-SCANNER-2026-010-jim-remote-update-and-data.md](./SPEC-SCANNER-2026-010-jim-remote-update-and-data.md). Claude runs them in order, continuously: each
checkpoint is run and logged by Claude, which goes straight on unless the prompt's
`Franz stop:` line says otherwise (fg-programming-specs `references/running-the-pack.md`).

Approved 2026-10-09 in Round B (https://claude.ai/artifact/EjYRfaTz1UDJ1zQHuQzEPi), with the
transport changed to a **Google Drive shared folder** and the storage platform deferred.
Not tracked in Linear (FG Scanner work stays out of it).

**Rules for every prompt** (CLAUDE.md, and what earlier specs learned):

- Tests first: write the named test, watch it fail for the right reason, then make it pass.
- CI's commands must pass: `dotnet build -c Release` (warnings are errors),
  `dotnet test -c Release` (MTP mode), `dotnet format --verify-no-changes`.
- Work on branch `spec-scanner-010-remote-update`. Claude never pushes, tags or publishes a
  release (the permission guard blocks it): Franz runs those as `!` commands that Claude gives him.
- **Never touch Franz's or Jim's real data** in a test. Tests use temp folders and temp SQLite
  databases; the rehearsals use copies (`%APPDATA%\FGScanner.rehearsal-jim-laptop-2026-09-25`,
  `C:\Evidence-Scans`), never `D:\Evidence-Scans` or the original copies on F:.
- No change to the evidence contract (`index.json` keys, `manifest.json`, the thirteen field
  names) and no change to the database schema. `capturedBy` is never rewritten (ADR-0005).
- All file writes are atomic (temp + `File.Replace`/move); dates ISO-8601; invariant culture.
- The CLI publishes to `cli\`, never beside the app (`build/verify-publish.ps1`).
- Comments explain why. No feature the spec does not name.
- A test that turns red because of a new constraint is a finding: fix the code or the test
  data, never loosen the constraint.
- Re-runnable: each prompt says what "already done" looks like. If it is, verify and move on.
- Continuous: run each checkpoint; when green, commit (`SPEC-SCANNER-2026-010 P<n>: …`), add a
  line to the Run log at the bottom, and go on. Stop only at a prompt's `Franz stop`.

**Run order — Franz stops:** P1 (publish 0.7.1, click UAC, put it back to draft — outward,
physical) · P11 (push the branch, merge, tag 0.8.0; click UAC in the rehearsal — outward,
physical) · P12 (the cycle with Jim on Teams — physical; publish 0.8.0 — outward). P9/P10
stop only if a finding needs a decision the spec didn't make. P2–P8 run straight through.

---

PROMPT 1 of 12 — Finish the 0.5.4 → 0.7.1 update rehearsal
Spec: §04 (Rehearsal state), §10 AC-1, §17 Phase 1    Phase: 1    Depends on: —
Franz stop: Outward action + Physical — Franz publishes v0.7.1, clicks UAC, then sets it back to draft

Already done if: the Run log has a P1 line with AC-1's counts, and
`%APPDATA%\FGScanner.station-aside-2026-10-09` no longer exists (Franz's data is back).

Do only this:
1. Before anything, hash every file under `C:\Evidence-Scans` to a list in the job folder.
2. Give Franz: `! cd "C:\Users\Franz Gerster\Visual\FgmakerScanner" && gh release edit v0.7.1 --draft=false --latest && gh release edit v0.6.0 --draft=false --latest=false`. Wait for "published".
3. Confirm `releases/latest/download/appcast.xml` names 0.7.1. Start the installed 0.5.4;
   answer Yes to the update offer through UI Automation; Franz clicks UAC.
4. Start 0.7.1. Record: version shown, 20 groups / 1,368 pages (read through the CLI against
   the live database, app closed), `fgscanner.db.bak-0.7.1` present,
   `PRAGMA integrity_check` = ok, the image hash list unchanged.
5. Close the app. Give Franz the command to set v0.7.1 **and** v0.6.0 back to draft (Q6, Q-B9).
6. Restore Franz's station: rename `%APPDATA%\FGScanner` → `%APPDATA%\FGScanner.rehearsal-run-2026-10-09`,
   `FGScanner.station-aside-2026-10-09` → `FGScanner`. Leave `C:\Evidence-Scans` in place (P11 reuses it).
7. Reinstall the newest local build only if Franz asks; the installed 0.7.1 is what his
   station already ran.

Do NOT in this prompt: change any code, publish anything yourself, delete the rehearsal copy.

CHECKPOINT 1 — Claude runs and records:
  · `appcast.xml` from the latest release → version 0.7.1 (before step 3)
  · the installed `FgScanner.exe` file version → 0.7.1
  · CLI group/page count on the rehearsal database → 20 / 1,368
  · image hash list before vs after → 0 differences
  · `gh release view v0.7.1 --json isDraft` → true (after Franz's command)
  · `%APPDATA%\FGScanner` holds Franz's own database again (its group count differs from 20)

---

PROMPT 2 of 12 — A second station on one machine: `StationPaths` and `--data-dir`
Spec: §03 item 1, §04 (One station per Windows account), §10 AC-7, §16 R7    Phase: 2    Depends on: P1
Franz stop: none

Already done if: `StationPathsTests` exist and pass, and `FgScanner.exe --data-dir <x>` puts
nothing in `%APPDATA%\FGScanner`.

Do only this:
1. Write `StationPathsTests` first (Core.Tests): default resolves to `%APPDATA%\FGScanner`
   exactly as today; `--data-dir X` puts the database, `trash`, `recovery`, `index-drafts`,
   logs and the `tessdata` lookup under X; a relative path is made absolute; the default is
   unchanged when no flag is given.
2. Add a `StationPaths` record resolved once at startup (App and CLI) and route every
   current `SpecialFolder.ApplicationData` use for station data through it
   (`DbBootstrapper`, `App.xaml.cs`, `ScanSessionService`, `IndexDraftStore`; check
   `TesseractPaths` and `CredentialStore` — the Gemini key stays in Credential Manager).
3. CLI: a visible `--data-dir` on the root command; the hidden `--db` keeps working.
4. The window title shows the station folder's name when it is not the default, so Franz
   can see which station he is in.

Do NOT in this prompt: anything about packs or manifests (P3–P5); shortcuts (P7); a
separate single-instance lock per station.

CHECKPOINT 2 — Claude runs and records:
  · `dotnet test -c Release` → all green, count higher than before
  · `dotnet build -c Release` → 0 warnings
  · run the built app with `--data-dir %TEMP%\fg-p2` → that folder gets `fgscanner.db`; `%APPDATA%\FGScanner` modified time unchanged
  · the title bar shows the station name (UIA read)

---

PROMPT 3 of 12 — The station manifest and `station verify`
Spec: §07 (`station-manifest.json`), §08, §10 AC-2    Phase: 2    Depends on: P2
Franz stop: none

Already done if: `StationManifestTests` pass and `fgscanner station verify <folder>` exists.

Do only this:
1. Tests first (`tests/FgScanner.Core.Tests/Station/StationManifestTests`): build a manifest
   over a temp station; verify an exact copy → 0 mismatches; a flipped byte, a missing file and
   an extra file are each named; a folder with no manifest yet → "still arriving", not a
   mismatch; the manifest round-trips through JSON byte-identically.
2. `FgScanner.Core.Station`: `StationManifest` (header and `files[]` as §07), a builder that
   hashes the database, the evidence root and trash, and `Verify`.
3. CLI `fgscanner station verify <pack-folder>` printing `OK <code> · n files · size` or
   `NOT OK — <first problem>`, with the full list in a log beside the manifest. `<code>` is the
   first four hex digits of the manifest's SHA-256 (Jim reads it out on Teams).

Do NOT in this prompt: write packs or deltas (P4), apply anything (P5).

CHECKPOINT 3 — Claude runs and records:
  · `dotnet test -c Release --filter-class *StationManifestTests*` (or the MTP equivalent) → green
  · full suite green; format clean
  · `fgscanner station verify` on a hand-made temp pack → `OK nnnn`; after flipping one byte → `NOT OK` naming that file

---

PROMPT 4 of 12 — `station pack`: delta, renames, freeze record
Spec: §07, §08, §10 AC-3, AC-4, §12    Phase: 2    Depends on: P3
Franz stop: none

Already done if: `StationPackTests` pass, including `RenamesAreNotResent` and
`SecondPackWithNoChangesShipsOnlyTheDatabase`.

Do only this:
1. Tests first (`StationPackTests`): a first pack ships everything; a second pack `basedOn`
   the first ships only the database when nothing changed; a renamed file ships as a rename
   (0 bytes); a changed file ships; pack refuses while the app is open (the single-instance
   mutex), while `recovery\` holds a session, with a `fgscanner.db-journal`, or with reading
   jobs pending; pack refuses a destination inside the data folder or the evidence root;
   the manifest is written last.
2. `fgscanner station pack --to <transport folder> [--based-on <manifest>] [--kind send|return]`:
   creates a new uniquely named folder (`<station>-<yyyy-MM-dd-HHmm>[-n]-<kind>`), copies the
   database with SQLite's backup API, copies only what the receiver lacks, writes the manifest
   last, keeps a copy of the sent manifest and database hash in the station folder (the
   freeze record), checks free space first (pack size + 1 GB).
3. Keep the machine awake while packing.

Do NOT in this prompt: apply, change logs, the needs-Jim sheet, the PowerShell sender.

CHECKPOINT 4 — Claude runs and records:
  · `StationPackTests` → green; full suite green
  · pack a copy of the rehearsal station (`--data-dir` on a copy) into a temp folder → `OK nnnn`, then `station verify` on it → `OK nnnn` (same code)
  · a second pack with nothing changed → bytes shipped ≤ 10 MB (AC outcome 2)

---

PROMPT 5 of 12 — `station apply`: the gates, Jim's settings kept, atomic swap, rollback
Spec: §07 (kept on apply), §10 AC-5, AC-6, §12, §16 R2/R3, §17 Rollback    Phase: 2    Depends on: P4
Franz stop: none

Already done if: `StationApplyTests` pass and `fgscanner station apply` and
`station apply --undo` exist.

Do only this:
1. Tests first (`StationApplyTests`): refuses — changing nothing — on an app version or last
   migration different from the pack's; on a live database whose hash differs from the freeze
   record (Jim worked during the freeze); with the app open; with a journal file or a
   recovery session; on a short or missing file. On success: the receiving station's
   `Settings` rows and `index-drafts\` survive; adds, changes and renames reach the manifest's
   final state; deletions are moved aside, never erased; `integrity_check` ok; the replaced
   database and files are kept in `apply-backup-<stamp>\`; `--undo` restores them exactly.
2. Implement: verify the pack first; stage into a sibling folder; swap the database
   atomically; re-verify the final state against the manifest; print `OK <code>` or
   `NOT OK — call Franz` plus the reason in the log.

Do NOT in this prompt: the change log or needs-Jim sheet (P6); shortcuts (P7).

CHECKPOINT 5 — Claude runs and records:
  · `StationApplyTests` → green; full suite green
  · end to end on copies: pack A → apply into B → B's groups/pages equal A's, B's Settings rows unchanged, `integrity_check` ok; `--undo` → B byte-identical to before (hash list)
  · touching B's database after its freeze record → apply prints `NOT OK` and B is unchanged

---

PROMPT 6 of 12 — The change log and the needs-Jim sheet
Spec: §07 (`changes.jsonl`), §10 AC-9, Q-B8, C-B3    Phase: 2    Depends on: P5
Franz stop: none

Already done if: `StationChangeLogTests` pass and a return pack contains `changes.jsonl` and
`needs-jim.txt`.

Do only this:
1. Tests first (`StationChangeLogTests`): every changed cell between two databases is one
   row (table, key, column, old, new); added and removed rows are listed; Franz's name and
   the time are on each row; an unchanged database gives an empty log.
2. `station pack --kind return` diffs the database against the one received and writes
   `changes.jsonl`; it writes `needs-jim.txt` in plain words: each open group, why it is still
   open (the importer's refusal text via `ImportRefusals`), and what Jim should look for on
   the paper.

Do NOT in this prompt: any portal change (the import runbook mention is P11's docs work).

CHECKPOINT 6 — Claude runs and records:
  · `StationChangeLogTests` → green; full suite green
  · a return pack from a copy where one Box value was set → `changes.jsonl` has exactly that one row; `needs-jim.txt` lists the open groups

---

PROMPT 7 of 12 — The first-cycle sender for 0.5.4, and Jim's two shortcuts
Spec: §03 items 3–4, §08, §11 (sender agrees with the C# verb), §12    Phase: 2    Depends on: P6
Franz stop: none

Already done if: `build/station/Send-ToFranz-0.5.4.ps1` exists, its test passes, and the
installer creates both shortcuts.

Do only this:
1. Test first: pack a fixture station with the PowerShell sender; `fgscanner station verify`
   accepts it and the manifest is byte-identical to the C# pack of the same fixture.
2. `Send-ToFranz-0.5.4.ps1`: Windows PowerShell 5.1-compatible (Jim's laptop), first pack only
   (no `basedOn`), the same checks as P4 (app closed, journal, recovery, free space), writes
   into the `to-franz` folder under Jim's Google Drive, prints `OK nnnn — leave the laptop on
   until Google Drive says it's done` or `NOT OK — call Franz`.
3. The installer adds two desktop shortcuts that run the CLI: "Send my scans to Franz"
   (`station pack --kind send` into `to-franz`) and "Install Franz's update"
   (`station apply` from the newest folder in `to-jim`), each in a console window that stays
   open until a key is pressed. The Drive folder location comes from one setting written at
   setup, never guessed.

Do NOT in this prompt: CI changes (P8), the release (P11).

CHECKPOINT 7 — Claude runs and records:
  · the sender test → green; full suite green
  · `powershell.exe -Version 5.1`-style run of the sender on a fixture → `OK nnnn`
  · an installer built locally; installing it into a scratch VM is not possible here — instead `ISCC` output lists both `[Icons]` entries

---

PROMPT 8 of 12 — CI migration ladder
Spec: §03 item 7, §10 AC-8, §16 R6    Phase: 2    Depends on: P7
Franz stop: none

Already done if: `MigrationLadderTests` pass with one fixture per shipped schema.

Do only this:
1. Build fixture databases from the tagged releases v0.5.4, v0.6.0 and v0.7.1 (check out each
   tag into a temp worktree, run its CLI against an empty temp folder, keep the `.db`), with a
   few groups and pages in each. Commit them under `tests/FgScanner.Data.Tests/Fixtures/ladder/`.
2. `MigrationLadderTests`: each fixture migrates to the current schema through
   `DbBootstrapper.MigrateWithBackup`, opens, and keeps its rows.
3. Document in `docs/release.md`: every new release adds its fixture.

Do NOT in this prompt: change migrations.

CHECKPOINT 8 — Claude runs and records:
  · `MigrationLadderTests` → green (3 fixtures); full suite green; format clean

---

PROMPT 9 of 12 — Code review  [COLD READER: run it in a subagent]
Spec: SPEC-SCANNER-2026-010    Depends on: P8
Franz stop: only if a finding needs a decision the spec didn't make (Spec revision)

Launch a subagent with the spec path and the range `61f367b..HEAD`. It runs `/code-review max`
over P2–P8, then checks compliance with §10: does the code do what the spec says, and only
that? Drift is a bug or a spec revision — say which.

Findings the approved spec already decides: fix them as P9a, P9b… and continue. Findings
needing a decision: fg-fix-findings (Spec revision stop).

CHECKPOINT 9 — Claude runs and records:
  · the findings table in §05 (Revision C) with every finding fixed or declined
  · full suite green after the fixes

---

PROMPT 10 of 12 — Security review  [COLD READER: run it in a subagent]
Spec: §13, §12    Depends on: P9
Franz stop: only if a finding needs a decision (Spec revision)

A subagent reviews the §13 surfaces: a pack or return folder is untrusted input (path
traversal in `rel`, absolute paths, symlinks/junctions, oversized files, a manifest that
names a file outside the station); apply must never write outside the data folder and the
evidence root; no credential or Google account detail in any script, log or pack; the
shared-folder rules in §13 are in the runbook. Fix as P10a… as in P9.

CHECKPOINT 10 — Claude runs and records:
  · a test per traversal case (`..\`, absolute path, junction) → apply refuses; full suite green

---

PROMPT 11 of 12 — Release 0.8.0: docs, draft, and the 0.5.4 → 0.8.0 rehearsal
Spec: §18, §19 Phase 2, §17 Phase 1 ("repeat with 0.8.0"), §10 AC-1    Depends on: P10
Franz stop: Outward action + Physical — Franz pushes the branch, merges after CI is green, pushes the tag; clicks UAC in the rehearsal

Already done if: a v0.8.0 draft exists, verified, and the Run log has the rehearsal line.

Do only this:
1. Docs per §18: CLAUDE.md rules (stations exchange data only by `station pack/verify/apply`;
   never sync a live folder; releases published only at a cycle's return; `--data-dir` for a
   second station; no captures in another station's workspace); new
   `docs/station-round-trip.md` (both sides, tagged `[Jim]`/`[Franz]`/`[Claude]` steps, the
   Google Drive setup and clean-up from §13 and §17); an ADR for the station manifest;
   `docs/release.md`; JimsStuff `docs/fgscanner-import-runbook.md` (committed groups arrive
   from the NucBox workspace with `changes.jsonl`); `docs/FEATURE-PARITY.md`.
2. Bump `<Version>` to 0.8.0; give Franz the push and merge commands; after CI, the tag.
3. When the draft is built: run `build/verify-publish.ps1` on it; download, install and start
   it on the NucBox (CLAUDE.md: install and start a draft before publishing).
4. Rehearse on Jim's 9/25 copy in a `--data-dir` workspace: the 0.5.4 sender → verify →
   0.8.0 opens it (migrates) → set one Box, commit nothing → return pack → apply into a second
   copy standing in for Jim's laptop → counts and settings match. Franz's own station is not touched.
5. Do **not** publish 0.8.0 — that happens in P12 after Jim's pack is verified (Q3, Q-B9).

CHECKPOINT 11 — Claude runs and records:
  · CI on the branch green (Franz reports or `gh run list`)
  · `gh release view v0.8.0 --json isDraft,assets` → draft, installer + appcast present; signature verified against 0.5.4's key
  · the rehearsal: sender `OK`, verify `OK` same code, apply `OK`, 0 hash mismatches, Jim's Settings rows unchanged

---

PROMPT 12 of 12 — The first cycle with Jim
Spec: §17 Phase 3, §10 AC-10, §13    Depends on: P11
Franz stop: Physical (Teams calls with Jim) and Outward action (publish 0.8.0; share the Drive folder)

Already done if: Jim's station runs 0.8.0, the return is applied with `OK`, and AC-10's counts
are in the Run log.

Do only this (each step tagged as in §17):
1. `[Franz]` Teams call: Jim's Google account (on his Yahoo address if he has none; he types
   his own password), Drive for Desktop installed and signed in; `[Claude in Chrome]` creates
   `FGScanner-Jim` with `to-franz` and `to-jim` in Franz's Drive and stops before the Share
   click; `[Franz]` shares it with Jim only; `[Jim]` adds it to his Drive; `to-jim` marked
   Available offline. Confirm UAC rights. Collect Box values (Q-B6).
2. `[Franz]` copies `Send-ToFranz-0.5.4.ps1` into place and runs it with Jim on "give control"
   (Q-B7) → `OK nnnn`. Wait for Drive's upload to finish.
3. `[Claude]` `station verify` on the NucBox → `OK nnnn` (same code) = Jim's backup. Copy the
   verified pack to the Tower backup; delete it from Drive and Drive's bin (§13).
4. `[Franz]` publish 0.8.0 (Claude gives the command). `[Jim]` start FG Scanner → Yes → UAC →
   start again. Confirm 0.8.0 and his group count on Teams.
5. `[Claude/Franz]` Process in workspace `Jim` (`--data-dir`): auto-orient off there only,
   Box values, read all pages, Propose, note fixes, commit what is finishable, import
   committed groups into the portal; `station pack --kind return` into `to-jim`.
6. `[Jim]` "Install Franz's update" → `OK nnnn`. `[Franz]` checks counts on Teams. Delete the
   return from Drive and its bin.

Do NOT in this prompt: publish any other release; change Jim's settings by hand.

CHECKPOINT 12 — Claude runs and records:
  · verify codes match on both sides, both directions
  · Jim's group/page counts equal the workspace's; his Settings unchanged (read out on Teams)
  · `to-franz` and `to-jim` empty, Drive bin emptied
  · §22 Built and Verified dates filled; README row → Built

---

## Run log

| Date | Prompt | Result |
|---|---|---|
| 2026-10-09 | P2 | Green — `StationPaths` + `--data-dir` (app and CLI); suite 1,186 (0 failed), Release 0 warnings, format clean; real window titled "FG Scanner — station fg-p2", workspace holds db/logs/recovery/tessdata, `%APPDATA%\FGScanner` db hash unchanged. Incident: a stale 9/21 exe (old TFM folder) was launched first and opened the rehearsal copy — no schema change, counts equal; reset byte-for-byte from the 9/25 original. P1 still waits on Franz's publish. |
| 2026-10-14 | P3 | Green (on FGERSTERPC, back from the NucBox — all code via GitHub, scanner memory identical) — `StationManifest` / builder / verifier + `fgscanner station verify`; pack layout `fgscanner.db` + `evidence/…` + `trash/…`, manifest last, rels with `/`; no manifest → `NOT YET` (exit 3). Suite 1,196 (1,193 passed, 3 skipped, 0 failed), Release 0 warnings, format clean. Real exe on a PowerShell-written manifest: `OK 96da · 4 files`, one byte flipped → `NOT OK — changed: evidence/Dental/scan_00001.jpg`, log beside the manifest. For P4: verify demands every listed file in the pack, so a delta pack needs the base's files counted (or a shipped/kept split) — decide there. `groupId`/`pageId`/`dbChecksum` left empty (Core cannot read the database); P4 may fill them from the CLI. |
| 2026-10-15 | P1 | **Data half passed, auto-update FAILED.** Run on the spare WIN-G31UIM7G129 (NucBox busy): 0.5.4 installed over Jim's 9/25 copy (`C:\Evidence-Scans`, 1,411 files). 0.7.1 + 0.6.0 published 14:10Z; 0.5.4 offered the update, Franz clicked Yes — no UAC, app stayed open, still 0.5.4. Cause (NetSparkle 3.1.0, `UpdateService`, same code in 0.7.1): no CloseApplication handler, so the app never exits; the generated `.cmd` waits 90 s for the PID and gives up; and it runs the downloaded installer by an unquoted path with no `.exe` under `C:\Users\Franz Gerster\…` → `'C:\Users\Franz' is not recognized`. Jim's account is also "Franz Gerster", so his station fails the same way. Both releases back to Draft ~14:25Z (0.5.4 Latest, appcast 0.5.4). Manual install of 0.7.1 from the release file (sha 6f615a2c…) then: version 0.7.1, migrated to `20261005135716_AddPageOrientationSettled` with `fgscanner.db.bak-0.7.1`, `integrity_check` ok, 20 groups / 1,368 pages, evidence 1,411/1,411 identical (0 changed, missing or extra). **Consequence:** Jim's update to 0.8.0 is a guided manual install (P12 step 4 must change), and 0.8.0 fixes the updater test-first. |
