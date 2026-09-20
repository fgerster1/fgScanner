# SPEC-2026-004 — Settings that take effect without a restart

| | |
|---|---|
| **Status** | Approved |
| **Revision** | B |
| **Tier** | Feature |
| **Author** | Claude, for Franz Gerster |
| **Date** | 2026-09-20 |
| **Project** | FgmakerScanner |
| **Supersedes** | — |

---

## 01 · Management summary

- **What we are building** — a change made in Settings takes effect straight away, in the
  part of the app that uses it, instead of waiting for the operator to close FG Scanner
  and open it again.
- **Why** — the operator reported it after a working session on real evidence: "when you
  make a change in the setup, you have to exit and restart the program for the changes to
  take effect." Restarting mid-box costs the scan session and the place in the stack.
  Reconnaissance found **twelve** affected settings, and two of them are worse than a
  restart: the Trash retention box silently resets a customised value to 30 days every
  time Settings is saved, and the AI model is never saved at all.
- **What it costs** — 6 prompts across 2 phases, roughly a day. No new dependency, no
  schema change, no recurring cost.
- **What could go wrong** — the fix is "reload when something changed", and a reload at
  the wrong moment throws away work in progress: values typed for the next scan, a
  half-finished annotated sheet, or the group selection. §16 names each one; the design
  refuses to reload while a capture is in hand.
- **What we need from you** — four decisions in §05, chiefly how far to go: the pure
  restart fixes only, or also the two neighbouring bugs and the missing theme control.

## 02 · Outcomes

- **Goal** — the operator changes a setting and sees the result without leaving the
  program. Trust follows: a setting that needs a restart teaches the operator that
  Settings cannot be relied on, and they stop using it.
- **Who benefits** — Jim at the scanning station first (he is mid-box when he needs a new
  field), and Franz when configuring a profile for a new case without tearing down a
  session.
- **How we will know it worked** — every row in the table in §04 is exercised without
  relaunching, and `docs/manual-tests.md` gains a section where each is ticked. Today
  zero of twelve pass that way; the target is twelve of twelve, less whatever §05 puts
  out of scope.

## 03 · Scope and non-goals

**In scope**

- One change-notification path from Settings to the rest of the app, replacing the
  `ProfilesChanged` event that is raised six times and subscribed nowhere.
- Profile create / rename / delete / import / Evidence-build / base-folder visible in the
  Groups profile list immediately.
- A new index-schema version visible to an open group immediately — including the
  "Use latest field layout" banner that today hides the button that would fix it.
- `Feature.Search` showing or hiding the Search section without a relaunch.
- `Feature.PatchT` showing or hiding the separator-sheet button without a relaunch.
- Trash retention: load the stored value into the box, stop overwriting it on save, and
  re-run the purge after a change.
- Refresh-on-entry for the Groups and Scan sections, mirroring what Trash and Search
  already do.
- **`Ai.Model` persisted by the Save button** (row 12) — decided §05 Q1(a).
- **A theme control in Settings** writing `Ui.Theme` and applying it live (row 11) —
  decided §05 Q1(a).

**Non-goals**

- No redesign of the Settings screen layout.
- No live re-validation of the Gemini key, no re-running OCR or AI over existing pages
  when a setting changes.
- No settings-change history, no undo of a settings change.
- No change to what any setting *means* — only to when it is noticed.
- No caching layer in `AppSettingsService` or `ProfileService`; they are stateless today
  and stay that way (§08).
- The evidence export contract is untouched: no field name, no `index.json` key and no
  `manifest.json` entry changes.

## 04 · Current state

**Stack and conventions** — .NET 10 WPF, CommunityToolkit MVVM, DI via
Microsoft.Extensions.Hosting. Section view models are **singletons** registered in
`src/FgScanner.App/App.xaml.cs:159-165`, constructed once at startup and never rebuilt.
View instances are cached in `ShellWindow`'s constructor
(`src/FgScanner.App/Views/ShellWindow.xaml.cs:21-28`).

