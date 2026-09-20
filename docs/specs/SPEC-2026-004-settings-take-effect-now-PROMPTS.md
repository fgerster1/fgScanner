# SPEC-2026-004 — Prompt pack

Prompts for [SPEC-2026-004-settings-take-effect-now.md](./SPEC-2026-004-settings-take-effect-now.md).
Work through them in order. Each stops at a checkpoint; paste the results back before
starting the next.

Branch: `phase-23-settings-live`. Approved 2026-09-20; build order 004 → 005 → 006 → 007.

---

```
PROMPT 1 of 6 — The change notification, and the profile list
Spec: SPEC-2026-004 §04, §08, §10, §11    Phase: 1    Depends on: nothing

Read SPEC-2026-004 §04 and §08 before starting. Create the branch
phase-23-settings-live from main if it does not exist.

Context you need: SettingsViewModel.ProfilesChanged (SettingsViewModel.cs:431-432) is
raised seven times and subscribed nowhere. GroupsViewModel.ReloadProfilesAsync
(GroupsViewModel.cs:91) has exactly one caller — startup. Section view models are DI
singletons (App.xaml.cs:159-165). The working precedent for a Settings→Shell event is
ShortcutsChanged, subscribed at ShellWindow.xaml.cs:31.

Do only this:
1. Write the failing tests FIRST in tests/FgScanner.App.Tests/SettingsPropagationTests.cs:
   - a profile created through SettingsViewModel.SaveCommand appears in an EXISTING
     GroupsViewModel instance (no reconstruction — that is the whole point);
   - the same for rename, delete and import, as a [Theory];
   - a group created after a base-folder change uses the new folder
     (GroupsViewModel.cs:188 currently reads a startup-snapshot entity).
2. Replace ProfilesChanged with SettingsChanged, carrying a small record that says what
   moved (profiles / schema / flags / retention). Raise it where ProfilesChanged was
   raised.
3. Subscribe once in ShellWindow, beside the ShortcutsChanged line, and fan out to
   GroupsViewModel.ReloadProfilesAsync() + RefreshAsync().
4. Make the reload preserve the current selection by id (§06 A1, §16 R3).

Do NOT in this prompt: touch ShellViewModel.Sections, ScanViewModel, the open-group
schema reload, retention, the AI model or the theme. Those are Prompts 2, 3 and 4.

Stop when the checkpoint below is satisfied and report the results.

CHECKPOINT 1 — paste back:
  · dotnet test -c Release → total count and "passed"; no lower than 692
  · the new test names and that they failed before the fix (paste the first failing run)
  · grep -rn "ProfilesChanged" src/ → no hits left
  · grep -rn "SettingsChanged" src/ → the raise sites and exactly one subscription
  · confirm no existing test was modified
```

```
PROMPT 2 of 6 — Sections, scan flags, the capture guard, section entry
Spec: SPEC-2026-004 §08, §10 (AC-6, AC-7, AC-9), §16 R1, R6    Phase: 1    Depends on: Prompt 1 (green)

Read SPEC-2026-004 §08 steps 3–5 and §16 R1 before starting.

Context: ShellViewModel resolves Feature.Search once in its constructor into a get-only
list (ShellViewModel.cs:24-28, :72). ScanViewModel fires LoadFeatureFlagsAsync once
(ScanViewModel.cs:46, :625-635). ShowSection refreshes Trash and Search only
(ShellWindow.xaml.cs:258-267).

Do only this:
1. Failing tests FIRST:
   - tests/FgScanner.App.Tests/ShellTests.cs — extend: a Feature.Search flag flipped
     AFTER construction changes Sections. KEEP the existing test at :82 as it is.
   - tests/FgScanner.App.Tests/ScanFeatureFlagTests.cs (new) — SeparatorSheetVisible
     follows a post-construction Patch-T change.
   - tests/FgScanner.App.Tests/SettingsPropagationTests.cs — extend: with
     AnnotatedActive true, no rebuild happens; after CancelAnnotated, it does.
2. Make ShellViewModel.Sections an ObservableCollection<string> recomputed on the flags
   change. If the section currently being shown is removed, fall back to Groups (§16 R6).
3. Re-run ScanViewModel's flag load on the event.
4. Add the capture guard: defer the Scan and Groups refreshes while IsScanning or
   AnnotatedActive, and run the deferred refresh when that clears. A half-finished
   annotated sheet must never be rebuilt (CLAUDE.md).
5. Add Groups and Scan to ShowSection's refresh-on-entry, mirroring Trash and Search.
6. Delete "(applies on next launch)" from SettingsView.xaml:234 — the label is now false.

Do NOT in this prompt: the open-group schema reload, retention, AI model, theme.

Stop when the checkpoint below is satisfied and report the results.

CHECKPOINT 2 — paste back:
  · dotnet test -c Release → count and "passed"
  · the three new/extended test names, failing first
  · grep -n "applies on next launch" src/ → no hits
  · confirm ShellTests.cs:82's original test still exists and passes
```

