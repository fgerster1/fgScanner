# SPEC-2026-008 — Prompt pack

Eight prompts, four phases (spec §19). Every prompt: failing tests FIRST (§11.1),
smallest diff, nothing from a later prompt. The golden results bytes from phase 2
are the standing canary — if `ContractGoldenTests` ever wants regoldening in this
spec, the change is wrong, stop.

---

PROMPT 1 of 8 — IndexAnswer and the widened writer (Phase A)
Spec: SPEC-2026-008 §03.1, §07, §08, AC-5    Depends on: —

Read SPEC-2026-008 §07 and §08 before starting.

Do only this:
1. Write the failing `PackageWriterTests` cases from §11.1: `WriteResults` over an
   `IReadOnlyList<IndexAnswer>` accepts one answer of each field (doc_type, date,
   person, subject, key_flag), a person answer with qualifier, and a withdrawal
   (empty value); and refuses, each with its own message, BEFORE writing any file:
   unknown field, off-vocabulary doc type / subject / person id, bad date shape,
   qualifier on a non-qualifier field or outside the package's qualifier set,
   empty decidedBy, anchor not in the package.
2. Add the `IndexAnswer` record (§07, with doc comments as the schema of record)
   and widen `PackageWriter.WriteResults`; the existing `DocTypeAnswer` overload
   delegates to it unchanged.
3. `dotnet test -c Release` both test projects; `ContractGoldenTests` untouched
   and green.

Do NOT in this prompt: any App/UI code, any portal-repo change, any contract file.

CHECKPOINT 1 — paste back:
  · dotnet test -c Release (Core.Tests) → all green, new tests listed
  · dotnet format --verify-no-changes → clean
  · confirm: ContractGoldenTests unchanged (git diff shows no golden files)

---

PROMPT 2 of 8 — Cross-repo round trip (Phase A)
Spec: SPEC-2026-008 AC-6, §06 assumptions 1+4    Depends on: Prompt 1 (green)

Do only this:
1. Scanner side: failing `ResultsRoundTripTests` (env-gated on the sibling repo
   like the sync-manifest comparison): build a results file via the widened writer
   containing one answer per field, one qualifier, one withdrawal, from the
   fixture package.
2. Portal side (JimsStuff): failing test in `tests/test_import_package.py` that
   imports that shape into the fixture register with zero refused rows — pin the
   exact field ids and the key_flag value convention ("true"/""); fix the spec's
   §06 if the pin disagrees.
3. Green in both suites; one commit in each repo.

Do NOT in this prompt: UI code; contract schema edits (if the round trip proves
one necessary, STOP and report — that is a sync-contract decision for Franz).

CHECKPOINT 2 — paste back:
  · dotnet test -c Release --filter ResultsRoundTrip → green (not skipped) with sibling present
  · JimsStuff: python -m pytest tests/test_import_package.py -q → green
  · confirm: field ids doc_type/date/person/subject/key_flag imported as decisions

---

PROMPT 3 of 8 — Index section: shell, flag, open and refuse (Phase B)
Spec: SPEC-2026-008 §03.2, AC-1, AC-10    Depends on: Prompt 2 (green)

Read §04 (shell pattern) and the ADR-0010 rule before starting.

Do only this:
1. Failing `ShellViewModelTests`: section "Index" present only when
   `Feature.IndexMode` is on (mirror the `Feature.Search` tests).
2. Failing `IndexViewModelTests`: opening a non-package folder / corrupted
   fixture / newer-format fixture surfaces `PackageRefusedException.Message`
   verbatim in the section's state; the section stays usable after a refusal.
3. `IndexView` + `IndexViewModel` (DI like the other sections), folder picker,
   `PackageReader.Open` on a worker thread, refusal banner. Add entries, never
   clear bound collections.
4. Both suites + format gate.

Do NOT in this prompt: document list rendering, answering, drafts, router keys.

CHECKPOINT 3 — paste back:
  · dotnet test -c Release → green incl. new Shell/IndexViewModel tests
  · run the app with the flag on → Index section appears; point it at a text
    folder → the reader's own refusal text shows, word for word

---

PROMPT 4 of 8 — Document list, page viewer, shortcuts (Phase B)
Spec: SPEC-2026-008 §08 UI panes, AC-2, AC-9    Depends on: Prompt 3 (green)

Do only this:
1. Failing `IndexViewModelTests`: fixture package lists documents in seed order
   with page counts and answered/total badges; selecting a document exposes its
   pages' image paths and its suggestions with reasonQuote text.
2. Failing `ShortcutRouterTests`: the Index page keys (next/previous document,
   next/previous page, zoom) fire only on the Index section; existing sections'
   keys never fire on Index.
3. Document list + page viewer (existing image/Zoom patterns, one image loaded at
   a time), router entries.