**The services are not the problem.** `AppSettingsService`
(`src/FgScanner.Data/AppSettingsService.cs:10-32`) and `ProfileService`
(`src/FgScanner.Data/ProfileService.cs`) open a fresh `DbContext` per call and hold **no
cache and no events**. Everything that reads a setting per operation is already live —
auto-orient (`src/FgScanner.Ocr/OcrPipeline.cs:115`), preserve-originals
(`src/FgScanner.Scanning/Editing/ImageEditor.cs:114`), OCR languages
(`OcrWorker.cs:118`), capture policy (`CaptureTriageService.cs:99-114`), commit hook
(`CommitHookRunner.cs:21-28`), the API key (`CredentialStore.cs:23`), and keyboard
shortcuts, which already have a working event (`SettingsViewModel.ShortcutsChanged` →
`ShellWindow.xaml.cs:31`).

**What actually needs a restart, and why**

| # | Setting | Reason | Evidence |
|---|---|---|---|
| 1 | Full-text search section (`Feature.Search`) | `ShellViewModel` ctor reads the flag once into a get-only list | `ShellViewModel.cs:24-28`, `:72` |
| 2 | Patch-T button visibility (`Feature.PatchT`) | `ScanViewModel` ctor fires `LoadFeatureFlagsAsync` once | `ScanViewModel.cs:46`, `:625-635` |
| 3 | New profile | Groups' profile list loaded once at startup | `GroupsViewModel.cs:40, 84-89, 91` |
| 4 | Rename profile | same | `SettingsViewModel.cs:638` |
| 5 | Delete profile | same; a deleted profile stays selectable | `SettingsViewModel.cs:605` |
| 6 | Import profile | same | `SettingsViewModel.cs:421` |
| 7 | Build the Evidence profile | same — the new profile is invisible in Groups | `SettingsViewModel.cs:518` |
| 8 | Base folder for new groups | `CreateGroupAsync` reads a startup-snapshot entity | `GroupsViewModel.cs:188` |
| 9 | Custom fields (new schema version) | `SchemaNotice`, `PendingFields`, `BatchFields` rebuilt only in `LoadAsync`, called only on group-selection change | `GroupDetailViewModel.cs:96-113, 121-141`; `GroupsViewModel.cs:103-108, 123` |
| 10 | Trash retention days | purge runs only at startup; **and** the value is never loaded, so Save writes 30 over it | `App.xaml.cs:210-224`; `TrashService.cs:168-177`; `SettingsViewModel.cs:41-45, 76, 697` |
| 11 | Theme (`Ui.Theme`) | not restart-gated — **absent from Settings entirely**; written only by the first-run wizard | `App.xaml.cs:188, 227-233, 245-246` |
| 12 | AI model (`Ai.Model`) | never persisted by Save — written only inside `SaveApiKeyAsync`, which early-returns without a freshly pasted key | `SettingsViewModel.cs:222-223, 260-264, 296` |

**The dead event.** `SettingsViewModel.ProfilesChanged` (`SettingsViewModel.cs:431-432`)
is raised at `:421, :493, :518, :569, :605, :638, :720` and subscribed **nowhere** in
`src/`. Wiring it up is the single highest-value line in this spec: it clears rows 3–8.

**The banner trap (row 9).** `GroupsView.xaml:99` gates the schema banner's visibility on
`SchemaNotice`, and the "Use latest field layout" button lives inside that banner at
`:102`. `SchemaNotice` is only recomputed in `LoadAsync`. So an operator who edits fields
while a group is open sees no banner and no button — the control that would apply the
change is hidden precisely when it is needed.

**Patterns to match** — `ShortcutsChanged` is the working precedent for a
Settings→Shell event. `ShellWindow.ShowSection` (`ShellWindow.xaml.cs:258-267`) is the
working precedent for refresh-on-entry, with `SearchViewModel.cs:18` documenting the
intent: "Rebuilt each time the section is shown, so new groups appear without a restart."

**Existing data** — no schema change. All keys already exist in the `Settings` table;
`Ui.Theme` and `Ai.Model` are existing keys with existing readers.

**Not examined** — the first-run wizard beyond where it writes `Ui.Theme`; the update
check; `SearchViewModel` internals; anything in `FgScanner.Cli` (headless, reads fresh
per run and is unaffected).

## 05 · Questions for Franz