```
PROMPT 3 of 6 — The open group learns about a field-layout change
Spec: SPEC-2026-004 §04 row 9, §05 Q2, §10 (AC-4, AC-5), §16 R2, R4, R5    Phase: 2    Depends on: Prompt 2 (green)

Read SPEC-2026-004 §04 (the banner trap) and §05 Q2 before starting.

Context: GroupDetailViewModel.LoadAsync (:96-153) is the only place SchemaNotice,
PendingFields and BatchFields are built, and it runs only on group-selection change. The
"Use latest field layout" button lives INSIDE the banner gated on SchemaNotice
(GroupsView.xaml:99-103), so it is invisible exactly when it is needed.

§05 Q2 was answered (a): keep the typed values whose fields still exist, matched by
field name; drop the rest and say how many in the status line.

Do only this:
1. Failing tests FIRST:
   - tests/FgScanner.App.Tests/SchemaNoticeTests.cs — extend: an edit made while the
     group is open raises SchemaNotice without a hand-called LoadAsync.
   - tests/FgScanner.App.Tests/PendingFieldValueTests.cs — extend: typed values for
     surviving fields are still there after a schema bump; a value for a removed field is
     gone and the count is reported.
   - a test asserting NoteState.Sticky is still false after a refresh (§16 R5 — CLAUDE.md
     forbids NoteState ever being sticky).
2. Add GroupDetailViewModel.RefreshSchemaAsync: reloads field definitions, the pinned and
   latest schema versions and SchemaNotice, rebuilds PendingFields/BatchFields preserving
   values by name. It must NOT reload rows and must NOT re-expand batch defaults through
   TokenExpander (§16 R4 — $(user)/$(today) would be re-evaluated).
3. Call it from the SettingsChanged fan-out, behind the capture guard from Prompt 2.

Do NOT in this prompt: retention, AI model, theme, or anything about how fields are
RENDERED — rendering is SPEC-2026-005.

Stop when the checkpoint below is satisfied and report the results.

CHECKPOINT 3 — paste back:
  · dotnet test -c Release → count and "passed"
  · the new test names, failing first
  · confirm rows are not reloaded by RefreshSchemaAsync (say which method reloads rows)
  · confirm no TokenExpander call was added
```

