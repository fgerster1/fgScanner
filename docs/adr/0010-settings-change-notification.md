# ADR-0010 — One announced settings change, and a capture in hand wins

**Status:** accepted, 2026-09-20

## Context

An operator reported that changing anything in Settings required closing and reopening FG
Scanner. Reconnaissance found **twelve** affected settings — see SPEC-2026-004 §04 for the table
— and the cause was never the services. `AppSettingsService` and `ProfileService` open a fresh
`DbContext` per call and hold no cache, so everything read per operation (auto-orient,
preserve-originals, OCR languages, capture policy, commit hook, the API key) was already live.

What was frozen was everything a **view model read once in its constructor**. The five section
view models are DI singletons built at startup and never rebuilt, so `Feature.Search` decided the
navigation list forever, `Feature.PatchT` decided the separator button forever, and the profile
list was a startup snapshot whose `Profile` entities came from a context disposed long ago.

`SettingsViewModel` already had an event for this — `ProfilesChanged`, raised seven times and
subscribed **nowhere**. It had presumably worked once.

Two of the twelve were worse than needing a restart. The trash-retention box was hard-initialised
to 30 and written back on every save, so an operator who set 365 lost it the next time they
pressed Save for any reason; this was observed happening on real data on 2026-09-20. The AI model
was written only inside the "validate and store the API key" path, so changing it alone persisted
nothing.

## Decision

**One event, naming what moved.** `SettingsChanged` carries a `SettingsChange` flags value
(`Profiles`, `Schema`, `Flags`, `Retention`) so a subscriber reloads only what it must. A bare
"something changed" signal would rebuild every section on every save, including the form fields an
operator is typing into.

**The event returns a `Task` and the raiser awaits it.** With `Action`, a command would report
"saved" while the reload was still running, and the propagation could only be tested by sleeping.
Each handler is invoked and awaited individually through `GetInvocationList`, inside its own
`try`/`catch`: `Invoke()` on a multicast `Func` returns only the last handler's task, and one
subscriber throwing must not abandon the others or turn a save that committed every write into
"Save failed".

**The subscription lives in `ShellViewModel`, not the window's code-behind**, beside the existing
"scan into this group" wiring and for the same reason: a `Window` cannot be constructed in a test,
and the behaviour worth pinning is that an *existing* view model updates.

**A capture in hand wins.** A change arriving while a scan is running or an annotated sheet is
part-captured is held, not applied. Rebuilding the Scan page mid-sheet would strand an as-found
capture with no clean partner — a whole-group refusal at import, discovered long after the box is
back on the shelf (CLAUDE.md). `ScanViewModel` exposes `CaptureInHand` and an **awaited**
`CaptureSettled`, raised where paper is released: the end of a scan, a batch, a save to group, and
an abandoned sheet. A deferred reload is therefore part of the operation that freed the page
rather than a race against it, and a failed apply leaves the change pending instead of dropping it.

**The open group is told, but keeps its own layout.** `RefreshSchemaAsync` re-reads the field
definitions and the schema notice without reloading the rows, carrying values typed for the next
scan across by field name. Rows are untouched because nothing about a profile change alters a
page, and reloading would discard an edit in progress.

## Consequences

- A setting is read where it is used, and a change is announced. **Snapshotting a setting in a
  singleton constructor is how this bug class comes back** — that line is now in CLAUDE.md.
- `Schema` is announced only when a version was actually minted. `ProfileService.SaveSchemaAsync`
  short-circuits on an identical layout, and announcing anyway rebuilt the open group's editors on
  every save, including one that changed only the theme.
- The group **list** is deliberately not refreshed on a profile change, and a profile reload that
  returns the same id does not count as a selection change. Both replace `Group` instances, which
  rebuilds the open group's detail pane and takes the operator's typed values with it. A renamed
  profile showing late in the group list is cosmetic; losing typed values is not.
- Settings gains a theme control and its key moves into `ThemeSetting`; the theme was previously
  unreachable after first run.
- `Save` awaits a `Ready` task covering the loads its constructor starts, so a save made before
  the screen finished loading cannot write a default over a stored value.

## What this does not do

It adds no cache to `AppSettingsService` or `ProfileService`. They are stateless and correct; a
cache would add a second source of truth to the one place that currently has none.

## Note on testing

Every test in this project runs headless, with no WPF `Application` and no bound control. A cold
code review found two defects that live specifically in what WPF writes *back* into a view model
when a bound list is cleared — a nulled `SelectedSection` that crashed navigation, and a nulled
`SelectedProfile` that rebuilt the open group. **View-model tests cannot see these.** Mutating a
collection that is bound to a `Selector.SelectedItem` deserves an add/remove rather than a
clear-and-refill, and a manual pass on the real window.