Do NOT in this prompt: the answer panel, staging, drafts.

CHECKPOINT 4 — paste back:
  · dotnet test -c Release → green
  · app: open PKG-0001 (real), page through a multi-page document, confirm
    priority order matches the portal's plan

---

PROMPT 5 of 8 — Answer staging (Phase C)
Spec: SPEC-2026-008 §08 answer panel, AC-3    Depends on: Prompt 4 (green)

Do only this:
1. Failing `IndexAnswerStagingTests` (§11.1): Accept stages the suggestion's
   field/qualifier/value; untouched suggestions stage nothing; edit-after-accept
   replaces (one row per field+qualifier-slot; same person+same qualifier
   replaces, same person+different qualifier adds); withdrawal staged as empty
   value; key_flag toggle; date inline validation (ISO only, never reformatted).
2. The answer panel: five fixed rows (§08), pickers fed from the package
   vocabularies, active ids only, suggestions with Accept buttons + quotes,
   current portal decisions pre-filled, "person not in the list" note flag (C3).
3. Both suites + format gate.

Do NOT in this prompt: any disk persistence, export, delete.

CHECKPOINT 5 — paste back:
  · dotnet test -c Release → green, staging tests listed by name
  · app: accept a subject suggestion, change it manually, confirm one staged
    answer results

---

PROMPT 6 of 8 — Draft store (Phase C)
Spec: SPEC-2026-008 §07 draft file, AC-4    Depends on: Prompt 5 (green)

Do only this:
1. Failing `IndexDraftStoreTests`: atomic save (temp + File.Replace) after every
   staging change to `%APPDATA%\FGScanner\index-drafts\<packageId>.json`; restart
   + reopen restores every answer; a draft whose stored package checksum differs
   from the opened package refuses with its own message; orphaned draft listed,
   never silently removed; failed save surfaces a banner immediately (§14).
2. Wire the store into the view model; `Index.DeciderName` setting (Q1: Settings
   box, seeded from Environment.UserName, answering blocked while empty), read
   where used, announced via SettingsChanged.
3. Both suites + format gate.

Do NOT in this prompt: export, share, delete.

CHECKPOINT 6 — paste back:
  · dotnet test -c Release → green
  · app: answer two documents, kill the process, relaunch, reopen → both restored
  · confirm: the package folder's bytes are untouched (recompute its checksums)

---

PROMPT 7 of 8 — Export, email leg, delete-after-confirm (Phase D)
Spec: SPEC-2026-008 §08 export, AC-7, AC-8, Q2, Q4    Depends on: Prompt 6 (green)

Do only this:
1. Failing `IndexExportTests`: export validates the full set through the writer,
   writes `<parent>\<packageId>-results.json` atomically, seq 1..N, pinned
   appVersion + package checksum; deterministic bytes on unchanged re-export;
   refusal when nothing is answered; locked-file refusal names the file.
2. Failing `IndexDeleteTests`: delete disabled before export and after any
   post-export change; confirm dialog carries counts + export date; Yes removes
   package folder + draft; No removes nothing.
3. Export button + "Email the results file" through `IShareService` (SPEC-2026-007
   rules unchanged: no send path, log surface/count/format, never recipient);
   delete-after-confirm; one log line per open/export/delete (§14 — never answer
   values).
4. Both suites + format gate.

Do NOT in this prompt: docs, release work, any portal change.

CHECKPOINT 7 — paste back:
  · dotnet test -c Release → green
  · app: export PKG-0001 with three answered docs → file appears beside the
    package; JimsStuff: import it into a SCRATCH copy of the register → 0 refusals
  · delete button: disabled → export → enabled → cancel keeps everything

---

PROMPT 8 of 8 — Review, docs, walkthrough (Phase D close-out)
Spec: SPEC-2026-008 §18, §21, AC-11    Depends on: Prompt 7 (green)

Do only this:
1. Run `/code-review max` in a fresh session over the branch; fix or record every
   finding in §22 (the spec's standing format).
2. §18 docs: ADR-0014 (drafts + no-auto-apply + delete gate), CLAUDE.md Index
   paragraph, FEATURE-PARITY row, memory update.
3. Full gates: both scanner suites Release, `dotnet format --verify-no-changes`,
   JimsStuff full pytest, FG_OPEN_PACKAGE smoke.
4. Write Franz's AC-11 walkthrough script (2am-simple, numbered) into the spec or
   runbook and hand it to him — the walkthrough itself is his, on the real window.

Do NOT in this prompt: publish any release; `Feature.IndexMode` stays off in any
published channel (§21).

CHECKPOINT 8 — paste back:
  · the review's finding count and the §22 record
  · all four gate commands green
  · the walkthrough script, ready for Franz
