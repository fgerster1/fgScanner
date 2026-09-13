# Project brief — FG Scanner (FgmakerScanner)

> Cached reconnaissance for the fg-programming-specs skill.
> Written from commit `605ce9d` on 2026-09-13. Refresh when stale — see the skill's
> references/project-brief.md.
> **This file never covers regression risk.** That is read fresh per spec.

## Stack
- .NET 10 (SDK 10.0.401), WPF with `ThemeMode="System"` Fluent, CommunityToolkit MVVM, DI via
  Microsoft.Extensions.Hosting.
- NAPS2.Sdk 1.3.0 (LGPL), EF Core 10 + SQLite, Tesseract 5.5 shell-out, PDFsharp, Google.GenAI,
  CsvHelper + ClosedXML, Serilog. Inno Setup 7 installer.
- Central package management (`Directory.Packages.props`); version only in `Directory.Build.props`
  `<Version>` (0.4.0).
- Non-negotiables live in `CLAUDE.md` (licensing guards, evidence contract, test rules). Read it;
  this brief does not restate it.

## Layout
- `src/FgScanner.App` — WPF. Views and their view models sit together in `Views/` (e.g.
  `ScanView.xaml` + `ScanViewModel.cs`); dialogs in `Views/Dialogs/`; services in `Services/`.
- `src/FgScanner.Core` — domain, no reference to Data (Quick Scan relies on that wall).
- `src/FgScanner.Scanning` — `IScanService`, NAPS2 adapter, recovery session, editing, export.
- `src/FgScanner.Data` — EF entities (`Entities.cs`), services (`ProfileService`, `GroupService`,
  `IndexingService`, `TrashService`, `AppSettingsService`), migrations.
- `src/FgScanner.Ocr`, `src/FgScanner.Ai`, `src/FgScanner.Cli`.
- Docs: `docs/PLAN.md`, `docs/FEATURE-PARITY.md`, `docs/manual-tests.md`, `docs/adr/` (0001–0005;
  0006–0008 reserved by the Quick Scan plan), `docs/superpowers/plans|research/`, `docs/specs/`.
- Conventions: comments explain why; WPF-free logic classes beside views for anything numeric
  (`ZoomController`, `PageNavigator`); UI strings inline English (ADR-0001).

## Testing
- xunit.v3 in MTP mode (opt-in in `global.json`), NSubstitute, AwesomeAssertions, Verify.
- `dotnet test -c Release` — baseline **516 passed** (measured in Debug on 2026-09-13).
- **Quirk:** if FG Scanner is running from `src\FgScanner.App\bin\Release`, the Release build fails
  with MSB3027 file locks. Close the app, or run Debug.
- No UI automation (FlaUI is listed in CLAUDE.md but not referenced). App tests exercise view
  models directly. GUI behaviour is covered only by `docs/manual-tests.md`.

## Data
- SQLite via EF Core; migrations in `src/FgScanner.Data/Migrations/` (latest
  `20260828162943_AddFieldScopeAndGroupBatchFields`). Startup migrates automatically after writing
  `fgscanner.db.bak-<version>`.
- Custom values are JSON in TEXT columns (`Document.CustomFieldsJson`, `Group.BatchFieldsJson`).
- Key-value app settings in the `Settings` table via `AppSettingsService`.

## Deployment
- No release has ever been published (no tags, no GitHub releases as of 2026-09-13). Installers are
  built locally with Inno Setup (`build/installer/setup.iss`, command in CLAUDE.md) and carried to
  the scanning station on USB (H:).
- Rollback on the station: reinstall the previous installer; restore `fgscanner.db.bak-<version>`.

## Design system
- The app's own WPF Fluent theme. The FG Maker web design kits (Organic, Workspace) do not apply to
  this WPF app.

## Security
- No auth; desktop app. Gemini API key in Windows Credential Manager (`CredentialStore`).
- Never logged: API keys. Evidence folders' contents are case material.

---
*Written 2026-09-13 from `605ce9d` alongside SPEC-2026-001..003.*
