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
- **The CLI publishes to `cli\`, never beside the app.** `fgscanner.exe` and `FgScanner.exe` differ only in case, so one folder means the CLI overwrites the app — release 0.5.2 shipped that way, unnoticed because local builds never publish the CLI. `build/verify-publish.ps1` guards it in `release.yml`; install and start a draft release before publishing it.

**Code:**
- Comments explain *why*, never what. Validate at boundaries (user input, files, external APIs) only. No features beyond the task.
- Hardware access only through `IScanService`. All index/file writes atomic (temp + `File.Replace`). Dates ISO-8601; numbers invariant culture.
- UI is English-only; user-visible strings are written inline, no .resx (docs/adr/0001).
- Shortcuts are bound on the main window, so they fire whichever section is showing. `ShortcutRouter` decides which section each one acts on — a new shortcut must say which section it belongs to, and a page key must never reach a section that is not on screen (SPEC-2026-003).
- **A setting is read where it is used, and a change is announced through `SettingsChanged`** (ADR-0010). The section view models are singletons, so a setting captured in a constructor is frozen until the next launch — that is how twelve settings came to need a restart. A reload is deferred while `ScanViewModel.CaptureInHand`: rebuilding the Scan page mid-sheet strands an as-found capture with no clean partner.
- **Never clear an `ObservableCollection` that is bound to a `Selector.SelectedItem`** — add and remove the entries that differ. WPF writes the nulled selection back into the view model, which crashed navigation and silently rebuilt an open group. No headless test can see this; check it on the real window.

**Tests:**
- Business logic must run without a scanner (FakeScanService). OCR tests run real Tesseract (deterministic — never mock the engine). AI tests use MockHttp — never live keys, never network in CI. CSV/PDF assertions via Verify snapshots (scrub PDF /CreationDate /ModDate /ID).
- `dotnet test` runs in MTP mode: test projects are Exe, no `Microsoft.NET.Test.Sdk`, opt-in is the `"test"` section in global.json.

**Process:** feature branch per phase (`phase-N-name`), CI green before merge, update FEATURE-PARITY.md and docs/adr/ when a decision lands. The release number lives only in `<Version>` in Directory.Build.props — the installer reads it back off the published exe.

## Evidence work for JimsStuff

FG Scanner is the capture station for a legal-evidence pipeline (`docs/spec-evidence-export.md`);
the JimsStuff portal (`JimsStuff/pipeline/import_fgscanner.py`) parses committed group folders.

- **Stable external contracts — renaming silently breaks a legal pipeline:** the `index.json`
  row keys (`sequence`, `pageId`, `checksum`, `isBlank`, `originalChecksum`, `capturedBy`, plus
  the original six), `manifest.json`'s `evidenceExport`, and the Evidence profile's field names
  (`DocNo`, `DocDate`, `DocType`, `Title`, `Parties`, `Operator`, `Redact`, `Box`, `Notes`,
  `NoteState`, `NoteAuthor`, `NoteBasis`, `NoteWhen`). `manifest.json`'s field entries also carry
  a `scope` (`row`/`batch`) — see `docs/spec-batch-row-metadata.md` and ADR-0004 for what field
  scope is and why `Box`/`Operator` are `batch`; ADR-0005 for why `capturedBy` is null on
  retro-processed pages.
- **`FgScanner.Core.Evidence.EvidenceProfile` is that field contract as code, and
  `ProfileService.EnsureEvidenceProfileAsync` creates or repairs the profile from it.** The
  operator used to hand-enter all of them, which made one typo (`NoteAuthour`) a silent break:
  the importer parses these names and cannot tell a misspelled field from an absent one.
  Re-seeding an intact profile mints no schema version, so the action is safe to repeat.
  This is what took `MaxFields` from 12 to 16 — the cap was PLAN §8 keeping the pre-scan
  editor usable, and nothing downstream is bounded by it. The operator reaches it through
  **Settings → "Build the Evidence profile"**; pressing it again is how a hand-edited
  profile is repaired.
- **Annotated sheets (sticky notes) are captured twice: as-found, then clean**, per
  `JimsStuff/docs/superpowers/plans/2026-08-27-annotated-pages-sticky-notes.md`. Neither
  image alone is a duplicate of the whole thing under Ohio Evid.R. 1001/1003, and lifting a
  note before capture is alteration. `AnnotatedCaptureSequence` owns the `NoteState` value so
  the operator never types it — **`NoteState` must never be made sticky**, because pending
  field values persist across scans and a sticky one would stamp `as-found` onto every plain
  sheet after it. Ctrl+Shift+N starts the sheet; the ordinary Scan key takes the clean
  capture. Abandoning a sheet trashes its captures: an as-found with no clean partner is a
  whole-group refusal at import, by which time the box has been re-shelved.

  **The Scan panel must keep showing `AnnotatedPrompt` and the Cancel control while
  `AnnotatedActive`.** The sequence is otherwise invisible — a sheet stays part-scanned with
  nothing on screen saying so, the next ordinary scan silently becomes its clean capture, and
  Cancel, the one control that keeps a half-pair off the disk, is unreachable. Every path that
  moves the sequence calls `AnnouncedAnnotatedState()`; a state change nobody announces hides
  that control while a sheet is genuinely in hand, which a test pins directly.
- **Both sides of a stack are captured in two passes on a feeder that scans one side**, and
  `DuplexPassSequence` (`FgScanner.Core.Capture`) works out which back belongs to which front.
  **The pairing happens on the Scan page, before anything is saved** — adoption numbers documents
  in the order it is handed them and renames the files to match, so ordering afterwards rewrites
  rows that are already written and re-exports a committed group (ADR-0011). The Groups-page
  Reverse / Interleave / Deinterleave buttons are the *repair* tool for stacks captured before
  this existed, or for one whose pairing was refused; never send an operator there for a fresh
  two-pass run.

  **The Scan panel must keep showing `DuplexPrompt` and the Cancel control while `DuplexActive`,
  and every path that moves the sequence must call `AnnouncedDuplexState()`** — the same rule as
  the annotated sheet, for the same reason, with a whole stack attached to it instead of one
  sheet. A stack left half-captured with nothing on screen saying so ends with the fronts adopted
  as whole one-sided documents. Both sequences own that one prompt area, so they are mutually
  exclusive; neither may start while the other is in hand.

  On a count mismatch **nothing is paired** and both counts are reported (§05 Q1a) — an odd stack
  is a mismatch. A confident wrong pairing is worse than an obvious mess, because nobody looks for
  it until it is read out. Two passes are refused unless the source is the feeder.

  **A duplex save keeps every back.** Blank backs are byte-identical, so a save whose pages were
  captured as pairs suppresses *both* things that remove them — adoption's checksum skip and
  capture triage's blank-page Drop policy — for those pages only, never as a default. The blank
  back of an evidence page is evidence that the back is blank, and a page removed shifts every
  pairing after it. That suppression belongs to the pages and lasts exactly as long as they are
  staged: it must survive a save that could not take every page, and must not outlive them.
- **Pages can leave the station by email** (SPEC-2026-007, ADR-0012), from the Scan page and from
  a group. `IShareService` opens a message and has no way to send — the operator presses Send in
  their own client; never add a send path, SMTP or a stored credential. What leaves is a copy built
  under `%TEMP%\FGScanner\email` (never in the group folder), swept at startup and exit. **Each send
  is logged with surface, page count, format and route — never a recipient and never the subject.**
  The first send from a committed evidence group shows a one-time warning inside the dialog;
  "evidence" is recognised by the contract's required fields, never by the profile's name. Routes:
  MAPI when the registry probe finds a client, then the Share sheet, then Explorer — unless
  Settings' "Send email with" (`Email.SendWith`) says Gmail or Yahoo Mail, when the compose page
  opens in the browser with the files on the clipboard and the operator presses Ctrl+V (no Windows
  mechanism can attach to webmail, but a browser takes a pasted file); Explorer, to drag from, is
  only the fallback when the clipboard cannot be set. Franz uses Gmail and Jim uses Yahoo, so on both stations it is the webmail path.
- `Feature.PreserveOriginals` stays ON for evidence groups (ADR-0003); the `originals\`
  subfolder and its checksums are part of the folder's evidentiary integrity.
- **A field's length and memo flag are layout and validation settings, never part of the export
  contract.** `FieldDefinition.MaxLength`/`Memo` shape how a value is typed on screen; no writer
  emits them, and memo is a flag on Text rather than a `FieldType` because `FieldType` casts
  positionally to `IndexFieldType` and its name is written into `manifest.json` (ADR-0009) —
  **even though the Settings Type list shows Memo as a fifth type.** That list is
  `FieldDisplayType`, a screen-only enum in the App layer that maps to `(FieldType, bool Memo)`;
  seeing Memo on screen is not evidence the stored enum gained a member, and neither direction of
  that mapping may fall through to a default, because absorbing an unknown type rewrites the name
  the export hands the importer.
- FG Scanner deliberately has **no Bates support** and none should be added to the capture
  path — identifiers live in the portal's register and display layer; stamped pixels can never
  be reorganized, and re-stamping is evidence alteration.