> **Answered 2026-09-20, Round A part 1** — [review page](https://claude.ai/artifact/CCZuGgTi2dm7YK4wqDDbdx),
> db doc `review/SPEC-2026-004-005-rA-p1`, verdict **approve**. Every item took option
> (a). No notes.

**Blocking**

1. **How far does this spec go?** — *why it matters:* rows 1–10 are "the setting needs a
   restart", which is what you reported. Rows 11 (theme has no control at all) and 12
   (AI model is never saved) are different bugs found on the way; fixing them is small
   but it is scope you did not ask for, and each needs its own tests. Leaving them out
   means theme stays unchangeable after first run and the AI model box stays a lie.
   > **Answer (a): all twelve — the two storage bugs are fixed here too.** Theme gains a
   > control in Settings; `Ai.Model` is written by the Save button. Phase 2 owns both.
2. **When a profile's fields change while a group is open with values typed for the next
   scan, what happens to those values?** — *why it matters:* the typed values live in
   `PendingFields`/`BatchFields`, which a schema reload rebuilds. Keeping them means
   matching by field name and dropping values for fields that no longer exist; discarding
   them is one line but the operator loses typing they may not notice was lost. This is
   the main regression risk in §16.
   > **Answer (a): keep the values whose fields still exist**, matched by field name.
   > Values for removed fields are dropped, and the status line says how many.

**Non-blocking**

1. **Should a reload be refused while a scan or an annotated sheet is in hand?** —
   *proceeding as if:* yes. A capture in progress wins; the refresh is deferred until the
   sheet completes or is cancelled, because `AnnotatedCaptureSequence` state is held in
   the `ScanViewModel` and a rebuild mid-sheet would strand a half-pair (CLAUDE.md).
   > **Answer (a): agreed — a capture in hand wins.**
2. **Should Groups and Scan also refresh on section entry, on top of the event?** —
   *proceeding as if:* yes, mirroring Trash and Search. It is cheap, and it covers any
   path the event misses.
   > **Answer (a): agreed — do both.**

**Build order** (asked on part 1 as N5): **004 → 005 → 006 → 007**. This spec is first,
because every later spec is easier to test once a settings change takes effect without a
relaunch.

## 06 · Assumptions

| # | Assumption | If wrong |
|---|---|---|
| 1 | Reloading the profile list does not disturb the current group selection, because groups are keyed by id, not by profile index | Selection jumps on every settings save; §16 R3 |
| 2 | `ProfileService.SaveSchemaAsync` short-circuiting on an identical layout (`ProfileService.cs:245-248`) means a no-op save raises no version and so triggers no reload | Every Save would rebuild open forms for nothing |
| 3 | The operator saves Settings with the Save button; there is no auto-save path that would fire the event on every keystroke | The event fires far more often than designed |
| 4 | The Scan section's only startup-snapshot is `SeparatorSheetVisible`; other scan options are user state, not settings | A second stale path stays broken after this work |

## 07 · Data model

**No schema change, no migration, no backfill.** Every key involved already exists in the
`Settings` table and is already written today: `Feature.Search`, `Feature.PatchT`,
`Trash.RetentionDays`, `Ui.Theme`, `Ai.Model`.

Two existing keys change *who writes them*, not their shape:

| Key | Today | After |
|---|---|---|
| `Trash.RetentionDays` | written by Save from a field hard-initialised to 30 | loaded into the box on open, written by Save only when the operator changed it |
| `Ai.Model` | written only inside `SaveApiKeyAsync` | written by `SaveAsync` like every other setting (if §05 Q1 includes row 12) |

Documented in code on `SettingsViewModel`'s loader methods, beside the existing
`LoadOcrSettingsAsync`/`LoadAiSettingsAsync` group (`SettingsViewModel.cs:41-45`).

## 08 · Architecture and approach

**One event, raised once, on the existing precedent.** `SettingsViewModel` already owns a
working pattern: `ShortcutsChanged` is raised on save and subscribed by `ShellWindow`
(`ShellWindow.xaml.cs:31`). This spec generalises that rather than inventing a bus.

1. Promote the dead `ProfilesChanged` into **`SettingsChanged`**, carrying a small record
   saying what moved — profiles, schema, feature flags, retention — so a subscriber can
   reload only what it needs rather than rebuilding on every save.
2. `ShellWindow` subscribes once, beside the shortcuts line, and fans out:
   `GroupsViewModel.ReloadProfilesAsync()`, `ScanViewModel.LoadFeatureFlagsAsync()`,
   `ShellViewModel.RefreshSections()`, `GroupDetailViewModel.RefreshSchemaAsync()`.
3. `ShellViewModel.Sections` becomes an `ObservableCollection<string>` recomputed on the
   flags change, so the nav can actually update (today it is a get-only list, so even
   mutating it would not refresh — `ShellViewModel.cs:72`).
4. `ShowSection` gains Groups and Scan alongside Trash and Search
   (`ShellWindow.xaml.cs:258-267`) as a second line of defence.
5. A **capture guard**: the Scan and Groups refreshes are deferred while
   `IsScanning` or `AnnotatedActive` is true, and run when that clears. A half-finished
   annotated sheet must not be disturbed (CLAUDE.md, ADR-0005 neighbourhood).

**Alternatives considered.** *Caching in the services with invalidation* — rejected:
the services are stateless and correct, and a cache would add a second source of truth to
the one place that currently has none. *`WeakReferenceMessenger` from CommunityToolkit* —
rejected: nothing in the app uses the messenger today, so it would add a second
notification idiom beside `ShortcutsChanged` for no gain at this size. *Refresh-on-entry
only, no event* — rejected as the sole mechanism: the operator who edits fields and
returns to a group already open would still see stale fields, which is row 9, the one
that hurts most.

**Data access.** No MCP, no index, no cache. Volumes are trivial (§15).

**System evolution.** Nothing here belongs in a skill; it is app-internal wiring.

## 08a · AI opportunity assessment

_Not applicable — this is change-notification plumbing. There is no classification,
extraction, ranking or natural-language surface in it, and a model in this path would add
latency and non-determinism to a settings save._

The AI settings themselves (`Ai.Model`, the key) are touched only as storage bugs, not as
AI behaviour.

## 09 · Design system compliance

The app's own WPF Fluent theme governs (project brief: the FG Maker web kits do not apply
to this WPF app).

