# SPEC-2026-008 — FG Scanner index mode (case-index phase 4)

| | |
|---|---|
| **Status** | Approved |
| **Revision** | A |
| **Tier** | Feature |
| **Author** | Claude, for Franz Gerster |
| **Date** | 2026-09-30 |
| **Project** | FgmakerScanner |
| **Parent** | JimsStuff `SPEC-2026-003` (program), phase 4 of 12 |
| **Supersedes** | — |

---

## 01 · Management summary

- **What we are building** — a new "Index" screen in FG Scanner. Jim opens a package
  of scanned documents that the portal exported (the first one, PKG-0001, holds 100
  documents), sees each document's pages beside the AI's proposals, and answers five
  questions per document: what kind of document it is, its date, who is on it, what
  subjects it touches, and whether it is a key document. His answers are saved into a
  results file he sends back; the portal makes them the official index.
- **Why** — this is the screen where Jim's knowledge of the case actually enters the
  system. Everything before it (the register, the AI suggestions, the export) exists
  so this screen can show him the right 100 documents in priority order with the
  busy-work pre-filled; everything after it (search, timelines, the contradiction
  work) depends on the answers it collects.
- **What it costs** — 8 prompts across 4 phases, roughly two to three days of build
  sessions. No new recurring cost: no AI calls, no hosting; it reads files the portal
  already produces.
- **What could go wrong** — top two: (1) an answer file that doesn't match the
  contract would be refused by the portal after Jim answered 100 documents — so the
  writer validates every answer at entry, not at the end; (2) a crash or update
  mid-batch could lose days of Jim's answering — so every answer is saved to disk the
  moment he moves on, and reopening the package resumes exactly where he stopped.
- **What we need from you** — five decisions on the review page (who the answers are
  signed by, whether the send-back goes by email from inside the app, partial
  send-backs, how package deletion is confirmed, and how the screen is switched on).

## 02 · Outcomes

- **Goal** — Jim can index a package of 100 documents by himself, at his own pace,
  over several sittings, without being able to produce an answer file the portal
  would refuse.
- **Who benefits** — Jim (the work becomes accept-or-correct instead of
  type-everything); Franz (the register fills with human-decided index values);
  later, the attorney (everything findable by date/person/type/subject).
- **How we will know it worked** — Jim finishes PKG-0001 and the portal imports his
  results file with zero refused rows; the time per document is minutes, not tens of
  minutes, because most answers are one Accept click.

## 03 · Scope and non-goals

**In scope**

1. `FgScanner.Core.IndexPackages.PackageWriter` widened from the phase-2 slice
   (doc-type answers only) to the full answer surface: `doc_type`, `date`
   (+qualifier), `person` (+qualifier), `subject`, `key_flag` — plus withdrawals
   (empty value), all schema-validated at entry against the package's own
   vocabularies.
2. A new **Index** section in the shell (same string-keyed pattern as
   Scan/Groups/Search/Trash/Settings): open-package picker, checksum/version
   verification through the existing `PackageReader` (all its refusal messages shown
   verbatim), document list with progress, page viewer, answer panel.
3. **Accept buttons on the AI suggestions** — each shows its value and the quoted
   reason; Accept turns it into an answer; everything is correctable before and after.
4. **Draft persistence** — answers autosaved outside the package folder; reopening
   the package resumes; the package files themselves are never written.
5. **Results export** — writes `results.json` via the widened writer (seq numbering,
   `decidedBy`, `decidedAt`, appVersion pinned at open), atomic write, re-exportable.
6. **Delete-after-confirm** — a "Remove package from this computer" action, enabled
   only after a successful export, with an explicit confirmation naming the counts.
7. ShortcutRouter entries for any new keys, section-scoped per the standing rule.

**Non-goals**

- **No portal-side work.** Import, progress dashboard and Jim's runbook are phase 5.
- **No release to Jim from this phase.** Parent spec: ships to Jim only after phase 5
  is deployed. Franz tests on the dev station.
- **No editing of vocabularies** — people, subjects and doc types come from the
  package read-only. An unknown person Jim needs is typed as a plain value only if
  the portal's contract allows it — it does not in v1, so phase 4 offers "the person
  is not in the list" as a note-to-Franz flag, not a free-text person (the person
  register stays the single source of names).
- **No Bates numbers, no redaction, no evidence-profile involvement.** The index
  package loop shares no files or writer code with the capture evidence contract.
