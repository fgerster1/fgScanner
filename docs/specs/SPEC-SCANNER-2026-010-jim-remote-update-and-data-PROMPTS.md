# SPEC-SCANNER-2026-010 — Prompt pack

Prompts for [SPEC-SCANNER-2026-010-jim-remote-update-and-data.md](./SPEC-SCANNER-2026-010-jim-remote-update-and-data.md). Claude runs them in order, continuously: each
checkpoint is run and logged by Claude, which goes straight on unless the prompt's
`Franz stop:` line says otherwise (fg-programming-specs `references/running-the-pack.md`).

Approved 2026-10-09 in Round B (https://claude.ai/artifact/EjYRfaTz1UDJ1zQHuQzEPi), with the
transport changed to a **Google Drive shared folder** and the storage platform deferred.
**Revision C approved 2026-10-16 in Round C** (https://claude.ai/artifact/GwvbCJH8cwXBG9KcVAb5yt):
P1 proved auto-update broken in 0.5.4 and 0.7.1, so Jim installs 0.8.0 by hand from `to-jim\`,
P8b repairs the updater, P11 rehearses both paths on the spare WIN-G31UIM7G129 (`ssh spare`),
and verify/processing run on Franz's PC (FG Scanner is uninstalled from the NucBox).
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

**Run order — Franz stops:** P1 (done 2026-10-15) · P11 (push the branch, merge, tag 0.8.0;
publish v0.8.0 as a **pre-release**; click UAC on the spare in both rehearsals — outward,
physical) · P12 (the cycle with Jim on Teams — physical; make v0.8.0 the Latest after Jim runs
it — outward). P9/P10 stop only if a finding needs a decision the spec didn't make. P2–P8b run
straight through.

---

PROMPT 1 of 13 — Finish the 0.5.4 → 0.7.1 update rehearsal  [DONE 2026-10-15 on the spare — see the Run log; kept as written for the record]
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

PROMPT 2 of 13 — A second station on one machine: `StationPaths` and `--data-dir`
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

PROMPT 3 of 13 — The station manifest and `station verify`
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

PROMPT 4 of 13 — `station pack`: delta, renames, freeze record
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

PROMPT 5 of 13 — `station apply`: the gates, Jim's settings kept, atomic swap, rollback
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

PROMPT 6 of 13 — The change log and the needs-Jim sheet
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

PROMPT 7 of 13 — The first-cycle sender for 0.5.4, and Jim's two shortcuts
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

PROMPT 8 of 13 — CI migration ladder
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

PROMPT 8b of 13 — The updater repair
Spec: §03 item 9, §04 (update path), §10 AC-11, §12, §13, §16 R8/R9, §05 Q-C2/Q-C3    Phase: 2    Depends on: P8
Franz stop: none

Already done if: `UpdateInstallerTests` pass, `UpdateService` never calls NetSparkle's
`InstallUpdate`, and `release.yml` writes a `releases/download/v<ver>/` link into the appcast.

Do only this:
1. Tests first, in `tests/FgScanner.App.Tests/UpdateInstallerTests.cs` (the project keeps tests flat), against a new
   `UpdateInstaller` that takes the process starter and the shutdown as seams (no real installer
   runs in a test):
   - a downloaded file with no extension under a profile path with a space is copied to
     `%TEMP%\FGScanner\update\fgscanner-<ver>-win-x64.exe` (atomically) and started with
     `FileName` = that path, `Arguments` = `/VERYSILENT /NORESTART`, `UseShellExecute = true`
     (so Windows raises UAC) — no `cmd`, no script;
   - after a successful start the app's normal shutdown is requested; a start that throws
     (UAC refused: `Win32Exception` 1223) is logged, nothing shuts down, and the copy is removed;
   - the appcast override `FGSCANNER_APPCAST_URL` is used only when it starts with
     `https://github.com/fgerster1/fgScanner/releases/download/`; anything else is ignored
     and logged, and the built-in Latest URL is used.
2. Make them pass: `UpdateInstaller`; in `UpdateService` replace
   `DownloadFinished += … InstallUpdate(…)` with `UpdateInstaller`, and read the override where
   the URL is used. The shutdown goes through the app's existing close path, so a capture in
   hand or a duplex/annotated sequence is never abandoned (CLAUDE.md) — if the close is
   cancelled, the installer has already started and Inno's `CloseApplications` asks; record
   which happens in the Run log.
3. `release.yml:140`: `-u https://github.com/fgerster1/fgScanner/releases/download/v${{ steps.ver.outputs.version }}/`
   so a pre-release's appcast points
   at its own installer.
4. A real-exe check on this PC: build Release, run `UpdateInstaller` against a dummy signed-off
   `.exe` that writes a marker file, under a temp folder with a space — marker written, app
   process gone.

Do NOT in this prompt: change the signature check or the public key; change the installer
script; publish anything.

CHECKPOINT 8b — Claude runs and records:
  · `UpdateInstallerTests` → red first (type missing), then green
  · `grep InstallUpdate src/` → nothing; `release.yml` appcast link is versioned
  · full suite green; Release 0 warnings; format clean

---

PROMPT 9 of 13 — Code review  [COLD READER: run it in a subagent]
Spec: SPEC-SCANNER-2026-010    Depends on: P8
Franz stop: only if a finding needs a decision the spec didn't make (Spec revision)

Launch a subagent with the spec path and the range `61f367b..HEAD`. It runs `/code-review max`
over P2–P8b, then checks compliance with §10: does the code do what the spec says, and only
that? Drift is a bug or a spec revision — say which.

Findings the approved spec already decides: fix them as P9a, P9b… and continue. Findings
needing a decision: fg-fix-findings (Spec revision stop).

CHECKPOINT 9 — Claude runs and records:
  · the findings table in §05 (Revision C) with every finding fixed or declined
  · full suite green after the fixes

---

PROMPT 10 of 13 — Security review  [COLD READER: run it in a subagent]
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

**Revision D fix prompts** (approved in Round D 2026-10-16, every recommendation; D2 refined the
same day). They run before P11, test first, each finding reproduced by a failing test. Nothing
declined. After 10c, a cold-reader subagent re-reviews 10a–10c.

PROMPT 10a — Apply and undo never strand a station (Revision D: P9-1, P9-2, P9-3, P10-6, D5)
Spec: §05 Revision D, §10 AC-5/AC-6, §12    Depends on: P10    Franz stop: none

1. Tests first (`StationApplyTests`): a file locked mid-commit (move-aside and place phases) →
   auto-undo, station byte-identical, NOT OK, `undo.json` written before anything moves; a changed
   file is never deleted by undo when its original was not yet moved aside (P9-1); undo refuses
   with the app open, a journal, recovery scans, or content/files that differ from the freeze record
   apply wrote, and moves the current DB into `apply-undone-…` (temp + move) (P9-3); a backup with no
   plan is skipped; apply refuses when either volume lacks pack size + 1 GB (P10-6).
2. D5: the CLI holds `FGScanner.SingleInstance` for the whole pack/apply/undo (an app started
   meanwhile exits as a second instance); the 0.5.4 sender holds it too.

PROMPT 10b — The incoming database is untrusted (Revision D: P10-1/D1, P10-2+P9-10, P10-3, P10-4/D2, P10-5/D3, P9-6, P10-7, P10-8, P10-10, P10-11, D7)
Spec: §05 Revision D, §07, §13    Depends on: 10a    Franz stop: none

1. Tests first: an empty station drops every Settings row (D1); a path in `Profiles.BaseDirectory`,
   `Groups.DirectoryPath` or `TrashItems.GroupDirectoryPath` outside the evidence root or on a share
   refuses (P10-2/P9-10); the freeze check uses the receiver's own freeze content and requires the
   pack's `basedOnContent` to equal it — a 0.5.4 send keeps `station-sent.db` (its sent database) so
   the content can be computed on Jim's side (P10-3); an extra table/index/trigger/view, or a changed
   table/index, refuses, and views/triggers are rebuilt from `RawSchemaSql`, `trusted_schema=OFF`,
   integrity before binding, receiver attached read-only (D2); the migration gate reads the receiver
   DB's last migration and Settings are copied by named columns (P9-6); identifiers outside
   `^[A-Za-z_][A-Za-z0-9_]*$` refuse (P10-7); a junction/symlink in a pack or station refuses (P10-8);
   a malformed manifest or a database error is NOT OK, never a crash (P10-10); a network drive letter
   is not local (P10-11).
2. D3: the code is 8 hex characters `xxxx-xxxx` everywhere (verb, verify, 0.5.4 sender);
   "Install Franz's update" verifies first, shows the code and asks "Is this the code Franz read
   you? (Y/N)" before apply changes anything. D7: more than one `*-return` waiting → NOT OK.

PROMPT 10c — Updater, change log and the shortcuts (Revision D: P9-8, D4, P9-9, D6, test gaps)
Spec: §05 Revision D, §10 AC-9, AC-11    Depends on: 10b    Franz stop: none

1. Tests first: the updater copies, then waits for a capture in hand, then re-verifies the Ed25519
   signature of the exact copy and only then starts it (P9-8, D4); `UpdateInstallerTests` use an
   update folder with a space; `changes.jsonl` diffs against a migrated copy of the received DB, so a
   value Franz sets in a column a migration added is logged and the migration's own changes are not
   (P9-9); "Send my scans to Franz" sends everything when the station's baseline has no content (D6).

PROMPT 10d — The re-review's follow-ups (Revision D re-review: R-1 to R-8)
Spec: §05 Revision D re-review    Depends on: 10c    Franz stop: none

1. Tests first: apply with a code other than the pack's refuses and changes nothing (R-1); an
   interrupted restore (marker present) is finished by undo without the "changed since" gate (R-2);
   a second undo after a successful one has nothing to undo (R-4); the 0.5.4 sender succeeds while
   taking and releasing the real lock (R-7); "Send my scans to Franz" passes `--based-on` only when
   the freeze record holds content (D6, R-7, with a stand-in CLI that records its arguments).
2. `station apply --expect-code`, passed by the shortcut; every exception after Commit restores;
   restore continues past per-file errors and reports them; undo selects the backup whose plan
   names the pack in the freeze record; space counts moved-aside originals across drives; apply
   logs crashes to `station-apply.log`; the update copy runs off the UI thread.

CHECKPOINT 10a–10c — Claude runs and records:
  · each finding's test red first, then green; full suite green; Release 0 warnings; format clean
  · on the spare: Jim's 9/25 copy round trip again (pack → empty workspace → return → apply → undo),
    plus a tampered return (a `BaseDirectory` on a share; an extra trigger) refused with nothing changed
  · a cold-reader subagent re-reviews 10a–10c; anything it finds is fixed or recorded

---

PROMPT 11 of 13 — Release 0.8.0: docs, draft, and the 0.5.4 → 0.8.0 rehearsal
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
   from the workspace on Franz's PC with `changes.jsonl`); `docs/FEATURE-PARITY.md`. Revision C
   (Q-C8): CLAUDE.md says 0.5.4–0.7.1 are offered updates but cannot install them, the updater
   starts the verified file itself, and a release is a pre-release until rehearsed; an ADR for
   the updater repair; `docs/release.md` gains the pre-release step and `FGSCANNER_APPCAST_URL`.
2. Bump `<Version>` to 0.8.0; give Franz the push and merge commands; after CI, the tag.
3. When the draft is built: run `build/verify-publish.ps1` on it; download, install and start
   it on the spare (CLAUDE.md: install and start a draft before publishing).
4. Rehearse on the spare (Q-C5), reset first: reinstall 0.5.4 and re-copy its data from the 9/25
   original (hash checked). Then: the 0.5.4 sender → verify on Franz's PC → **manual** install of
   0.8.0 as Jim will do it (AC-1) → 0.8.0 opens it (migrates) → in a `--data-dir` workspace on
   Franz's PC set one Box, commit nothing → return pack → apply on the spare → counts and
   settings match. Franz's own station is not touched. Record reading speed on Franz's PC (§15).
5. The updater rehearsal (AC-12, §17 Phase 2): give Franz
   `! gh release edit v0.8.0 --draft=false --prerelease`; confirm
   `releases/latest/download/appcast.xml` still names 0.5.4; on the spare install a local build
   of the same code stamped 0.7.99, set `FGSCANNER_APPCAST_URL` (user scope) to v0.8.0's own
   appcast; Franz starts it → Yes → UAC; the app closes itself; start again → 0.8.0, data
   intact; remove the variable; confirm the Latest appcast still names 0.5.4.
6. Do **not** make 0.8.0 the Latest — that happens in P12 after Jim is running it (Q-C6).

CHECKPOINT 11 — Claude runs and records:
  · CI on the branch green (Franz reports or `gh run list`)
  · `gh release view v0.8.0 --json isDraft,assets` → draft, installer + appcast present; signature verified against 0.5.4's key
  · the rehearsal: sender `OK`, verify `OK` same code, apply `OK`, 0 hash mismatches, Jim's Settings rows unchanged
  · the updater rehearsal: 0.7.99 → 0.8.0 by Yes alone; Latest appcast names 0.5.4 before and after; v0.8.0 `isPrerelease: true`

---

PROMPT 12 of 13 — The first cycle with Jim
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
3. `[Claude]` `station verify` on Franz's PC → `OK nnnn` (same code) = Jim's backup. Copy the
   verified pack to the Tower backup; delete it from Drive and Drive's bin (§13).
4. `[Claude]` download the 0.8.0 installer from the release, check it against `SHA256SUMS`, put
   it in `to-jim\` (Q-C1). `[Jim]` with Franz on "give control": FG Scanner closed →
   double-click the installer → **Yes** on UAC → start FG Scanner → read out the version and
   group count. `[Franz]` then `! gh release edit v0.8.0 --prerelease=false --latest` (Q-C6).
   Delete the installer from `to-jim\` and Drive's bin.
5. `[Claude/Franz]` Process in workspace `Jim` (`--data-dir`) on Franz's PC: auto-orient off there only,
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
| 2026-10-16 | P4 | Green — `StationPacker` + `StationFreezeRecord` (Core) and `fgscanner station pack --to <folder> [--based-on <manifest>] [--kind send\|return] [--station <name>] [--evidence <folder>]`. **Delta design (the P3 open point):** the manifest still lists the station's whole final state; a file the receiver already holds carries `from` (its rel there — the same rel when unchanged, another when renamed) and is not in the pack; verify checks only files without `from` and treats `from` as untrusted as a rel; same rel wins over a rename. DB copied with SQLite's backup API; the manifest names the *packed* DB's hash, the freeze record (`station-freeze.json` + `station-sent-manifest.json` in the station folder) keeps the *live* DB's hash for apply's freeze check. Refuses: app open (single-instance mutex), leftover `-journal` or non-empty `-wal`, unsaved scans in `recovery\` (a bare `.lock` is not one), pending OCR jobs, a destination inside the station or evidence folder, missing destination, free space < pack + 1 GB; the pack re-verifies before `OK`, and a refusal or failure leaves no folder behind. **Added beyond the prompt (a data-loss guard):** pack refuses when a group's folder lies outside the evidence folder — it would not travel and the other side's DB would point at nothing. Facts read from the DB read-only, never migrating it. Keep-awake via `SetThreadExecutionState`. Suite 1,226 (0 failed, 3 skipped), Release 0 warnings, format clean. **Real data (spare, copy of Jim's 9/25 station, self-contained CLI):** first pack 1,412 files + DB, 1.54 GB in 15 s → `station verify` `OK 8ed9`; second pack based on it ships only the DB, 1.4 MB (≤ 10 MB) → `OK bfa5`, same code as the freeze record; live DB hash still `24fff133…` (pack changed nothing). **For P7:** the `·` and `—` in the result line come out as `�` through the SSH console — the shortcut's window must print UTF-8 (set `Console.OutputEncoding` or `chcp 65001`) or Jim reads garbage; and the mutex is per Windows session, so pack from another session (SSH) does not see an open app — fine for Jim's shortcut, worth a line in the runbook. |
| 2026-10-16 | P5 | Green — `StationApplier` (Core), `SqliteStationDatabase` (Data), `fgscanner station apply <pack> [--evidence]` and `station apply --undo`. **Franz decision mid-prompt (spec amendment 723fbda):** the freeze check compares *content*, not the DB file hash — measured on Jim's 9/25 copy, 2.5 h of 0.5.4 plus 0.7.1's migration changed 0 rows but the file hash, so a hash check would have refused every cycle 1 (Jim starts 0.8.0 after sending). Content = SHA-256 over every row of Groups, Documents, Pages, TrashItems, Profiles, IndexSchemas, FieldDefinitions in the *sender's* columns, by `Id`, without Settings / `Profiles.ScanSettingsJson` / `TrashItems.TrashFolderPath`; the return carries it as `basedOnContent` (taken from the send pack's own DB, so the 0.5.4 PowerShell sender needs no SQLite), and the freeze record keeps it for sends based on a return. Apply also re-hashes the receiver's evidence and trash against the manifest it last sent. Gates, all before anything moves: app open, journal/WAL, recovery scans, pack verifies (`NOT YET` exit 3 while arriving), evidence root equal, and — when the station already has a DB — version + last migration equal, `basedOn` = this station's last pack, content and files unchanged; an empty station takes a first (full) pack of any version only into an empty evidence folder. Then: stage DB + new/renamed files beside the station, `BindToStation` (receiver's Settings rows and profile scan settings replace the sender's; trash paths re-rooted — **added: Jim's 9/25 trash row pointed at `C:\Users\fgers\…`**), `integrity_check`, move replaced/deleted files into `apply-backup-<stamp>\` (plan written first), place staged files, swap the DB, re-verify the final state (auto-undo on mismatch), rewrite the freeze record + `station-sent-manifest.json` as the new shared baseline. `--undo` puts files, DB and freeze record back and renames the backup `apply-undone-…`. Suite 1,251 (0 failed, 3 skipped), Release 0 warnings, format clean. **Real data (spare, Jim's 9/25 copy, self-contained CLI):** Jim pack `OK 0b9d` → Franz apply into an empty workspace `OK 0b9d` (1,411 files) → Franz's CLI migrates it, Box set on `_new2`, `Feature.AutoOrient=false` + a Franz-only setting → return based on the send `OK 7235 · 1 files · 1.4 MB` → Jim's CLI migrates his DB (the "0.8.0 start") → apply `OK 7235`: 20 groups / 1,368 documents / 1,368 pages equal to Franz's, Box arrived, Jim's Settings rows exactly his own (AutoOrient still true, Franz's absent), trash path rebound, `integrity_check` ok → `--undo` → every one of 1,424 files byte-identical (only the new `apply-undone-…` record differs) → Jim scans 3 pages, apply → `NOT OK — this station's data has changed since it was sent`, nothing changed. Spare cleaned; `C:\Evidence-Scans` 1,411/1,411 identical to the P1 list. **For P7:** a `| Select -First 1` on the CLI's output kills it early (exit -1) — the shortcut must not truncate output. |
| 2026-10-16 | P5a | Security fixes from the background review of 26d0486 (no decision needed): apply acts on exactly the manifest bytes it verified (a shared folder can change the file between two reads) and hashes the staged DB against `dbSha256` before binding it; an empty station's evidence folder must be typed (`--evidence`), never taken from the pack, which arrives through Drive and would otherwise choose where files land; the trash rebind refuses a last segment that is empty, `.`/`..`, ends in dots or spaces (Windows trims `...` to the trash root itself — a purge there empties the whole trash) or leaves the trash folder, and stores the normalised path; evidence folders are refused when, normalised, they start with `\` (UNC, `\?\`, `\.\`), for pack and apply. The hostile-trash and typed-evidence tests were written alongside the fixes, not watched red first. Commit 11123d8 + this P6 commit. |
| 2026-10-16 | P6 | Green — `StationChangeLog` (`changes.jsonl`: one JSON line per changed cell `{table,key,change:"changed",column,old,new,by,at}`, added/removed rows whole; over the evidence tables and the columns both DBs have, so a migration's new columns and the station-owned values — Settings, scan settings, trash paths — are not logged as Franz's work) and `NeedsJimSheet` (`needs-jim.txt`: each open group, "Which box…" when Box is empty, the importer's own `ImportRefusals` wording with what to look for on the paper, max 20 per group, or "Nothing needs you"). Reports travel at the pack's top level and are hashed in the manifest (`reports[{name,size,sha256}]`, only those two names) — an edited change log fails verify. `station pack --kind return` must be `--based-on` the send it answers (refuses otherwise) and takes `--by`. Suite 1,265 (0 failed, 3 skipped), Release 0 warnings, format clean. **Real data (spare):** Jim send `OK 95de` → Franz workspace → migrate → Box set on Dental → return → `changes.jsonl` exactly 1 row (Groups `BatchFieldsJson`, old/new, "Franz Gerster", time), verify OK; `needs-jim.txt` lists all 20 open groups — on this unprocessed 9/25 data mostly blank-DocNo refusals that Propose clears before a real return. Spare cleaned, evidence 1,411/1,411 identical. |
| 2026-10-16 | P7 | Green — `build/station/Send-ToFranz-0.5.4.ps1` (Windows PowerShell 5.1, ASCII-only file, first pack only: refuses a version other than 0.5.4, the app open, a journal/WAL, unsaved recovery scans, a destination inside the live folders, low space; copies, re-hashes every copy, writes the manifest last, writes `station-sent-manifest.json` + `station-freeze.json`; prints `OK nnnn - leave the laptop on until Google Drive says it's done`). It cannot read SQLite, so the last migration is the 0.5.4 constant `20260914210756_AddFieldLengthAndMemo` (confirmed in Jim's 9/25 DB) and pending reads are not checked (0.5.4 on Jim's laptop reads nothing in the background); hashing uses .NET directly — `Get-FileHash` failed to autoload when 5.1 was started from a .NET 10 process. `StationSenderTests` (Windows PowerShell 5.1.26100): **the sender's manifest is byte-identical to `StationPacker`'s** for a fixture with an accented folder (`Zoë`), an apostrophe-and-comma folder, a hidden system file, originals, sidecars, index and trash; it verifies with the same code; refusals leave nothing; all three scripts parse in 5.1. Shortcut scripts `Send-ToFranz.ps1` (pack `--kind send --station jim` into `<drive>\to-franz`, `--based-on` the station's `station-sent-manifest.json` when there is one) and `Install-FranzUpdate.ps1` (apply the newest `*-return` in `<drive>\to-jim`, `NOT YET` exit 3 when none) set the console to UTF-8 and wait for a key unless there is no console. Installer: unchecked task "This is Jim's station", the two scripts into `{app}\station`, two desktop shortcuts (powershell.exe -File …, icon from the app), and a wizard page for the shared folder (default `G:\My Drive\FGScanner-Jim`, `/DriveFolder=` for a silent first install, kept across silent updates via previous data) written to `{app}\station\drive-folder.txt`. Suite 1,272 (0 failed, 3 skipped), Release 0 warnings, format clean. **ISCC compiled; installed on the spare** (the prompt assumed no scratch machine — the spare is one): silent install with the task → both `.lnk` on the public desktop pointing at the scripts, `drive-folder.txt` = the folder passed; with the current CLI in `{app}\cli`: "Install Franz's update" → `NOT YET`, exit 3; "Send my scans to Franz" → `OK 8f21 · 1,413 files · 1.54 GB` with the `·` rendered (UTF-8 fix works). Then uninstalled, official 0.7.1 reinstalled (sha 6F615A2C…), freeze files removed from the rehearsal data. **Finding:** the first install attempt rolled back (exit 5) because yesterday's 0.7.1 (pid 9396, Franz's desktop session, never logged "exiting") held the DLLs and Inno's CloseApplications cannot close an app in another Windows session; ended it by hand. For P8b: the repaired updater runs the installer from inside the app's own session, so this does not apply to it, but a stuck instance would still block an update — worth a line in the runbook. |
| 2026-10-16 | P8 | Green — `MigrationLadderTests` with `fixtures/ladder/v0.5.4.db`, `v0.6.0.db`, `v0.7.1.db`, each written by that tag's own CLI (`git -c core.longpaths=true worktree add` — a plain worktree under the scratch path failed on long snapshot file names — then `fgscanner --fake --db … scan` into two groups, 6 pages). Each rung migrates through `DbBootstrapper.MigrateWithBackup` with no migration left pending, a `.bak` exactly when one was pending (only 0.5.4: `20260914210756_AddFieldLengthAndMemo` → current), `integrity_check` ok, every group and page id, name/file name, path and checksum unchanged, and opens through EF (2 groups, 6 pages); a second test pins that the ladder starts at v0.5.4. Red first: no fixtures → "No data found" + the v0.5.4 assertion. `docs/release.md` gained "The migration ladder — every release adds a rung" with the commands. The rungs are in WAL mode (the CLI's), unlike Jim's `delete`-mode database; the existing `MigrationFixtureTests` (v1/v2) are untouched — the ladder sits in a subfolder they do not enumerate. Suite 1,276 (0 failed, 3 skipped), Release 0 warnings, format clean. |
| 2026-10-16 | P8b | Green — `UpdateInstaller` (App): after NetSparkle's strict download check (`DownloadFinished` = "fully downloaded and verified"), waits while `ScanViewModel.CaptureInHand` (checked every 5 s — the ~150 MB download can finish mid-sheet), copies the download atomically to `%TEMP%\FGScanner\update\fgscanner-<ver>-win-x64.exe` (version reduced to letters, digits, `.`, `-`; never `..`), starts it with `UseShellExecute` (Windows raises UAC) and `/VERYSILENT /NORESTART` — no `cmd`, no script — then closes the main window and shuts down; a refused prompt (Win32 1223) is logged, the copy removed, the app stays open; any other start failure is logged by `UpdateService` and offered again next launch. NetSparkle's `InstallUpdate` is no longer called (one comment names it). `FGSCANNER_APPCAST_URL` is honoured only for `https://github.com/fgerster1/fgScanner/releases/download/…` with no `..` (else the Latest appcast, logged when used); signatures unchanged. `release.yml` writes `releases/download/v<version>/` into the appcast so a pre-release points at its own installer. `UpdateInstallerTests` 12 (red first: type missing). Suite 1,288 (0 failed, 3 skipped), Release 0 warnings, format clean. **Real exe:** a throwaway harness (scratch, not committed) ran the real `UpdateInstaller` with `Process.Start` on a console exe saved as `…\Franz Gerster\Temp\39362164-4dc1-42be-adf4-f0cc859a0ff0` (no extension, as NetSparkle saves it) → started by the shell as `…\Franz Gerster\update\fgscanner-0.0.99-win-x64.exe` with `/VERYSILENT /NORESTART`, close requested, `InstallAsync` true — the exact path that failed on 0.5.4 and 0.7.1. (A first attempt hit Win32 193 because the dummy was compiled as a DLL; the harness then showed a non-1223 start failure propagates to `UpdateService`'s catch, which logs it.) Inno's `CloseApplications` remains the backstop; the end-to-end proof is AC-12 in P11. |
| 2026-10-16 | 10a | Green — apply and undo never strand a station. The undo plan (now with the station files that existed) is written before anything is copied or moved; Commit, the re-verify and the freeze-record write are wrapped: an `IOException`/access/verify failure restores from the backup, renames it `apply-undone-…`, removes staging and prints `NOT OK — the update could not be installed (…), so the station has been put back` (P9-2). Restore deletes a placed file only when its original is in the backup or it replaced nothing, and never deletes a station file that existed but was not yet copied (P9-1). Undo: same rest gates as apply, refuses when the live content or files differ from the freeze record apply wrote ("changed since the update was installed"), keeps the replaced DB as `fgscanner.replaced.db`, restores station files by temp + move, skips a backup with no plan (P9-3). Apply checks free space (update + 1 GB) on the station and evidence drives (P10-6). D5: `station pack`/`apply` run inside `HoldingTheStation`, which takes FG Scanner's own `FGScanner.SingleInstance` lock or prints `NOT OK — FG Scanner is open`; the 0.5.4 sender takes the same lock; the send shortcut says not to open FG Scanner. Tests: a file held open mid-commit → station byte-identical; the P9-1 case (the changed file held open) → original intact; undo refused after Jim works / not at rest; undo keeps the replaced DB; a plan-less backup skipped; no room → refused; CLI pack and the 0.5.4 sender refused while the test holds the lock. The seven applier tests went green on first run (the build fails on an unused seam, so they were not watched red); the scenarios are the reviewers' own. Suite green (below). |
| 2026-10-16 | 10b (1/2) | Green — the incoming database is untrusted. D1: binding into an empty station keeps no Settings at all (the sender's include `Hook.CommandLine`); a receiver's own Settings and scan settings are read on a separate read-only connection and copied in by named columns (P9-6) — ATTACH with `?mode=ro` is unavailable (this SQLite has no URI support), so no ATTACH. P10-2/P9-10: after binding, `Profiles.BaseDirectory` (may equal the root), `Groups.DirectoryPath` and `TrashItems.GroupDirectoryPath` must be inside the evidence root and not on a share, else NOT OK naming `Table.Column = value`. P10-3: the freeze check compares the receiver's *own* content — the freeze record's, or for a 0.5.4 send the fingerprint of `station-sent.db`, the sent database that sender now keeps — and requires the pack's `basedOnContent` to equal it. D2 (refined): integrity before binding; tables and indexes must equal the receiver's (or a fresh database migrated to the pack's `lastMigration`; an unknown version refuses); triggers and views are dropped and the build's own `RawSchemaSql` ones created; every connection runs `PRAGMA trusted_schema = OFF`. P9-6: apply passes the receiver DB's own last migration and refuses "FG Scanner has not been started since it was updated — start it once". P10-7: table/column names reaching SQL must match `^[A-Za-z_][A-Za-z0-9_]*$`. Tests (Core, text fake): a forged `basedOnContent` subset refused; a 0.5.4-style freeze checked through `station-sent.db` (passes, and refuses after a new scan); never-started version; a share path; an extra table. Tests (Data, real SQLite): empty station drops Settings incl. the hook; share/Startup/elsewhere paths named per column; extra table and dropped index refused; the v0.5.4 ladder rung matches a fresh DB at its own migration and not at a later one; unknown version refused; a smuggled trigger and a lying `v_index` replaced by the build's own; a non-identifier table name refused. The sender test asserts `station-sent.db` = the sent DB. Suite 1,311 (0 failed, 3 skipped), Release 0 warnings, format clean. |
| 2026-10-16 | 10b (2/2) | Green. P10-8: one enumerator (`StationManifestBuilder.FilesUnder`) refuses any junction or symlink (`LinkTarget` set) in a station, an evidence folder or a pack — pack refuses with nothing written, verify is NOT OK, apply refuses (tests make real `mklink /J` junctions; their cleanup unlinks first). P10-10: a manifest with a null `files`, `rel`, `sha256`, `kind`, `dbSha256`, `evidenceRoot` or report field is "manifest unreadable", never an exception; `HoldingTheStation` and `verify` turn IO/access/data/SQLite/JSON/invalid-operation errors into `NOT OK — …`. P10-11: a mapped or SUBSTed network drive letter is not local (no network drive here to test it; the check is one `DriveType` line). D3: the code is 8 hex characters `xxxx-xxxx` in the verb, verify and the 0.5.4 sender (still byte-identical, test green); "Install Franz's update" verifies first, shows the code, asks "Is it xxxx-xxxx? Type Y …" and changes nothing on any other answer or without a console. D7: more than one `*-return` waiting → `NOT OK - more than one update is waiting - call Franz` (tested). Also from the background security review of 48c1809: the schema comparison excluded `name NOT LIKE 'sqlite_%'`, where `_` is a wildcard, so a table named `sqliteX…` slipped past; now the literal, case-folded prefix `sqlite_` (red first, then green). Suite green (below). |
| 2026-10-16 | 10c | Green. P9-8 + D4: `UpdateInstaller` copies the verified download first, then waits out a capture in hand, then checks the exact copy against the appcast's Ed25519 signature (`SignatureVerifier.VerifySignatureOfFile(latest.DownloadSignature, copy)`; a missing signature counts as invalid) and only then starts it — a copy that fails is deleted, nothing starts, the app stays open. Tests: the order copy → wait → verify → start; a failing signature never starts; the update folder now has a space in it (test gap). P9-9: `StationChangeLog.DiffSinceReceived` migrates a temp copy of the received DB to this build first, so a value Franz sets in a column a migration added is logged and the migration's own fill-in is not; the temp copy and its backup are removed (red first: method missing). D6: "Send my scans to Franz" passes `--based-on` only when the freeze record holds content; after a refused cycle-1 return (0.5.4 freeze record, `"content": null`) it sends everything and says so (pattern checked in Windows PowerShell; the script's CLI call is not unit-tested). Suite 1,320 (0 failed, 3 skipped), Release 0 warnings, format clean. |
| 2026-10-16 | Checkpoint 10a–10c (spare) | Real data, Jim's 9/25 copy, self-contained CLI from 692736e: Jim send `OK c0a6-e170` (8-character code) → Franz's empty workspace with a typed `--evidence` `OK c0a6-e170`, **0 Settings taken from Jim's send** (D1) → Franz migrates, sets a Box and `Feature.AutoOrient=false` → return. Three tampered copies of that return, each with its manifest's `dbSha256` rewritten to match (what an attacker with the folder could do): `Profiles.BaseDirectory` on a share → `NOT OK — the update names a folder outside this station's evidence folder`, nothing changed; an extra table → `NOT OK — … has a table this version does not: Smuggled`, nothing changed; a smuggled `AFTER INSERT ON Pages` trigger → `OK f5c6-b1ce`, and the applied database holds only the three built-in FTS triggers, Franz's Box, Jim's own Settings exactly, 20 groups / 1,368 pages, `integrity_check` ok, and Jim's stale `v_index` brought up to date → `--undo` → every file byte-identical. Spare cleaned; evidence 1,411/1,411 identical. |
| 2026-10-16 | 10d | Green — the re-review's follow-ups. R-1: `station apply --expect-code`, passed by "Install Franz's update" with the code Jim confirmed; a pack whose code differs is refused before anything changes. R-2/R-3: every exception after Commit (not only IO) restores; the restore marks itself started (`restore-started`), goes on past a file it cannot move, reports what is stuck ("could not be fully put back … `station apply --undo` finishes putting it back") and clears the mark when whole; undo finishes a marked restore without the "changed since" gate. R-4: the undo plan records its pack and undo takes back only the apply the freeze record names — an older backup is never a second undo; so once Franz has packed a return, undo of the workspace's apply says "nothing to undo" (the CLI test was reordered to undo first). R-5: when the station and the evidence are on different drives, the originals that will be moved aside count against the station drive. R-6: a crash during apply/undo is appended to `station-apply.log`. R-7: tests for the 0.5.4 sender's real lock path (takes it, lets it go) and for D6 through a stand-in `fgscanner.exe` that records its arguments (`--based-on` only when the freeze record holds content; "Sending everything again" otherwise); still untested: the Y/N prompt (needs a console) and a failure injected during placing or the DB move. R-8: the 150 MB update copy runs on a worker thread. R-9: kept as decided (D5 a). Suite green (below). |