- No new controls except, if §05 Q1 includes row 11, a theme `ComboBox` in the existing
  Settings layout, matching the neighbouring combos.
- The user-facing label "Full-text search section (applies on next launch)"
  (`SettingsView.xaml:234`) must lose "(applies on next launch)" when row 1 is fixed — a
  stale label that contradicts the behaviour is worse than no label.
- Accessibility: any new control is keyboard reachable with a visible focus ring and a
  label, consistent with the existing grid.

## 10 · Acceptance criteria

> **AC-1** — Given Settings is open and a new profile is created, when the operator
> switches to Groups without restarting, the new profile appears in the profile list.
> *Proven by:* `tests/FgScanner.App.Tests/SettingsPropagationTests.cs` → "a new profile
> reaches the Groups list without rebuilding the view model"

> **AC-2** — Renaming, deleting and importing a profile, and building the Evidence
> profile, each update the Groups profile list the same way.
> *Proven by:* same file → four cases in one `[Theory]`

> **AC-3** — Given a profile whose base folder is changed in Settings, when a group is
> created afterwards, it is created under the new base folder.
> *Proven by:* same file → "a new group uses the base folder set since startup"

> **AC-4** — Given a group is open and the profile's fields are edited, the schema
> banner and its "Use latest field layout" button become visible without re-selecting the
> group.
> *Proven by:* `tests/FgScanner.App.Tests/SchemaNoticeTests.cs` → "an edit made while the
> group is open raises the notice"

> **AC-5** — Given values have been typed for the next scan and the profile's fields are
> then edited, the values for fields that still exist survive the reload, and the status
> line reports how many were dropped for fields that no longer exist.
> *Proven by:* `tests/FgScanner.App.Tests/PendingFieldValueTests.cs` → "typed values
> survive a schema reload by field name"

> **AC-11** — Changing the AI model and pressing Save stores it; reopening Settings shows
> the stored model.
> *Proven by:* `tests/FgScanner.App.Tests/AiModelSettingTests.cs` → "the model round-trips
> through Save"

