# Release runbook

## Cutting a release

1. `dotnet test -c Release` green locally; CI green on main.
2. Bump notable docs (FEATURE-PARITY.md), then tag: `git tag v0.1.0 && git push origin v0.1.0`.
3. `release.yml` publishes the app + CLI, builds the portable ZIP and Inno
   installer, writes SHA256SUMS, attests provenance, and drafts a GitHub
   Release. Review the draft, edit notes, publish.
4. Publishing the release triggers `winget.yml` (needs `WINGET_TOKEN`).

Local installer check: `dotnet publish src/FgScanner.App -p:PublishProfile=win-x64`
then `ISCC.exe /DAppVersion=0.1.0 build\installer\setup.iss` → `dist/`.

## Secrets and variables (GitHub → Settings)

| Name | Kind | Purpose |
|---|---|---|
| `SIGNPATH_API_TOKEN` | secret | SignPath Foundation API token (code signing) |
| `SIGNPATH_ORG_ID` | variable | SignPath organization id |
| `SIGNPATH_ENABLED` | variable | `true` enables both signing steps |
| `SPARKLE_ED25519_PRIVATE_KEY` | secret | signs the auto-update appcast |
| `APPCAST_ENABLED` | variable | `true` enables appcast generation |
| `WINGET_TOKEN` | secret | PAT with `public_repo` for winget-releaser |

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

First submission is manual (`wingetcreate new` with the release URL,
identifier `FranzGerster.FGScanner`); after the package exists,
`winget.yml` auto-submits version updates on every published release.

## Upgrade safety

Migrations run at startup with an automatic pre-migration database backup
(`fgscanner.db.bak-<version>`); versioned fixture databases in
tests/FgScanner.Data.Tests/fixtures prove old databases upgrade cleanly.
The installer's `[InstallDelete]` purges stale binaries, and per-user data
under %APPDATA% is never touched by install or uninstall.
