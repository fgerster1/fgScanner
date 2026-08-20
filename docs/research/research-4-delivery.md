# Research 4 — Delivery & engineering infrastructure (COMPLETE, 2026-08-19)

## Executive decisions
- **Installer: Inno Setup 7.x** primary .exe (license = free, no revenue gate; NAPS2 does exactly this) + WiX v7 MSI later only for GPO/enterprise. Per-machine install needed for HKLM shell integration (WIA AutoPlay handler, StillImage scan-button registration, OpenWithProgids — NAPS2's [Registry] section is the template). Inno built-in DownloadTemporaryFile w/ SHA-256 for .NET runtime chaining if ever framework-dependent. winget knows InstallerType: inno.
- Skip: NSIS (no advantage), MSIX-as-primary (cert friction, ms-appinstaller disabled since Dec 2023, container breaks TWAIN/registry), ClickOnce (no HKLM/associations, background update gone on .NET 5+), WinApp CLI (preview).
- **Publish: self-contained + ReadyToRun + win-x64 RID + NOT single-file + no trimming** (WPF/WinForms can't trim/AOT — SDK disables it). Single-file extracts native DLLs to %TEMP% → AV heuristics + slow first run; pointless when shipping installer. Framework-dependent needs Microsoft.WindowsDesktop.App detection — skip that burden.
- **Auto-update:** per-machine Inno → **NetSparkleUpdater** (MIT, active, WinForms/WPF/Avalonia UI, Ed25519-signed appcast); if per-user acceptable → Velopack 1.2.0 (MIT, deltas, but %LocalAppData% = no HKLM shell integration). Squirrel/Clowd both dead. Recommend: keep Inno per-machine + NetSparkle.
- **Code signing 2026:** CA/B 6.2.7.4.2 (June 2023): all OV certs need FIPS hardware/HSM. Cert validity now ~15 months max. **EV no longer bypasses SmartScreen** (MS doc explicit — don't buy EV). Options: **SignPath Foundation free for OSS** (best; requires OSI license w/o commercial dual-licensed components — watch ImageSharp/QuestPDF; REQUIRES privacy policy shown at install + opt-out for Gemini data transfer!); **Azure Artifact Signing** (ex-Trusted Signing, GA Jan 2026, individuals OK US/CA, $9.99/mo Basic, no EV); Certum OSS ~$29-50/yr. Unsigned = "Windows protected your PC" forever, reputation never carries between unsigned builds.
- **Data store: EF Core 10 + Microsoft.Data.Sqlite (10.0.11)**, migrations at startup w/ backup-before-migrate, **user-defined fields in JSONB column** (SQLite 3.45+ jsonb(); promote hot fields via stored generated column + index), **FTS5** external-content table + triggers via raw-SQL migration for OCR text search. Explicitly ref SQLitePCLRaw.bundle_e_sqlite3 3.0.5 for current SQLite 3.53.4. NOTE: SQLCipher/encrypted builds no longer free in SQLitePCLRaw 3.x. Avoid System.Data.SQLite, LiteDB (v6 3-yr prerelease, 755 issues), Evolve. EF Core 10 breaking change: Sqlite DateTimeOffset read behavior — relevant for scan timestamps.

## Testing (copy NAPS2's proven 5-part pattern)
1. One interface at hardware seam (IScanBridge); 2. MockScanBridge fake returning canned ProcessedImage lists + injectable Exception; 3. protocol-level unit tests w/ synthetic byte buffers; 4. REAL Tesseract/PDF binaries in tests (deterministic, hardware-free — don't mock OCR); 5. GUI automation w/ scan step substituted.
Stack: **xunit.v3 4.0.0 + NSubstitute 6.2 + AwesomeAssertions 9.5 (NOT FluentAssertions ≥8 — Xceed commercial license) + Verify.XunitV3 (snapshot CSV/PDF — scrub /CreationDate /ModDate /ID) + FlaUI 5.0 (targeted dialogs) + Appium.WebDriver (few E2E) + RichardSzalay.MockHttp (Gemini — never hit paid API in CI) + coverlet → ReportGenerator**.
Data-layer: in-memory SQLite per test; **versioned fixture .db files per shipped schema version + migration test per fixture** (catches library-corrupting migration bugs).

## CI/CD (GitHub Actions)
- windows-2025 runners: .NET 10 SDK preinstalled (10.0.302), Inno Setup 6.7.1 preinstalled (7.x = choco), WiX 3 only. Public repos free.
- ci.yml: checkout@v7 → setup-dotnet@v6 10.0.x → restore/build/test → dotnet format --verify-no-changes; CodeQL csharp job.
- release.yml on tag v*: publish → sign payload (signpath action or azure/trusted-signing-action@v2 w/ OIDC) → ISCC installer → sign installer → SHA256SUMS → attest-build-provenance@v4 → softprops/action-gh-release@v3.
- winget.yml on release: vedantmgoyal9/winget-releaser@v2 (needs PAT).
- Dependabot nuget ecosystem; Renovate later.

## Repo hygiene
- Layout: src/ tests/ docs/ build/ .github/. **.slnx** (default in .NET 10 `dotnet new sln`).
- Directory.Build.props: net10.0-windows, Nullable enable, ImplicitUsings, TreatWarningsAsErrors (Release), Deterministic, ContinuousIntegrationBuild, EnableNETAnalyzers AnalysisMode=Recommended.
- **Directory.Packages.props CPM** + CentralPackageTransitivePinningEnabled; GlobalPackageReference for SourceLink.GitHub + Meziantou.Analyzer (3.0.170, MIT — yes) + Roslynator (yes). Skip StyleCop (stale). global.json rollForward latestFeature. dotnet format gate.
- **License: MIT** (Apache-2.0 defensible for patent grant). Cannot copy GPL NAPS2.Lib code into MIT app; CAN reference NAPS2.Sdk (LGPL) from MIT — only copyleft in tree (may complicate SignPath; clarify). Avoid iText (AGPL), QuestPDF hard dep (revenue-gated), FluentAssertions ≥8. Prefer TesseractOCR (Sicos1977, 5.5.2, active) over charlesw Tesseract (5.2.0, quiet). Magick.NET (Apache) if zero revenue gates wanted over ImageSharp.
- Ship THIRD-PARTY-NOTICES.md + **privacy policy at install w/ Gemini opt-out**.

## Key uncertainties
Publish sizes (benchmark scaffold); Velopack paid tier; Azure signing 3-yr org rule at GA; Certum price; SignPath turnaround; .slnx VS support.