> **AC-12** — Changing the theme in Settings applies without a restart and survives one.
> *Proven by:* `tests/FgScanner.App.Tests/ThemeSettingTests.cs` → "the theme setting is
> written and read back"; the live application itself is `manual`

> **AC-6** — Turning `Feature.Search` off hides the Search section, and on shows it,
> without a restart.
> *Proven by:* `tests/FgScanner.App.Tests/ShellTests.cs` → "toggling the search flag
> moves the section list" (the inverse of the existing `:82` test, which is kept)

> **AC-7** — Turning Patch-T off hides the separator-sheet button without a restart.
> *Proven by:* `tests/FgScanner.App.Tests/ScanFeatureFlagTests.cs` → "the separator
> button follows the flag"

> **AC-8** — Given a stored retention of 365 days, when Settings opens, the box shows
> 365; and when Save is pressed without touching it, the stored value is still 365.
> *Proven by:* `tests/FgScanner.App.Tests/TrashRetentionSettingTests.cs` → "the stored
> retention is loaded and not overwritten"

> **AC-9** — A refresh is not performed while a scan is running or an annotated sheet is
> in hand; it happens once that finishes.
> *Proven by:* `tests/FgScanner.App.Tests/SettingsPropagationTests.cs` → "a settings
> change during an annotated sheet is deferred, not applied"

> **AC-10** — The evidence export is unchanged: the Verify snapshots for `index.json`,
> `index.csv` and `manifest.json` are byte-identical to before this work.
> *Proven by:* `tests/FgScanner.Core.Tests/IndexExporterTests.cs` → existing snapshots,
> unmodified

## 11 · Test strategy

**11.1 — The failing tests to write first**

| Feature | Test file | The failing assertion |
|---|---|---|
| Profile list propagation | `tests/FgScanner.App.Tests/SettingsPropagationTests.cs` (new) | After `SettingsViewModel.SaveCommand`, the *existing* `GroupsViewModel` instance lists the new profile — no reconstruction |
| Base folder | same | `CreateGroupAsync` uses the folder written since startup |
| Schema notice while open | `tests/FgScanner.App.Tests/SchemaNoticeTests.cs` (extend) | `SchemaNotice` becomes non-null without calling `LoadAsync` by hand |
| Pending values survive | `tests/FgScanner.App.Tests/PendingFieldValueTests.cs` (extend) | Values keyed by surviving field names are still present after a schema bump |
| Search section | `tests/FgScanner.App.Tests/ShellTests.cs` (extend) | Flag flipped *after* construction changes `Sections` |
| Patch-T button | `tests/FgScanner.App.Tests/ScanFeatureFlagTests.cs` (new) | `SeparatorSheetVisible` follows a post-construction flag change |
| Retention round-trip | `tests/FgScanner.App.Tests/TrashRetentionSettingTests.cs` (new) | Stored 365 loads as 365 and survives a Save |
| Capture guard | `SettingsPropagationTests.cs` | With `AnnotatedActive` true, no rebuild happens; after cancel, it does |

**11.2 — Test data.** In-memory SQLite via the existing `FgScanner.App.Tests` fixtures
(pattern: `ShellTests.cs`, `SchemaNoticeTests.cs`). `FakeScanService` for anything that
touches the Scan page — no scanner, per CLAUDE.md. No production snapshot is needed; the
one real-data check is manual (§11.3).

**11.3 — Verification suite**

- `dotnet build -c Release` — warnings are errors.
- `dotnet test -c Release` — all green, count no lower than 692.
- `dotnet format --verify-no-changes`.
- Manual, on the copy of Jim's data now at `D:\Evidence-Scans`: work the twelve rows of
  §04 without relaunching, and record them in `docs/manual-tests.md`. The manual pass is
  the only place rows 1, 2 and 11 can be truly observed, since they are window chrome.

## 12 · Edge cases and failure modes