```
PROMPT 4 of 6 — Retention, the AI model, and the theme
Spec: SPEC-2026-004 §04 rows 10-12, §07, §10 (AC-8, AC-11, AC-12)    Phase: 2    Depends on: Prompt 3 (green)

Read SPEC-2026-004 §04 rows 10-12 and §07 before starting. These are the two storage
bugs and the missing control; Franz included them in scope (§05 Q1a).

Context:
- Retention: SettingsViewModel has no LoadRetentionAsync (loaders at :41-45), _retentionDays
  is hard-initialised to 30 (:76), and SaveAsync writes it back (:697) — so a stored 365
  is silently overwritten with 30 on every save. PurgeExpiredAsync runs only at startup
  (App.xaml.cs:210-224).
- Ai.Model is written only inside SaveApiKeyAsync (:296), which early-returns without a
  freshly pasted key (:260-264), so the model box never persists.
- Ui.Theme is written only by the first-run wizard (App.xaml.cs:245-246) and applied only
  at startup (:188, ApplyTheme :227-233). There is no control in SettingsView.xaml.

Do only this:
1. Failing tests FIRST:
   - tests/FgScanner.App.Tests/TrashRetentionSettingTests.cs (new): a stored 365 loads as
     365, and survives a Save that did not touch it.
   - tests/FgScanner.App.Tests/AiModelSettingTests.cs (new): the model round-trips through
     SaveAsync with no API key involved.
   - tests/FgScanner.App.Tests/ThemeSettingTests.cs (new): the theme setting is written
     and read back.
2. Add LoadRetentionAsync to the loader chain; stop Save overwriting an untouched value;
   re-run PurgeExpiredAsync only when the value actually changed, and report how many
   items it removed (§16 R7).
3. Write Ai.Model in SaveAsync.
4. Add a theme ComboBox to SettingsView.xaml writing Ui.Theme, and apply it live —
   ApplyTheme will need to be reachable from the view model; move or expose it, do not
   duplicate it.

Do NOT in this prompt: touch the API key storage, the credential store, or anything
about OCR/AI behaviour. Only the model NAME persists here.

Stop when the checkpoint below is satisfied and report the results.

CHECKPOINT 4 — paste back:
  · dotnet test -c Release → count and "passed"
  · the three new test names, failing first
  · with the app running: change the theme and confirm it applies without a restart
  · confirm the retention box shows a stored 365 rather than 30
```

```
PROMPT 5 of 6 — Code review  [START A NEW SESSION FOR THIS]
Spec: SPEC-2026-004    Depends on: Prompt 4 (green)

Run /code-review max over the changes made in Prompts 1..4.

Then check compliance with the spec, which is the primary criterion: does the code do
what SPEC-2026-004 §10 says, and only that? Report any place the implementation drifted
from the spec — drift is either a bug in the code or a spec that needs revising, and both
need saying out loud.

Pay particular attention to §16: the capture guard (R1), pending values (R2), selection
preservation (R3), batch defaults not being re-expanded (R4), NoteState never becoming
sticky (R5), and the active section not vanishing (R6).

Resolve every correctness finding. For anything you decline to change, write one line in
the spec's §22 saying what and why — an accepted finding is a decision, not an oversight.

CHECKPOINT 5 — paste back:
  · the findings list with each one's resolution
  · dotnet test -c Release → count and "passed"
  · dotnet format --verify-no-changes → clean
```

```
PROMPT 6 of 6 — Documentation, ADR and memory
Spec: SPEC-2026-004 §18    Depends on: Prompt 5 (green)

Do only this:
1. Write docs/adr/0010-settings-change-notification.md: one event carrying what changed,
   why not a cache in the services, and why a capture in hand defers a refresh.
2. CLAUDE.md — add under the code rules: a setting is read where it is used, and a change
   is announced through SettingsChanged; snapshotting a setting in a singleton constructor
   is how this bug class comes back.
3. docs/manual-tests.md — add a section with the twelve rows from SPEC-2026-004 §04, to be
   ticked WITHOUT relaunching. Leave them unticked; they are for Franz at the station.
4. docs/user-guide.md — remove any implication that settings need a relaunch; describe the
   "Use latest field layout" banner appearing on an open group.
5. docs/specs/_project-brief.md — refresh the stale facts: version 0.5.0, test baseline
   692, latest migration 20260914210756_AddFieldLengthAndMemo.
6. Tick §21 in the spec and fill §22's Built row.

Do NOT in this prompt: change any source file, or tick a manual-test row Franz has not
performed.

CHECKPOINT 6 — paste back:
  · git diff --stat → docs only, no src/
  · the ADR filename
  · dotnet test -c Release → unchanged count, still green
```