- **No new AI calls** — the suggestions in the package are the AI's contribution.
- **No bulk answering** ("apply to all") in v1 — a wrong bulk answer on a legal
  index outweighs the keystrokes saved; revisit with real usage data.
- **No withdrawal of an already-decided person or subject.** Discovered mid-build
  and decided by Franz in-terminal (2026-09-30): for the multi-value fields the
  value joins the decision slot (SPEC-2026-003 §07 amendment), and the contract's
  empty-value withdrawal cannot NAME which person/subject it withdraws — so phase 4
  stages multi-value ADDS only, and removal of a decided person/subject belongs to
  phase 5's web UI. Single-value withdrawals (doc type, date, key flag) stay.

## 04 · Current state

- **Stack** — .NET 10 WPF, MVVM via CommunityToolkit, DI via
  Microsoft.Extensions.Hosting; xunit.v3 MTP; warnings-as-errors in Release;
  `dotnet format` gate.
- **Contract (vendored, read-only)** — `docs/contract-vendored/schemas/`:
  `seed.schema.json` (per document: `anchorPageId`, pages with `images/` paths,
  current `decisions`, open `suggestions` each with `suggestionId`/`field`/
  `qualifier`/`value`/`reasonQuote`), `results.schema.json` (answers:
  `seq`/`anchorPageId`/`field`/`qualifier`/`value`/`decidedBy`/`decidedAt`;
  empty value = withdrawal; `packageChecksum` = SHA-256 of manifest bytes;
  `appVersion` pinned at open), plus people/subjects/doctypes vocabularies
  (soft-deleted rows ship on purpose — membership, not activity, is what the
  writer checks; hiding inactive ids is this UI's job).
- **Core** — `src/FgScanner.Core/IndexPackages/`: `PackageReader.Open` (total-refusal
  contract: marker → formatVersion (verbatim `UpdateMessage` on newer) → checksums
  with path confinement → coherence; returns `IndexPackage` with documents,
  decisions, suggestions, vocabularies), `PackageWriter.WriteResults` (**today takes
  `IReadOnlyList<DocTypeAnswer>` only** — the phase-2 minimal slice; validates
  anchor membership, doc-type membership, decidedBy charset), `PackageSmokeTests`
  (`FG_OPEN_PACKAGE` env opens any real package).
- **Shell pattern** — `src/FgScanner.App/Views/ShellViewModel.cs`: sections are a
  string-keyed list ("Scan", "Groups", …); `Feature.Search` shows how a section is
  feature-flag-hidden; `ShortcutRouter.cs` routes page keys per section (page keys
  must never reach an off-screen section). Settings are read where used and announced
  via `SettingsChanged` (ADR-0010). Bound `ObservableCollection`s are never cleared
  (the Selector write-back trap).
- **Provenance precedent** — `capturedBy = Environment.UserName`
  (`src/FgScanner.Data/GroupService.cs:445`, `IndexingService.cs:523`).
- **Email path** — `IShareService` (SPEC-2026-007): opens a message, never sends;
  webmail route = compose page + files on clipboard; per-send log (no recipient/subject).
- **Real vocabulary values (the enum check, from the live register 2026-09-30)** —
  person qualifiers: `from`, `to`, `cc`, `mentioned` (seeded
  `person_role_qualifiers`); date qualifiers in use: `exact`, `about`; suggestion
  fields present: `doc_type`, `date`, `person`, `subject`, `key_flag` (plus
  `amount`/`payee`/`expense_category`, which stay portal-side — money rows are for
  the phase-9 ledger, not Jim's five questions); subjects: 7 groups with 19 children
  (hierarchy in `people.json`-style vocab files).
- **PKG-0001 exists** — 100 documents, 257 images, on this station, opened clean by
  `PackageSmokeTests`.
- **Not examined** — the App's dialog/window sizing helpers in depth
  (`DialogFit`/`WindowSizing` — assumed reusable as-is); FlaUI end-to-end harness
  (present for other screens; §11 keeps UI automation to view-model level plus one
  manual walkthrough, matching how Scan/Groups are tested).

## 05 · Questions for Franz

**Blocking** — none; the contract fixes the data shapes.

**Answered — review round A, 2026-09-30** (page `SPEC-2026-008-rA`, verdict
approve; every item on the recommended option, no notes). Recorded per question
below; C1–C4 (no auto-apply, drafts outside the package, no free-text person, no
bulk answering) all confirmed “Agreed”.

**Mid-build amendment (2026-09-30, AskUserQuestion):** multi-value current
decisions. The approved parent-§07 slot rule — latest per (anchor, field,
qualifier) — silently collapsed a second mentioned person or second subject.
Franz chose “value joins the slot” for person and subject; the portal reader is
amended (test-first, 3,173 portal tests green), the planner follows, and the
phase-4 UI stages multi-value fields as independent chips. See the §03 non-goal
this creates for withdrawals.

1. **ANSWERED (a settings box): Who signs the answers (`decidedBy`)?** — *Options:* (a) Windows username
   automatically (precedent: `capturedBy`); (b) **recommended:** a Settings box
   "Indexer name", seeded from the Windows username, required non-empty before the
   first answer. *Why it matters:* `decidedBy` goes into the legal record on every
   row; Jim's laptop username may be something unhelpful like "Owner", and a
   deliberate name box costs one setting.
2. **ANSWERED (include the email leg): Send-back by email from inside the app?** — *Options:* (a) **recommended:**
   after export, offer "Email the results file" through the existing share path
   (Yahoo webmail + clipboard on Jim's station, one small results.json attached);
   (b) skip — Jim copies the file manually, phase 5 documents how. *Why it matters:*
   (a) reuses SPEC-2026-007 wholesale and removes the most error-prone manual step;
   cost is one button.
3. **ANSWERED (yes, any time): Partial send-backs allowed?** — *Options:* (a) **recommended:** yes — export
   works at any point, includes only answered documents, and the portal's importer
   already handles partial→imported by design; (b) export only when all 100 are
   answered. *Why it matters:* (a) lets Franz see progress and catch problems after
   ten documents instead of after a hundred; (b) is simpler to reason about but
   bottles up risk.
4. **ANSWERED (enabled after export + confirm): Delete-after-confirm shape** — *Options:* (a) **recommended:** button enabled
   only after a successful export of the current answer set; pressing it shows
   "PKG-0001: 100 documents, 257 images, results exported 2026-10-02 — remove the
   package folder from this computer?" and deletes permanently on Yes; (b) also
   require the portal to have confirmed import first — but the laptop can't know
   that in phase 4 (no portal connection), so (b) would really mean "never delete
   until phase 5 exists". *Why it matters:* the evidence images are copies (originals
   live in the portal), but deleting is still the one irreversible act on this screen.
5. **ANSWERED (Feature.IndexMode flag): How the screen is switched on** — *Options:* (a) **recommended:** a
   `Feature.IndexMode` flag, following the `Feature.Search` pattern — ON on the dev
   station for your testing, OFF in any release until phase 5 is deployed (the
   parent spec's shipping fence); (b) always visible. *Why it matters:* an auto-updated
   station must not grow a half-supported screen before the portal can take its output.

## 06 · Assumptions

| # | Assumption | If wrong |
|---|---|---|
| 1 | Answer field ids the portal expects are exactly `doc_type`, `date`, `person`, `subject`, `key_flag` (as in `index_suggestions`) | §07 field list and writer validation change; verified against the portal importer in Prompt 1's cross-repo test |
| 2 | Person/date qualifier sets are `from/to/cc/mentioned` and `exact/about` | Pickers show wrong options; values are read from the package vocabularies at open, so a vocabulary change flows through without an app change |
| 3 | `PackageReader` tolerates a package folder that gained no extra files (drafts live OUTSIDE the folder), and re-opening an already-opened package is idempotent | Draft location moves further away (it already is outside); no design change |
| 4 | `key_flag` answer value is `"true"` (flag) or `""` (withdraw/no) — mirroring the suggestion rows | One-line value mapping in the writer changes after the Prompt-1 cross-repo test pins it |
| 5 | The 0.5.x auto-update channel means anything merged to main can reach Jim's station if released — the `Feature.IndexMode` flag (§05 Q5) is the fence, releases stay unpublished regardless | Tighten to: no release published from this branch at all (parent spec already says so) |

## 07 · Data model

No database change. Three shapes, all file-based:

- **`IndexAnswer`** (new, `FgScanner.Core.IndexPackages`): `Seq:int`,
  `AnchorPageId:string`, `Field:string` (one of the five), `Qualifier:string?`,
  `Value:string` (empty = withdrawal), `DecidedBy:string`, `DecidedAt:string`
  (UTC, `yyyy-MM-ddTHH:mm:ssZ`). Documented as doc comments on the record, which are
  the §07 of record in code. Validation at construction/write: field whitelist;
  value membership per field (doc-type id, subject id, person id `P####`, date
  `yyyy-MM-dd` when field is `date`, `"true"`/`""` for `key_flag`); qualifier only
  on `person`/`date` and from the package's qualifier sets; decidedBy the existing
  charset guard.
- **Draft file** (new, App-side): `%APPDATA%\FGScanner\index-drafts\<packageId>.json`
  — the answer list so far plus the package checksum it belongs to (a draft is
  refused against a package whose checksum differs — a re-export from the portal is
  a different package). Written atomically (temp + `File.Replace`) after every
  answer change. Deleted by delete-after-confirm; orphaned drafts (package gone) are
  listed in the picker for cleanup, never silently removed.
- **`results.json`** — exactly the vendored `results.schema.json`; written by
  `PackageWriter` only; byte rules already enforced there (phase 2).

## 08 · Architecture and approach

**Core first, UI on top.** Phase A widens `PackageWriter.WriteResults` to
`IReadOnlyList<IndexAnswer>` with per-field membership validation (the
`DocTypeAnswer` overload stays, delegating, so the phase-2 golden tests keep
compiling unchanged — the golden `results.json` bytes must not change). A
cross-repo test (same pattern as `ContractGoldenTests`) round-trips a widened
results file through the portal importer fixture to pin assumptions 1 and 4.

**UI** (`src/FgScanner.App/Views/IndexView.xaml` + `IndexViewModel`, DI-registered
like the other sections; section key "Index"):

- **Open flow**: folder picker → `PackageReader.Open` → any
  `PackageRefusedException` message shown verbatim in the section (the refusal
  texts are the contract's own words — the newer-version `UpdateMessage`
  especially), never rephrased, never a dialog that can be OK'd past.
- **Three panes**: document list (anchor, title, pages, answered/total badge,
  priority order preserved from the seed) · page viewer (existing image display +
  `ZoomController` patterns; page-through within the document) · answer panel.
- **Answer panel, five rows in a fixed order** — Doc type (combo, family-grouped,
  active ids only), Date (text + `exact/about` toggle), People (list of chips with
  qualifier each, add from a searchable picker over `people.json` names+aliases),
  Subjects (grouped multi-check), Key document (toggle). Each row shows: the current
  portal decision if any (from `decisions`, as the pre-filled value), the AI
  suggestions for that field with their quoted reasons and an **Accept** button
  each, and the manual control. Accepting or editing stages an `IndexAnswer`;
  moving to another document autosaves the draft. Latest edit per (field,
  qualifier-slot) wins within the draft — one answer row per decision, not an
  append log (seq is assigned once at export).
- **Suggestion provenance**: a suggestion row never auto-applies. Untouched
  suggestions produce no answer — silence is not consent on a legal index.
- **Export**: validates the whole set through the writer, writes
  `<packageDir-parent>\<packageId>-results.json`, shows the count exported +
  skipped-unanswered, then (Q2) offers the email leg via `IShareService`.
- **Delete-after-confirm** (Q4): enabled only after an export that covered the
  current draft state; deletes the package folder and the draft.

**Alternatives considered** — (1) Editing answers directly into a copy of the
package folder: rejected, the package is read-only law and a stray write breaks its
checksums. (2) SQLite for drafts: rejected, one small JSON file per package, atomic
replace, no migration surface. (3) Reusing the Groups record editor for the answer
panel: rejected — that editor binds the capture-side field contract (ADR-0009
territory); the two contracts must never touch.

**Data access / system evolution** — no MCP, no index, no cache (§15 volumes are
trivial). Nothing here generalises into a skill; the reusable asset is the widened
writer, which phase 11's helper flow will also use.

## 08a · AI opportunity assessment

None in this phase, deliberately. The AI's contribution (suggestions with quoted
reasons) is already in the package; this screen is precisely the human-decides
gate, and adding model calls on the laptop would put an unauditable step between
Jim and the record. The learning loop for suggestion quality is portal-side
(acceptance rate per field is computable from results vs suggestions at import —
phase 5 already plans to track it).

## 09 · Design system compliance

The app's own WPF Fluent theme wins over the FG Maker web kit (project style guide
precedence; same call as every prior scanner spec). Follow the existing sections:
same nav placement, same light/dark behavior, inline English strings (ADR-0001).
Accessibility: every control keyboard-reachable in a top-to-bottom tab order per
answer row; visible focus follows the Fluent theme; Accept buttons carry
`AutomationProperties.Name` including the field and value ("Accept doc type:
letter"); the page viewer's zoom keys go through `ShortcutRouter` scoped to the
Index section.

## 10 · Acceptance criteria

> **AC-1** — Opening a folder that is not a package, a damaged package, or a
> newer-format package shows the reader's refusal message verbatim and leaves the
> section usable. *Proven by:* `IndexViewModelTests` → "refusal text is shown
> verbatim" (drives the real reader against corrupted fixture packages).

> **AC-2** — Opening PKG-0001's fixture twin lists its documents in seed order with
> correct page counts; selecting one shows its first page image path and its
> suggestions with reason quotes. *Proven by:* `IndexViewModelTests` → "documents
> and suggestions come from the seed".

> **AC-3** — Accept on a suggestion stages an answer with that field/qualifier/
> value; an untouched suggestion stages nothing; editing after Accept replaces the
> staged answer (one row per field+qualifier-slot). *Proven by:*
> `IndexAnswerStagingTests` → three named tests.

> **AC-4** — Every answer change autosaves the draft atomically outside the package
> folder; killing and restarting the app and reopening the package restores every
> staged answer; a draft whose package checksum differs is refused with its own
> message. *Proven by:* `IndexDraftStoreTests` → "restart resumes", "checksum
> mismatch refuses".

> **AC-5** — `PackageWriter.WriteResults(IndexAnswer[])` refuses: unknown field,
> off-vocabulary doc type/subject/person, bad date shape, qualifier on the wrong
> field or outside the set, empty decidedBy, anchor not in the package — each
> before any file is written; and the existing `DocTypeAnswer` golden bytes are
> unchanged. *Proven by:* `PackageWriterTests` (one test per refusal) +
> `ContractGoldenTests` staying green untouched.

> **AC-6** — A widened results file containing one answer of each field, a
> qualifier, and one withdrawal imports into the portal fixture register with zero
> refused rows. *Proven by:* cross-repo `ResultsRoundTripTests` (env-gated like the
> manifest comparison, skips when the sibling repo is absent) + the portal's own
> `test_import_package.py` fixture addition (one commit in each repo, synced).

> **AC-7** — Export writes `<packageId>-results.json` beside the package folder
> with seq 1..N, the pinned appVersion, and the package checksum; exporting twice
> replaces the file atomically with identical bytes when nothing changed. *Proven
> by:* `IndexExportTests` → "bytes are deterministic".

> **AC-8** — Delete-after-confirm is disabled before any export and after a
> post-export answer change; confirming removes the package folder and the draft;
> cancelling removes nothing. *Proven by:* `IndexDeleteTests` → three named tests.

> **AC-9** — Page keys added for the Index section never fire on other sections and
> other sections' page keys never fire on Index. *Proven by:* `ShortcutRouterTests`
> additions.

> **AC-10** — The Index section is absent when `Feature.IndexMode` is off and
> present when on, per the `Feature.Search` pattern (Q5). *Proven by:*
> `ShellViewModelTests` addition.

> **AC-11** — manual, Franz on the dev station: open the real PKG-0001, answer three
> documents (one via Accepts only, one manual, one with a withdrawal), export,
> re-open, confirm resume; then import the file into a scratch portal register and
> see the three documents' decisions. Named in §21; this is the walkthrough that
> headless tests cannot see (Selector write-back rule).

## 11 · Test strategy

**11.1 — Failing tests first** (per the repo's TDD rule; each prompt in §20 names
its red tests):

| Feature | Test file | The failing assertion |
|---|---|---|
| Widened writer | `tests/FgScanner.Core.Tests/IndexPackages/PackageWriterTests.cs` | `WriteResults` accepts an `IndexAnswer` set and refuses each malformed case by message |
| Golden stability | existing `ContractGoldenTests` | unchanged bytes (already green — the canary that must STAY green) |
| Round trip | `tests/FgScanner.Core.Tests/IndexPackages/ResultsRoundTripTests.cs` + portal `tests/test_import_package.py` | widened file, zero refusals |
| VM: open/refuse | `tests/FgScanner.App.Tests/IndexViewModelTests.cs` | verbatim refusal surfaced |
| Staging | `IndexAnswerStagingTests.cs` | accept/edit/one-row-per-slot |
| Draft store | `IndexDraftStoreTests.cs` | atomic write, resume, checksum refusal |
| Export | `IndexExportTests.cs` | deterministic bytes, seq, pinned version |
| Delete | `IndexDeleteTests.cs` | gating + removal + cancel |
| Router/shell | existing test files | section scoping, flag visibility |

**11.2 — Test data.** The phase-2 fixture package generators (golden twin) extended
with one fixture document carrying suggestions of every field — generated, never
hand-edited, same regolden discipline. No live keys, no network (AI rules don't
apply — no AI here).

**11.3 — Verification suite.** `dotnet build -c Release` (warnings=errors),
`dotnet test -c Release` both test projects, `dotnet format --verify-no-changes`,
portal `pytest` for the cross-repo commit, `FG_OPEN_PACKAGE` smoke against
PKG-0001, then AC-11 manual walkthrough.

## 12 · Edge cases and failure modes

| Case | Expected behaviour | Where handled |
|---|---|---|
| Package folder on a disconnected USB mid-session | Next image load fails → non-blocking banner "package drive missing", answers keep their draft; reopen resumes | IndexViewModel image loader |
| Draft exists, package deleted outside the app | Picker lists the orphan with "package missing — keep or discard the draft?" | Draft store |
| Results file locked (open in an email compose) | Export refusal names the file and says close-and-retry; draft untouched | Export path (AV/lock precedent from phase 2 finding 14) |
| Jim answers nothing and exports | Export refuses: "no answers yet" — an empty results file is legal for the contract but meaningless to send | Export gate |
| Same person added twice with two qualifiers | Allowed — `from` and `mentioned` are different slots (matches register semantics) | Staging model |
| Same person, same qualifier, twice | Second replaces first (one row per slot) | Staging model |
| Date typed as `13/02/2021` | Inline refusal at the field, ISO example shown; never silently reformatted | Answer panel validation |
| App auto-updates mid-batch | appVersion was pinned at open (contract); draft survives; reopening pins the new version for NEW exports — the already-exported file keeps its truth | PackageReader/draft store |
| Two FG Scanner instances on the same draft | Second `File.Replace` wins; single-instance behavior matches the rest of the app (no new mutex in this phase; noted in §16) | accepted, documented |
| Export target disk full | Atomic write fails whole; message; draft untouched | AtomicFileWriter reuse |

## 13 · Security and configuration

Desktop, no network, no new dependency, no secrets. The only new persisted settings:
`Feature.IndexMode` and (Q1) `Index.DeciderName` — both through the existing
`AppSettingsService`, read where used (ADR-0010). Input validation: everything Jim
types is validated against the package vocabularies before it can reach a file;
paths from the package were already confined by the reader (phase 2). The share leg
(Q2) inherits SPEC-2026-007's rules unchanged: no send path, no recipient logged.

## 14 · Observability

The section itself is the observer — refusals and export results are shown in full.
Persistent trace: one line per open / export / delete appended to the app's existing
log (package id, counts, checksum prefix, app version — never answer values). A
silent failure would be a draft that didn't save: the draft store surfaces every
failed save as a banner immediately (an unsaved answer Jim believes saved is this
screen's worst outcome), and the export re-validates the full set so a corrupt
draft cannot exit quietly.

## 15 · Performance and scale

100 documents / 257 images per package, a few thousand vocabulary rows, an answer
file under 100 KB — not a performance question. Images load one at a time (the
existing viewer pattern); nothing loads the whole package's images into memory.
Revisit only if a package ever exceeds ~2,000 documents, which the portal's
planner caps well below.

## 16 · Regression risk

| # | What could break | Why | Evidence (file:line) | Mitigation |
|---|---|---|---|---|
| 1 | Phase-2 golden results bytes | Widening the writer touches its serialization | `PackageWriter.cs:21`, `ContractGoldenTests` | `DocTypeAnswer` overload delegates; golden test is the canary and must not be regoldened |
| 2 | Shell navigation | New section joins a bound section list | `ShellViewModel.cs:27` (Search flag pattern) | Add/remove entries, never clear (standing rule); `ShellViewModelTests` |
| 3 | Shortcut leakage between sections | Keys bound window-wide | `ShortcutRouter.cs:43` | Route() entries + AC-9 |
| 4 | Capture evidence contract | The two contracts must never touch | CLAUDE.md hard rule | No shared files/writer code; review checks imports |
| 5 | CLI publish separation | New App code must not enter `FgScanner.Cli` | CLAUDE.md (0.5.2 incident) | No Cli changes at all in this spec |
| 6 | Settings freeze | New settings read in a constructor would freeze | ADR-0010 | Read where used; `SettingsChanged` |
| 7 | Contract sync drift | Cross-repo test commit (AC-6) touches both repos | `test_contract_sync.py` / `ContractSyncTests` | No contract file changes; if one proves needed it goes through sync-contract.ps1 in BOTH repos |
| 8 | Pre-existing red on main | `AnnotatedScanTests.A_plain_sheet…` already fails on untouched main | found 2026-09-30 | Named in every run report; not this spec's to fix (Franz to triage) |

## 17 · Migration and rollback

Feature tier, additive. Forward: merge; the flag (Q5) is the exposure control.
Backward: turn the flag off — no data to migrate; drafts and results files are
plain files a rollback leaves harmlessly in place. No point of no return; the only
irreversible act (package delete) is behind its own confirm and is user-initiated.

## 18 · Documentation updates

- **CLAUDE.md** — Index section paragraph under the JimsStuff block: the widened
  writer's validation stance, the draft-outside-the-package rule, the
  delete-after-confirm gate, flag name.
- **docs/FEATURE-PARITY.md** — Index mode row.
- **docs/adr/0014-index-mode-drafts-and-results.md** — where drafts live and why,
  latest-wins staging, the no-auto-apply rule for suggestions.
- **Memory** — update `fg-scanner-spec-2026-003-case-index.md`: phase 4 spec state,
  and after build: what shipped, flag name, walkthrough result.

## 19 · Phase plan

### Phase A — Widened writer (Core, cross-repo pinned)
- **Objective**: full answer surface in `PackageWriter`, contract-proven.
- **Delivers**: `IndexAnswer`, validation, round-trip test green in both repos.
- **Not in this phase**: any App code.
- **Done when**: AC-5, AC-6; goldens untouched.

### Phase B — Index section: open, browse, refuse
- **Objective**: the section exists and shows a package honestly.
- **Delivers**: shell entry + flag, open flow, document list, page viewer, verbatim
  refusals, router entries.
- **Not in this phase**: answering, drafts, export.
- **Done when**: AC-1, AC-2, AC-9, AC-10.

### Phase C — Answering and drafts
- **Objective**: Jim's five questions, resumable.
- **Delivers**: answer panel, Accept buttons with quotes, staging, draft store.
- **Not in this phase**: export, delete.
- **Done when**: AC-3, AC-4.

### Phase D — Export, share, delete, close-out
- **Objective**: the answers leave the laptop safely; the screen cleans up after
  itself.
- **Delivers**: export, email leg (Q2), delete-after-confirm, docs, review.
- **Done when**: AC-7, AC-8, AC-11; §21 all ticked.

## 20 · Prompt pack

`docs/specs/SPEC-2026-008-index-mode-PROMPTS.md` — 8 prompts: A1 writer TDD,
A2 cross-repo round trip, B1 shell+flag+open/refuse, B2 list+viewer+router,
C1 staging TDD, C2 draft store TDD, D1 export+share+delete, D2 `/code-review max`
+ docs + walkthrough script. Written in full once this spec is approved.

## 21 · Definition of done

- [ ] AC-1..10 green, failing tests first; AC-11 walked by Franz on the real window
- [ ] Both scanner test projects + portal suite green; `dotnet format` clean
- [ ] `/code-review max` run, findings resolved or recorded in §22
- [ ] §18 docs written (ADR-0014, CLAUDE.md, FEATURE-PARITY, memory)
- [ ] No release published; `Feature.IndexMode` off in any published channel
- [ ] Golden results bytes byte-identical to phase 2

## 22 · Sign-off

| | |
|---|---|
| **Review round answered** | ☑ 2026-09-30 — round A, https://claude.ai/artifact/6tFmTBm8TbFzruzSESdQ2b (docId SPEC-2026-008-rA) |
| **Franz approved** | ☑ 2026-09-30 — verdict approve on round A; all recommendations taken |
| **Built** | ☐ date: |
| **Verified in production** | ☐ date: |