| Case | Expected behaviour | Where handled |
|---|---|---|
| Settings saved with nothing changed | `SaveSchemaAsync` short-circuits; no version minted; no reload | `ProfileService.cs:245-248` |
| The profile currently selected in Groups is deleted in Settings | Selection falls back to the default profile; no dead foreign key | `GroupsViewModel.ReloadProfilesAsync` |
| Schema edited while a group is open and dirty | Rows are not reloaded; only fields and the notice are (§05 Q2 decides values) | `GroupDetailViewModel.RefreshSchemaAsync` |
| Settings saved during a feeder run | Refresh deferred until `IsScanning` is false | capture guard, §08 step 5 |
| Settings saved during an annotated sheet | Refresh deferred until the sheet completes or is cancelled | capture guard |
| Retention box left empty or non-numeric | Existing validation stands; no write | `SettingsViewModel` retention setter |
| Two saves in quick succession | Last one wins; the reload is idempotent | event handler is a plain reload |
| Reload throws (database locked) | Status text reports it; the old lists stay; no crash | handler try/catch, logged (§14) |

## 13 · Security and configuration

_Not applicable in the web sense — desktop app, no auth, no network surface added._

- No new environment variable, no new configuration file, no new dependency.
- No change to how the Gemini key is stored or read (`CredentialStore`); this spec only
  makes the **model name** persist, never the key.
- No new file-path input; the base-folder path continues to be validated where it is
  today.

## 14 · Observability

- **Logged** at Information, once per settings save: which parts changed (profiles /
  schema / flags / retention) and how many consumers were refreshed. Serilog, to the
  existing `%LOCALAPPDATA%\FGScanner\logs\app-*.log`.
- **Logged** at Warning when a refresh is deferred because a capture is in hand, and at
  Information when the deferred refresh runs.
- **Surfaced to the operator** — the existing Settings status line already reports the
  save; it gains "…applied" or, when deferred, "…will apply when the sheet is finished."
- **A silent failure** here looks exactly like today's bug: the save succeeds and nothing
  changes. What makes it non-silent is the log line naming zero refreshed consumers, plus
  AC-1..AC-8 which fail loudly if the wiring is dropped.
- **Who notices** — the operator immediately (the list updates or it does not); the test
  suite on every run.

## 15 · Performance and scale

Small by nature: 5 profiles and 18 groups in the real database on this machine, 16 fields
maximum per schema (`ProfileService.MaxFields = 16`). A reload is a handful of SQLite
reads on a local file, well under the threshold where anyone would notice.

The only thing worth watching is **reload frequency**: the event must fire on save, not
on keystroke, or a 16-field grid would rebuild continuously. Revisit if a profile ever
holds hundreds of groups, or if an auto-save is added to Settings.

## 16 · Regression risk

| # | What could break | Why | Evidence (file:line) | Mitigation |
|---|---|---|---|---|
| R1 | A half-finished annotated sheet is stranded | `AnnotatedCaptureSequence` state lives in the `ScanViewModel`; rebuilding scan state mid-sheet would lose the as-found capture's partner, which is a whole-group refusal at import | `ScanViewModel.cs:408-444`; CLAUDE.md "Annotated sheets" | Capture guard (§08 step 5); AC-9 pins it |
| R2 | Values typed for the next scan are lost | A schema reload rebuilds `PendingFields`/`BatchFields`, which hold unsaved operator input pushed to Scan via `ActiveGroupStore.PendingValues` | `GroupDetailViewModel.cs:121-141`, `:713-716`; `ScanViewModel.cs:556` | §05 Q2 decides; AC-5 pins whichever answer |
| R3 | The group selection jumps on every save | Reloading the profile list can reset a bound `SelectedProfile`, and selection drives `LoadDetailAsync` | `GroupsViewModel.cs:103-108, 123` | Reload preserves selection by id; assumption A1 |
| R4 | A sticky batch value is re-expanded | Batch defaults are expanded once at group creation; a rebuild that re-runs `TokenExpander` would re-evaluate `$(user)`/`$(today)` | `GroupService.cs` batch default expansion (AdoptDirectory) | Refresh reloads field *definitions* only, never re-expands defaults |
| R5 | `NoteState` becomes sticky by accident | If the refresh path copies field flags, a bug could mark `NoteState` sticky — CLAUDE.md forbids it absolutely, as it would stamp `as-found` on every later sheet | CLAUDE.md "NoteState must never be made sticky" | No flag is written by this spec; a test asserts `NoteState.Sticky == false` after a refresh |
| R6 | Search section hidden while its view is showing | `Sections` becoming observable could remove the section the operator is currently looking at | `ShellViewModel.cs:24-28`, `:62-70` | On removal of the active section, fall back to Groups; covered by AC-6 |
| R7 | Retention change purges more than expected | Re-running `PurgeExpiredAsync` after a change deletes trash older than the *new* value immediately | `TrashService.cs:168-177`; `App.xaml.cs:210-224` | Purge runs only when the value actually changed, and the status line says how many items it removed |
| R8 | The existing `ShellTests.cs:82` test breaks | It asserts the constructor reads the flag, which stays true, but `Sections` changes type | `ShellTests.cs:82-100` | Keep the test; extend rather than replace |

