# Spec — Scan section becomes scan-to-folder

**Status:** approved design, not implemented · **Written:** 2026-08-24
**Sequencing:** deferred until v0.1.0 ships — do not start before the release is out.

Filed in `docs/` alongside `scope-auto-orientation.md` rather than under
`docs/superpowers/specs/`, to match this repo's existing convention.

---

## 1. Why

The Scan section today is not a scanning tool — it is the front half of the group workflow. Scanned
pages land in a recovery session under `%APPDATA%` and the **only** route to a durable location is
`SaveToGroupCommand`, gated on `CanSaveToGroup()` requiring an active group. A user who just wants
to scan something must first understand groups.

Two smaller frictions compound it: the driver and device selection are not persisted anywhere (no
settings keys exist for either), so every launch starts from the default driver with nothing chosen.

This spec makes Scan a self-contained scan-to-folder tool. Indexing, OCR and AI stay in Groups.

## 2. Decisions taken

| # | Decision | Rationale |
|---|---|---|
| 1 | Scan writes to a folder; Groups adopts it afterwards via "Process existing folder…" | Reuses the tested phase-7 adoption path (keeps filenames, checksum dedup, idempotent) instead of building a new bridge. |
| 2 | Flat destination folder, dated per-run **filename** prefix | Browsing stays simple. The batch-adoption concern is handled by intent: one-off scans go to the default folder, and a batch meant for indexing is scanned into its own directory via the one-shot picker. |
| 3 | "Scan to…" is a **one-shot** override, reverting to the default next run | The default stays the resting state, so scans cannot silently drift to an unexpected location. |
| 4 | Restore the last device via a **background probe** at startup | The window opens instantly with the remembered selection shown; TWAIN enumeration (several seconds, spawns 32-bit workers) never blocks launch. |
| 5 | **Batch scan stays** on the page | It is pure scanning with no indexing — it belongs in a scan-only section. Removing its only entry point would have made a shipped, tested phase-8 feature unreachable. |

## 3. Design

### 3.1 Destination

- New setting **`Scan.DefaultFolder`**, default `%USERPROFILE%\Documents\FG Scanner Scans`, created
  on first use. Editable in Settings with a `Microsoft.Win32.OpenFolderDialog` browse button — the
  same picker already used in `GroupsViewModel` and the export flow, so no new UI dependency.
- The Scan page **shows the live destination path**. Not knowing where a scan went is the problem
  being solved; a dialog you passed through three minutes ago does not solve it.
- **"Scan to…"** opens `OpenFolderDialog` (which permits creating a folder) and applies to the next
  run only. After that run the destination reverts to `Scan.DefaultFolder`.
- **"Run" means one press of Scan, or one entire batch** — all passes of a batch share the override
  and the same run prefix, and it reverts when the batch ends, not between passes. A run cancelled
  before any page is captured leaves the override armed; a run that produced pages consumes it.

### 3.2 Naming

Reuse `FgScanner.Core.Naming.NamingEngine` rather than introducing a second naming scheme — it
already provides `$(YYYY) $(MM) $(DD) $(hh) $(mm) $(n..nnnn)`, slugification and
`ExpandUnique(pattern, context, exists)` for collision suffixing.

- Run prefix is stamped **once at run start** so every page in a run shares it.
- Pattern: `$(YYYY)-$(MM)-$(DD)-$(hh)$(mm)_$(nnnnn)` → `2026-08-24-1842_00001.jpg`.
- Collisions resolved through `ExpandUnique` against the destination folder.
- The pattern is a constant for now — **not** user-configurable. Revisit only if asked.

### 3.3 Write path — the one architectural choice

Pages continue to stream into the recovery session exactly as today, and **each completed page is
moved to the destination as it arrives**. `IScanService`, `IPageStorage` and the recovery machinery
are untouched.

Rejected: writing directly to the destination from the scan loop. It would mean reworking
`IPageStorage` and the recovery session for no user-visible gain, and would put a hardware-facing
interface change in the blast radius of a UI feature.

Crash behaviour gets *simpler*: a page is in its final home the moment it is scanned, so the Scan
section no longer has an "unsaved pages" state to recover. The recovery prompt becomes relevant only
to a run interrupted mid-page.

### 3.4 Device memory

- New settings: **`Scan.LastDriver`** (first-run default **`Twain`**), **`Scan.LastDeviceId`**,
  **`Scan.LastDeviceName`**, via the existing `AppSettingsService.GetAsync/SetAsync`.
- On startup: render the remembered driver and device name immediately, touching no hardware.
- Then probe that driver in the background. On completion, match the saved device by **id**, falling
  back to **name** (ids are not stable across all drivers). Select it if found.
- If it is not found, populate the dropdown with what was found and set a status line saying the
  last scanner is unavailable. Never fail silently.
- The Scan command is disabled until the probe resolves or the user picks a device.
- Both values are written after every successful scan.

### 3.5 Removals

- `SaveToGroupCommand`, its button, `SaveTargetText`, and `CanSaveToGroup()`.
- `ScanViewModel`'s `ActiveGroupStore` dependency, if nothing else in the class needs it —
  verify at implementation time.
- **`ScanViewModel.cs:233` auto-saves a completed batch into the active group.** Since batch scan
  stays, that call must be replaced by the same write-to-destination path as a normal run, or a
  batch will silently do nothing at the end. Easy to miss.
- Unchanged: batch scan, the separator-sheet button (still behind `FeatureFlags.PatchT`), Cancel.

## 4. Testing

Everything here tests without hardware, per the CLAUDE.md rule:

| Area | Test |
|---|---|
| Destination resolution | default vs one-shot override; override reverts after one run |
| Run naming | one prefix per run; sequence increments; collision suffixing against an existing file |
| Missing default folder | created on first use |
| Device restore | saved id matches; falls back to name; missing device surfaces a status message and does not throw |
| Batch completion | pages land in the destination, not silently dropped |

`FakeScanService` plus a fake settings store covers all of it.

## 5. Knock-on work

- `docs/user-guide.md`, `README.md` and `docs/FEATURE-PARITY.md` all describe Scan as the route into
  a group. All three need updating in the same change.
- Adds user-visible strings, so it collides with the **D1 localization decision**
  (`docs/STATUS-AND-REMAINING-WORK.md`), which is still unmade. If `.resx` is happening, these
  strings should be born there rather than added to the hardcoded pile.
- `docs/manual-tests.md` needs new rows: scan to default folder, scan to a chosen folder, restart and
  confirm the scanner is remembered.

## 6. Risks

- **Removing the Scan→Group bridge is a workflow change for anyone used to it.** Mitigated by the
  destination being visible on the page and Groups' "Process existing folder…" being a documented,
  idempotent path.
- **`ActiveGroupStore` removal** may have callers beyond the obvious. Check before deleting.
- **Background probe timing.** Pressing Scan during the probe window needs defined behaviour;
  disabling the button until it resolves is the simplest correct answer.
