# SPEC-SCANNER-2026-011 — Prompt pack

Prompts for [SPEC-SCANNER-2026-011-station-backup.md](./SPEC-SCANNER-2026-011-station-backup.md). Claude runs them in order, continuously: each
checkpoint is run and logged by Claude, which goes straight on unless the prompt's
`Franz stop:` line says otherwise (fg-programming-specs `references/running-the-pack.md`).

Approved 2026-10-17 in Round A (https://claude.ai/artifact/Qdunwnh6FxJBVM984KjiNj), every
recommendation taken: keep 3, button in the left column, Last backup + update offer, backup
offered not required, test copy relocated and marked TEST COPY, capture allowed there, no
encryption. Not tracked in Linear (FG Scanner work stays out of it).

**Rules for every prompt** (CLAUDE.md, and what earlier specs learned):

- Tests first: write the named test, watch it fail for the right reason, then make it pass.
- CI's commands must pass: `dotnet build -c Release` (warnings are errors),
  `dotnet test -c Release` (MTP mode), `dotnet format --verify-no-changes`.
- Work on branch `spec-scanner-011-usb-backup`. Claude pushes, opens the PR, merges once CI is
  green (the merge needs Franz's word in the turn — the auto-mode classifier blocks it
  otherwise), tags and publishes a **pre-release**. Making a release the **Latest** waits for
  Franz, at a cycle's return.
- **Never touch Franz's or Jim's real data.** Tests use temp folders; rehearsals use the spare's
  copy of Jim's 9/25 station only.
- **A backup never writes or changes the freeze record or the sent manifest** — that is the
  round trip's baseline (SPEC-SCANNER-2026-010). Send/return behaviour must not change: every
  existing station test stays green untouched.
- No schema change, no evidence-contract change; `capturedBy` is never rewritten.
- All writes atomic; dates ISO-8601; invariant culture. Station scripts ASCII, PowerShell 5.1.
- Never clear an `ObservableCollection` bound to a `Selector.SelectedItem` (CLAUDE.md).
- Comments explain why. No feature the spec does not name.
- Re-runnable: each prompt says what "already done" looks like.
- Continuous: run the checkpoint; when green, commit (`SPEC-SCANNER-2026-011 P<n>: …`), add a
  Run log line, go on.

**Run order — Franz stops:** P7/P8 only if a finding needs a decision · P9 (plug a USB stick into
the spare and click UAC — physical; merge — Franz's word). P1–P6 run straight through.

---

PROMPT 1 of 9 — The `backup` pack kind
Spec: §03 item 1, §08 Core, §10 AC-1, AC-2    Phase: 1    Depends on: —
Franz stop: none

Already done if: `StationBackupTests` has "a backup never moves the round trip's baseline" green.

Do only this:
1. Failing tests in `tests/FgScanner.Core.Tests/Station/StationBackupTests.cs`:
   - a `backup` pack verifies and holds the DB plus every evidence and trash file;
   - the freeze record and sent manifest are byte-identical before and after (and absent stays absent);
   - a backup refuses `BasedOnManifest`;
   - a backup between a send and its return leaves the return applicable (AC-2).
2. In `StationPacker`: kind `backup` refuses a based-on manifest and skips
   `StationFreezeRecord.Write`. Nothing else changes.

Do NOT: touch retention, the CLI or the app.
Checkpoint: the new tests and every existing `Station*` test green.

---

PROMPT 2 of 9 — Retention, Last backup record, and `station backup`
Spec: §03 items 2–3, §07, §10 AC-3, AC-10    Phase: 1    Depends on: P1
Franz stop: none

Already done if: `fgscanner station backup --to <tmp>` prints `OK xxxx-xxxx` in the CLI tests.

Do only this:
1. Failing tests: with keep 3 and four backups the oldest goes only after the fourth verifies; a
   failed fourth deletes nothing; a stranded folder with no manifest older than the newest
   verified one is removed, a newer one is left; a FAT32 target with a ≥ 4 GB file refuses
   (simulated); `last-backup.json` written atomically after verification.
2. `StationBackup` (Core) + `LastBackupRecord`.
3. CLI verb `station backup --to <folder> [--keep N] [--evidence]`, holding the station lock
   like pack; `StationBackupVerbTests`.

Do NOT: change `station pack`'s kinds or output.
Checkpoint: tests green; the verb run against a temp station prints OK and the code `station verify` prints.

---

PROMPT 3 of 9 — WorkGate and BackupService
Spec: §08 App, §10 AC-4, AC-5    Phase: 2    Depends on: P2
Franz stop: none

Already done if: `WorkGateTests` and `BackupServiceTests` exist and are green.

Do only this:
1. Failing tests: no OCR/AI job is claimed while the gate is held; the running job finishes;
   held jobs run after release. BackupService refuses with a capture in hand, with unsaved Scan
   pages, with recovery files, with a missing target — each with a plain reason; on success it
   returns the code and writes `last-backup.json`.
2. `WorkGate` (App/Services) checked by `OcrWorker` and `AiWorker` before claiming a job.
3. `BackupService`: at-rest checks against the running app, hold the gate, keep awake,
   `DbBootstrapper.BackupDatabase`, `StationBackup` on a background thread, release.

Do NOT: build UI.
Checkpoint: tests green.

---

PROMPT 4 of 9 — Back up button, window, Last backup, update offer
Spec: §03 items 4–5, §08 App, §09, §10 AC-8, AC-9; Q2, Q3, Q4    Phase: 2    Depends on: P3
Franz stop: none

Already done if: the left column shows Back up and "Last backup: …" on the real window.

Do only this:
1. Failing tests: `ShellTests` "Last backup: Never" on a fresh station and the time after one;
   `UpdateOfferTests` — the offer text names the last backup and Back up first runs a backup
   before the install, Install now skips it, Later does nothing.
2. `BackupWindow` (Views/Dialogs): removable and non-system fixed drives listed, Browse, last
   folder remembered (`Backup.Folder`), progress, result `OK xxxx-xxxx` / `NOT OK — …`; blocks
   the app while running; Esc closes only when idle.
3. Left column: Back up button above About, Last backup line.
4. `UpdateService` offer: three choices; `UpdateInstaller` unchanged.
5. Check on the real window with a throwaway `--data-dir` (UI Automation).

Do NOT: add a Restore button or automatic backups.
Checkpoint: tests green; the window walked once with a temp target folder.

---

PROMPT 5 of 9 — Test copy
Spec: §03 items 6–7, §08 TestCopyOpener, §13, §10 AC-7; Q5, Q6    Phase: 3    Depends on: P4
Franz stop: none

Already done if: `TestCopyTests` green.

Do only this:
1. Failing tests: `station apply <backup> --data-dir <ws> --test-copy --evidence <new root>`
   into an empty workspace puts every file under the new root; every stored absolute path
   (`Group.DirectoryPath`, trash `GroupDirectoryPath`, `TrashFolderPath`, `Profile.BaseDirectory`)
   is under it; a stored path outside the manifest's evidence root refuses; `test-copy.json`
   written; `station pack` in that workspace refuses; the shell title says TEST COPY; a second
   workspace's files and DB are byte-identical before and after.
2. `TestCopyOpener` (CLI side, after the ordinary apply into a staging root) and the marker.
3. BackupWindow: "Send a test copy to Franz" → `<drive folder>\to-franz\copies`.

Do NOT: change `StationApplier.GateProblem`.
Checkpoint: tests green.

---

PROMPT 6 of 9 — Documentation
Spec: §18    Phase: 3    Depends on: P5
Franz stop: none

Do only this: CLAUDE.md rule; `docs/station-round-trip.md` (Back up per cycle, restore §17, test
copy); `docs/adr/0019-station-backup.md`; `docs/user-guide.md`; `docs/FEATURE-PARITY.md`.
Checkpoint: links resolve; format/build green.

---

PROMPT 7 of 9 — Code review
Spec: §21    Phase: 3    Depends on: P6
Franz stop: only if a finding needs a decision the spec didn't make

Do only this: `/code-review max` over `main..HEAD` in a cold subagent; findings through
fg-fix-findings (Revision B).

---

PROMPT 8 of 9 — Security review of the test-copy path
Spec: §13    Phase: 3    Depends on: P7
Franz stop: as P7

Do only this: a cold subagent reviews `TestCopyOpener`, the path rewrite and the backup target
handling as untrusted input (crafted manifest, paths, junctions); findings through fg-fix-findings.

---

PROMPT 9 of 9 — Release and rehearsal on the spare
Spec: §10 AC-6, AC-8, AC-10, §17    Phase: 3    Depends on: P8
Franz stop: Physical — a USB stick in the spare; UAC on the spare. Merge — Franz's word.

Do only this:
1. Bump `<Version>` (0.8.2), PR, merge after CI, tag, verify the draft (verify-publish, Ed25519),
   publish as **pre-release**.
2. On the spare (Jim-like 0.8.1 state): update to 0.8.2 through the offer with Back up first
   (AC-8); back up to the stick; pull the stick mid-backup once (AC-10); restore the newest
   backup into an empty station and compare counts and hashes (AC-6).
3. Send a test copy to the spare's Drive folder and open it on Franz's PC (AC-7 manual).
4. Ladder rung for 0.8.2.

Checkpoint: every AC green in the Run log; the Latest still 0.5.4 (or whatever P12 made it).

---

## Run log

| Prompt | Date | Result |
|---|---|---|
