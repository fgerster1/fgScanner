# FG Scanner — CLAUDE.md

Windows desktop scanning app (NAPS2-class) with a document-indexing layer. The approved plan is **docs/PLAN.md** — read the relevant § before starting any phase. Living parity checklist: docs/FEATURE-PARITY.md. Research behind every decision: docs/research/.

## Stack

.NET 10 (LTS) · WPF (Fluent, light/dark) · NAPS2.Sdk (scanning; LGPL-2.1) · EF Core 10 + SQLite · Tesseract 5.5 via shell-out (`NAPS2.Tesseract.Binaries`) · PDFsharp · Google.GenAI (Gemini, BYO-key) · CsvHelper + ClosedXML · Inno Setup 7 · xunit.v3 (MTP mode) + NSubstitute + AwesomeAssertions + Verify + FlaUI.

## Commands

```bat
dotnet build                            rem debug build
dotnet build -c Release                 rem warnings are errors here
dotnet test  -c Release                 rem MTP mode (opt-in lives in global.json "test" section)
dotnet format --verify-no-changes       rem CI gate
dotnet run --project src/FgScanner.App
dotnet publish src/FgScanner.App -p:PublishProfile=win-x64
```

Installer (PowerShell, from repo root — `ISCC.exe` is not on PATH, and Inno Setup may be
installed machine-wide *or* per-user under `%LOCALAPPDATA%\Programs`):

```powershell
$iscc = Get-ChildItem "${env:ProgramFiles(x86)}\Inno Setup*","$env:ProgramFiles\Inno Setup*",
  "$env:LOCALAPPDATA\Programs\Inno Setup*" -Filter ISCC.exe -Recurse -EA SilentlyContinue |
  Sort-Object FullName -Descending | Select-Object -First 1
& $iscc.FullName build\installer\setup.iss   # → dist\ (version read from the published exe)
```

## Architecture (docs/PLAN.md §8)

```
FgScanner.App  (WPF shell, MVVM via CommunityToolkit, DI via Microsoft.Extensions.Hosting)
FgScanner.Core (domain + services: GroupService, IndexExporter, NamingEngine, TrashService, JobQueue)
FgScanner.Scanning (IScanService → NAPS2.Sdk; FakeScanService for tests)
FgScanner.Ocr  (TesseractRunner, MarkdownReconstructor)
FgScanner.Ai   (IDescriptionProvider → Gemini via IChatClient)
FgScanner.Data (EF Core + SQLite; JSONB custom fields; FTS5)
FgScanner.Cli  (headless fgscanner.exe; Core/Scanning/Data/Ocr/Ai — never App/WPF)
```

## Hard rules

**Licensing guards (never violate):**
- Never copy code from NAPS2.Lib / NAPS2 app layer (GPL). Reading for reference is fine; re-implement.
- Never reference: `NAPS2.Images.ImageSharp`, `FluentAssertions` ≥8, `iText*`, `EPPlus` ≥5, `Emgu.CV`, `System.Data.SQLite`.
- Never merge/bundle NAPS2.* DLLs into a single file (LGPL separation); publish profile stays non-single-file, non-trimmed.

**Code:**
- Comments explain *why*, never what. Validate at boundaries (user input, files, external APIs) only. No features beyond the task.
- Hardware access only through `IScanService`. All index/file writes atomic (temp + `File.Replace`). Dates ISO-8601; numbers invariant culture.
- UI is English-only; user-visible strings are written inline, no .resx (docs/adr/0001).

**Tests:**
- Business logic must run without a scanner (FakeScanService). OCR tests run real Tesseract (deterministic — never mock the engine). AI tests use MockHttp — never live keys, never network in CI. CSV/PDF assertions via Verify snapshots (scrub PDF /CreationDate /ModDate /ID).
- `dotnet test` runs in MTP mode: test projects are Exe, no `Microsoft.NET.Test.Sdk`, opt-in is the `"test"` section in global.json.

**Process:** feature branch per phase (`phase-N-name`), CI green before merge, update FEATURE-PARITY.md and docs/adr/ when a decision lands. The release number lives only in `<Version>` in Directory.Build.props — the installer reads it back off the published exe.

## Evidence work for JimsStuff

FG Scanner is the capture station for a legal-evidence pipeline (`docs/spec-evidence-export.md`);
the JimsStuff portal (`JimsStuff/pipeline/import_fgscanner.py`) parses committed group folders.

- **Stable external contracts — renaming silently breaks a legal pipeline:** the `index.json`
  row keys (`sequence`, `pageId`, `checksum`, `isBlank`, `originalChecksum`, plus the original
  six), `manifest.json`'s `evidenceExport`, and the Evidence profile's field names (`DocNo`,
  `DocDate`, `DocType`, `Title`, `Parties`, `Operator`, `Redact`, `Box`, `Notes`,
  `NoteState`, `NoteAuthor`, `NoteBasis`, `NoteWhen`).
- **`FgScanner.Core.Evidence.EvidenceProfile` is that field contract as code, and
  `ProfileService.EnsureEvidenceProfileAsync` creates or repairs the profile from it.** The
  operator used to hand-enter all of them, which made one typo (`NoteAuthour`) a silent break:
  the importer parses these names and cannot tell a misspelled field from an absent one.
  Re-seeding an intact profile mints no schema version, so the action is safe to repeat.
  This is what took `MaxFields` from 12 to 16 — the cap was PLAN §8 keeping the pre-scan
  editor usable, and nothing downstream is bounded by it.
- **Annotated sheets (sticky notes) are captured twice: as-found, then clean**, per
  `JimsStuff/docs/superpowers/plans/2026-08-27-annotated-pages-sticky-notes.md`. Neither
  image alone is a duplicate of the whole thing under Ohio Evid.R. 1001/1003, and lifting a
  note before capture is alteration. `AnnotatedCaptureSequence` owns the `NoteState` value so
  the operator never types it — **`NoteState` must never be made sticky**, because pending
  field values persist across scans and a sticky one would stamp `as-found` onto every plain
  sheet after it. Ctrl+Shift+N starts the sheet; the ordinary Scan key takes the clean
  capture. Abandoning a sheet trashes its captures: an as-found with no clean partner is a
  whole-group refusal at import, by which time the box has been re-shelved.
- `Feature.PreserveOriginals` stays ON for evidence groups (ADR-0003); the `originals\`
  subfolder and its checksums are part of the folder's evidentiary integrity.
- FG Scanner deliberately has **no Bates support** and none should be added to the capture
  path — identifiers live in the portal's register and display layer; stamped pixels can never
  be reorganized, and re-stamping is evidence alteration.