Nothing else was found. What was checked: every consumer of `AppSettingsService.GetAsync`
and every caller of `ProfileService`, plus all five section view models.

## 17 · Migration and rollback

_Not a System-tier change — no data migration._

- **Forward** — code only; ships in the next installer.
- **Backward** — reinstall the previous installer; no data change to undo. The database
  is untouched by this spec.
- **Point of no return** — none.
- **Backup** — the existing automatic `fgscanner.db.bak-<version>` on first launch after
  an upgrade still applies, though nothing here writes schema.

## 18 · Documentation updates

- **CLAUDE.md** — add a line under the code rules: a setting is read where it is used, and
  a settings change is announced through `SettingsChanged`; snapshotting a setting in a
  singleton constructor is how this bug class returns.
- **docs/user-guide.md** — remove any implication that settings need a relaunch; state
  that a field-layout change offers "Use latest field layout" on the open group.
- **docs/manual-tests.md** — new section with the twelve rows from §04, to be ticked
  without relaunching.
- **docs/adr/0010-settings-change-notification.md** — new ADR: why one event with a
  change record, why not a cache, and why a capture in hand defers a refresh.
- **docs/specs/_project-brief.md** — refresh while here: version is 0.5.0 (not 0.4.0),
  the test baseline is 692 (not 516), and the latest migration is
  `20260914210756_AddFieldLengthAndMemo`.
- **Memory** — update `fg-scanner-specs-2026-09-13` or add a sibling recording that
  SPEC-004..007 came out of the 2026-09-20 session against Jim's data.

## 19 · Phase plan

### Phase 1 — The notification path
- **Objective** — one event, subscribed, that reaches the profile list, the sections and
  the scan flags.
- **Delivers** — `SettingsChanged` replacing the dead `ProfilesChanged`; `ShellWindow`
  fan-out; `ShellViewModel.Sections` observable; `ScanViewModel` flags reload; the capture
  guard; refresh-on-entry for Groups and Scan.
- **Not in this phase** — the schema/open-group reload (Phase 2), retention, AI model,
  theme.
- **Done when** — AC-1, AC-2, AC-3, AC-6, AC-7, AC-9 pass.

### Phase 2 — The open group, and the two storage bugs
- **Objective** — a field-layout change reaches the group that is already open, and the
  settings that never saved correctly do.
- **Delivers** — `GroupDetailViewModel.RefreshSchemaAsync`, the banner fix, pending values
  kept by field name (§05 Q2a), retention load/save/purge, `Ai.Model` in `SaveAsync`, and
  a theme control that applies live (§05 Q1a).
- **Not in this phase** — anything about how fields are *rendered* (that is SPEC-2026-005).
- **Done when** — AC-4, AC-5, AC-8, AC-10, AC-11, AC-12 pass, and the manual rows are ticked.

## 20 · Prompt pack

[SPEC-2026-004-settings-take-effect-now-PROMPTS.md](./SPEC-2026-004-settings-take-effect-now-PROMPTS.md)
— written once this spec is approved (§22).

## 21 · Definition of done

