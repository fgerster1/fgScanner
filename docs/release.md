# Release runbook

## Cutting a release

1. `dotnet test -c Release` green locally; CI green on main.
2. Bump notable docs (FEATURE-PARITY.md), then tag: `git tag v0.1.0 && git push origin v0.1.0`.
3. `release.yml` publishes the app, then the CLI into `cli\`, checks the payload
   (`build/verify-publish.ps1`), builds the portable ZIP and Inno
   installer, writes SHA256SUMS, attests provenance, and drafts a GitHub
   Release. **Before publishing, install the draft's installer and start it** —
   the local check below never publishes the CLI, so it cannot catch what only the
   pipeline does. 0.5.2 passed every local check and shipped with the CLI written over
   the app. Publishing is what puts it in front of every station's auto-update.
4. Publishing the release triggers `winget.yml`, which is skipped unless
   `WINGET_ENABLED=true` (see winget below).

Local installer check (PowerShell, from repo root):

```powershell
dotnet publish src/FgScanner.App -p:PublishProfile=win-x64
$iscc = Get-ChildItem "${env:ProgramFiles(x86)}\Inno Setup*","$env:ProgramFiles\Inno Setup*",
  "$env:LOCALAPPDATA\Programs\Inno Setup*" -Filter ISCC.exe -Recurse -EA SilentlyContinue |
  Sort-Object FullName -Descending | Select-Object -First 1
& $iscc.FullName /DAppVersion=0.1.0 build\installer\setup.iss   # → dist\
```

`winget install JRSoftware.InnoSetup.7` installs **per-user** under
`%LOCALAPPDATA%\Programs` when run unelevated — hence the wider search above.
CI installs it machine-wide via `choco install innosetup`, so `release.yml`
only searches the two Program Files roots.

## Secrets and variables (GitHub → Settings)

| Name | Kind | Purpose |
|---|---|---|
| `SIGNPATH_API_TOKEN` | secret | SignPath Foundation API token (code signing) |
| `SIGNPATH_ORG_ID` | variable | SignPath organization id |
| `SIGNPATH_ENABLED` | variable | `true` enables both signing steps |
| `SPARKLE_ED25519_PRIVATE_KEY` | secret | signs the auto-update appcast |
| `APPCAST_ENABLED` | variable | `true` enables appcast generation |
| `WINGET_TOKEN` | secret | PAT with `public_repo` for winget-releaser |
| `WINGET_ENABLED` | variable | `true` enables `winget.yml` (unset: the job is skipped) |

## Code signing (SignPath Foundation)

Apply at <https://signpath.org/apply> (free for OSS; requires the install-time
privacy policy + AI opt-out we ship, and OSI licenses throughout — see
THIRD-PARTY-NOTICES.md). Create project `fgScanner` with signing policy
`release-signing` and two artifact configurations: `publish-payload`
(exe/dll set before packing) and `installer` (the setup exe). Then set the
three SignPath entries above. Until then releases are unsigned and SmartScreen
warns — documented in README.

## Auto-update keys (one-time)

```
dotnet tool install --global NetSparkleUpdater.Tools.AppCastGenerator
netsparkle-generate-appcast --generate-keys
```

- Put the **private** key into the `SPARKLE_ED25519_PRIVATE_KEY` secret
  (never in the repo).
- Put the **public** key into `UpdateService.Ed25519PublicKey`
  (src/FgScanner.App/Services/UpdateService.cs) and commit. Until that
  constant is replaced, the app skips update checks entirely — it never
  accepts an unsigned appcast.
- Set `APPCAST_ENABLED=true`. The appcast uploads with each release and is
  fetched from `releases/latest/download/appcast.xml`.

## winget

**Off.** FG Scanner is not in winget-pkgs yet, and `winget.yml` is skipped
until `WINGET_ENABLED=true`. The action can only update a package that
already exists there, so 0.5.2 and 0.5.3 each showed a failed Winget run
that meant nothing. The stations don't need winget: they update from the
appcast.

To turn it on, in this order:

1. First submission by hand: `wingetcreate new` with the release's installer
   URL, identifier `FranzGerster.FGScanner`. This opens a public PR to
   microsoft/winget-pkgs; wait for it to merge.
2. Add the `WINGET_TOKEN` secret (classic PAT, `public_repo` scope).
3. Set `WINGET_ENABLED=true`. From the next published release on,
   `winget.yml` submits each version update itself.

## Upgrade safety

Migrations run at startup with an automatic pre-migration database backup
(`fgscanner.db.bak-<version>`); versioned fixture databases in
tests/FgScanner.Data.Tests/fixtures prove old databases upgrade cleanly.
The installer's `[InstallDelete]` purges stale binaries, and per-user data
under %APPDATA% is never touched by install or uninstall.
