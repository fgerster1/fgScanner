# ADR-0018 — The updater starts the verified installer itself; releases are rehearsed as pre-releases

Date: 2026-10-16 · Status: Accepted · Decided by: Franz · Spec: SPEC-SCANNER-2026-010 Revision C (Q-C2, Q-C3), Revision D (D4)

## Context

The 2026-10-15 rehearsal showed that no published release had ever been installed by
auto-update. On 0.5.4 and 0.7.1, pressing Yes downloaded and verified the installer, then
NetSparkle 3.1.0's generated `.cmd` waited 90 seconds for an app nothing closed and ran the
download — saved with no extension under `C:\Users\Franz Gerster\…` — by an unquoted path, which
cmd split at the space. Jim's laptop account has the same name.

## Decisions

1. **NetSparkle keeps the check, the download and the Ed25519 verification; FG Scanner runs the
   install.** `UpdateInstaller` copies the verified download to
   `%TEMP%\FGScanner\update\fgscanner-<ver>-win-x64.exe`, waits while a capture is in hand,
   verifies that exact copy's signature again, starts it directly (shell execute, so Windows
   raises UAC) with `/VERYSILENT /NORESTART`, then closes the app. A refused UAC prompt leaves
   the app open. `InstallUpdate` and its script are never used.
2. **The appcast's download link names its release** (`releases/download/v<ver>/`), so a
   pre-release's appcast points at its own installer.
3. **Every release is first published as a pre-release.** `releases/latest` never serves one,
   so no station is offered it; a rehearsal station is pointed at it with
   `FGSCANNER_APPCAST_URL`, which is honoured only for this repository's `releases/download/…`
   links over https. A release becomes the Latest only at a cycle's return.

## Consequences

- Jim's first move to 0.8.1 is a guided manual install; every later release can arrive by
  itself.
- A stuck FG Scanner in another Windows session still blocks an install (Inno's
  `CloseApplications` cannot reach it) — seen on the spare 2026-10-16.