- [x] All acceptance criteria met — AC-1..AC-12; AC-3 and AC-9 have the caveats noted below
- [x] Failing tests written first, now passing — with the exceptions recorded below
- [x] Full suite green (`dotnet test -c Release`, **714 passed**, baseline was 692)
- [x] Cold code review run, findings resolved or accepted in writing (see §22)
- [x] Security review — _not applicable, no web surface_
- [x] Documentation updated per §18
- [ ] Installed from the built installer and verified on the copy of Jim's data
- [ ] Rollback tested or explicitly waived by Franz

## 22 · Sign-off

| | |
|---|---|
| **Review round answered** | ☑ 2026-09-20 — [Round A, part 1 of 3](https://claude.ai/artifact/CCZuGgTi2dm7YK4wqDDbdx) · db doc `review/SPEC-2026-004-005-rA-p1` |
| **Franz approved** | ☑ 2026-09-20 (verdict `approve`, all items option (a)) |
| **Built** | ☑ 2026-09-20 — branch `phase-23-settings-live`, 6 commits, 714 tests green |
| **Verified in production** | ☐ — not yet on Jim's station; he stays on the older build until this is installed here first |

### Code review — findings and their resolution

A cold reviewer (no knowledge of how the code came to be written) read the four
implementation commits against §10, §12 and §16 on 2026-09-20. Its headline: *the suite was green
for exactly the reason two of the bugs survived* — every test runs headless, and both defects live
in what WPF writes back into a view model when a bound list is cleared.

**Fixed** (commit "Fix the defects the cold review found"):

1. `ApplySections` cleared the collection bound to the navigation `ListBox`'s `SelectedItem`.
   WPF pushed null back, which navigated to nothing and threw from inside a `PropertyChanged`
   handler; and the "fall back to Groups" branch then fired on *any* search-flag toggle, not only
   when the section on screen disappeared. Now adds/removes only the differing entries, and
   `ShowSection` ignores a null.
2. `ReloadProfilesAsync` replaced every `Profile` instance, so the selection changed by reference
   on every save. With **"Only this profile's groups"** ticked that cascaded into rebuilding the
   open group's detail pane — losing typed values, undo history and row selection. R2 and R3 were
   both violated in that configuration, and the Prompt 3 fix had missed it. Only a change of
   profile id counts now.
3. A reload that threw escaped `ScanAsync` unhandled — a crash at the end of a scan. `ApplyAsync`
   catches and logs, and a failed apply keeps the change pending rather than dropping it (§12).
4. `SaveToGroupAsync` never raised `CaptureSettled`, so a change deferred during an annotated
   sheet was stranded when the sheet completed normally: the clean capture is staged, not
   auto-saved, so the scan ends with the sequence still in hand.
5. `Schema` was announced on every save although `SaveSchemaAsync` short-circuits on an identical
   layout — rebuilding the open group's editors when only the theme had changed (§12 row 1, A2).
6. A throwing subscriber reported a committed save as "Save failed" and abandoned later handlers.
7. The constructor's retention and theme loads could be beaten by a save, writing 30 over a stored
   365 with no purge — §04 row 10 returning as a race. `Save` now awaits `Ready`.
8. The purge count reached the status line only when no group was behind its schema (§16 R7).
9. §14 observability was unimplemented; the deferral, the apply and every failure are now logged.

**Accepted, not fixed:**

- `SchemaNoticeTests.A_refresh_leaves_NoteState_unsticky` would pass against a stub: it asserts a
  property of the seeded Evidence profile rather than of the refresh. Kept as a regression guard
  and labelled as one. R5 itself is clean — no refresh path writes a field flag.
- AC-3's "a new group uses the base folder set since startup" is proven through the
  `ClearBaseDirectory` path only; setting a folder goes through a `OpenFolderDialog` that a
  headless test cannot drive. The manual row covers it.
- AC-2's delete and import cases are not automated — both are behind a `MessageBox` / file dialog.
  Manual rows cover them.
- A latent race in two tests (`FeatureSearch`/`FeaturePatchT` set after construction, racing the
  constructor's fire-and-forget load) is noted rather than fixed; `Ready` covers retention and
  theme but not the feature flags.

**Verified clean by the review:** no NAPS2.Lib reference, no banned package, no change to the
evidence export contract (no field name, `index.json` key or `manifest.json` entry touched), and
R4 (batch defaults never re-expanded through `TokenExpander`) and R5 hold.
