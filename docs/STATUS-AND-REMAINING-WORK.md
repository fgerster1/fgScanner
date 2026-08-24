# FG Scanner — Status & Remaining Work

**Reviewed:** 2026-08-24 · **Branch:** `main` @ `9829c2c` · **Reviewer:** Claude (repo audit)

Purpose: a plain account of where the app actually stands, and a prioritized punch list of
everything that must be closed out before it is safe/sensible to start adding *new* features.
Every claim below was verified against the repo today — the evidence is quoted.

---

## 1. Verified state as of today

| Check | Command | Result |
|---|---|---|
| Release build | `dotnet build -c Release` | **Succeeded — 0 warnings, 0 errors** (warnings-as-errors is on in Release) |
| Test suite | `dotnet test -c Release` | **253 passed, 0 failed** (MTP mode) |
| Format gate | `dotnet format --verify-no-changes` | **Clean (exit 0)** |
| CI on main | `gh run list` | **Green** on the last 3 pushes (latest: run 32414182652, 2026-08-20) |
| Working tree | `git status` | **Clean** |
| Code TODOs | `grep TODO/FIXME/NotImplemented src/**/*.cs` | **None** (3 hits are legitimate `NotSupportedException` in one-way value converters) |
| Parity checklist | `docs/FEATURE-PARITY.md` | 20 rows: **16 ☑ done, 4 ◐ partial**, 0 ☐ todo |

**Bottom line:** the codebase is healthy. All ten planned phases are implemented, merged, and
CI-green. Nothing is broken. What is *missing* is everything downstream of "code complete":
the product has never actually been released, never been proven on real scanner hardware in a
recorded pass, and carries a small pile of process/quality debt that will get more expensive
the more feature code you pile on top of it.

**Two facts that anchor this whole document:**

1. `git tag -l` → **empty**. `gh release list` → **empty**. **v0.1.0 was never cut.** The entire
   phase-9 shipping apparatus (installer, auto-update, appcast, provenance, winget) has never
   executed once end-to-end.
2. `find src -name "*.resx"` → **empty**. The CLAUDE.md hard rule *"User-visible strings go in
   .resx from phase 3 onward"* has been violated since phase 3, and there are now ~9,650 lines
   of App code with hardcoded strings. This is the single largest and fastest-growing piece of
   debt in the project.

---

## 2. Release blockers — do these first (nothing has ever shipped)

### R1 — ✅ DONE 2026-08-24 — Inno Setup installed, installer builds clean

**Why it mattered:** `release.yml` builds the installer in CI via `choco install innosetup`, but
`setup.iss` had never been compiled even once. A script error would have surfaced *after* pushing a
tag — a failed release run and a burned tag.

**What was done:**
1. `winget install JRSoftware.InnoSetup.7` → 7.1.0. Note it installs **per-user** to
   `%LOCALAPPDATA%\Programs\Inno Setup 7` when run unelevated, *not* Program Files.
2. `dotnet publish src/FgScanner.App -p:PublishProfile=win-x64` → 353 files / 318.8 MB, with the
   10 NAPS2 DLLs as separate files (LGPL separation intact — non-single-file, non-trimmed).
3. ISCC compile → **`dist\fgscanner-0.1.0-win-x64.exe`, 91.6 MB, exit 0, zero errors.**

**Corrections to the original audit** (both were wrong, recorded here so the doc stays honest):
- **Inno Setup 6 was already installed** per-user at `%LOCALAPPDATA%\Programs\Inno Setup 6`. The
  original claim that ISCC was "absent" only checked the two Program Files roots.
- The docs' ISCC invocation assumed a Program Files path. Fixed in CLAUDE.md, `docs/release.md`, and
  the `setup.iss` header — all three now use a discovery snippet that searches both roots plus
  `%LOCALAPPDATA%\Programs` and prefers the highest version. `release.yml` was left alone: CI
  installs machine-wide via choco, so its narrower search is correct there.

**Fixed along the way:** `setup.iss` set no `VersionInfo*` directives, so the setup exe shipped a
**blank FileVersion** resource (`ProductVersion` was `0.1.0`, `FileVersion` was empty). Added
`VersionInfoVersion={#AppVersion}`. Blank version info hurts support triage and scores badly against
AV/SmartScreen heuristics on an already-unsigned binary.

**Still open from this item:** the built installer has only been *compiled*, not *installed*. Running
it on a clean Win11 VM is part of R2/R4's checklist (`docs/manual-tests.md` § Phase 9).

**Observation for later:** the payload carries the full cross-platform Tesseract/pdfium set
(`_linux`, `_linuxarm`, `_mac`, `_macarm`, `_win32`, `_win64`) inside a Windows-only installer.
Harmless, but it is a meaningful slice of the 91.6 MB if you ever want to slim the download.

---

### R2 — Run the full manual hardware smoke test and record the result

**Why:** `docs/manual-tests.md` is a 9-section checklist with **every box unchecked**. The parity
row *"WIA / TWAIN / eSCL scanning"* is explicitly `◐ (logic done; hardware smoke pending)`.
Automated tests all run against `FakeScanService` — by design, they prove *zero* about real
drivers. Shipping without this pass is the biggest single risk in the project.

**Steps:**
1. Work top-to-bottom through `docs/manual-tests.md`, ticking boxes in the file as you go.
   Prioritize, in this order, because these are the ones with no automated backstop at all:
   - **Setup + Device discovery** (WIA, TWAIN 32-bit worker, eSCL over mDNS)
   - **Scanning** (flatbed 300 DPI, 3+ page feeder, duplex, cancel mid-run, empty feeder)
   - **Crash recovery** (kill mid-scan → relaunch → recovery prompt)
   - **TWAIN specifics** (32-bit-only vendor driver; unplug mid-scan)
2. Watch specifically for the known issue already logged in that file: after force-killing
   `FgScanner.exe`, check Task Manager for lingering `NAPS2.Worker.exe` processes. If any linger,
   that is a real bug in the Job-object assignment race and must be filed before release.
3. For anything that fails, open a GitHub issue rather than fixing inline — you want the smoke
   pass to produce a written record.
4. Commit the ticked checklist: `git commit -am "Hardware smoke pass for v0.1.0"`.

**Done when:** Setup / Device discovery / Scanning / Crash recovery / TWAIN sections are fully
ticked, and `docs/FEATURE-PARITY.md` row 1 is upgraded from `◐` to `☑`.

---

### R3 — Cut the v0.1.0 release and verify the pipeline

**Why:** this is the first exercise of `release.yml`. It does eight things in sequence (publish
app → publish CLI → sign → portable ZIP → Inno installer → appcast → SHA256SUMS → provenance
attestation → draft release). Any one of them can fail on first contact.

**Steps:**
1. Confirm the pre-conditions from `docs/release.md`: `dotnet test -c Release` green locally
   (✅ verified today, 253/253), CI green on main (✅), R1 and R2 done.
2. Decide the version. Note that `Directory.Build.props` contains **no `<Version>` property** —
   the version is injected entirely by the *"Version from tag"* step in `release.yml`. So the tag
   **is** the version; there is nothing else to bump.
3. Tag and push:
   ```powershell
   git tag v0.1.0
   git push origin v0.1.0
   ```
4. Watch it: `gh run watch` (or `gh run list --workflow=release.yml`).
5. Expected non-fatal skips on this first run: the two **SignPath** steps skip (see R5) and
   **winget** does not fire until you publish (see R6). The **appcast** step *will* run —
   `APPCAST_ENABLED=true` and `SPARKLE_ED25519_PRIVATE_KEY` are both already set on the repo
   (verified via `gh variable list` / `gh secret list`).
6. Review the **draft** release. Confirm the assets: setup exe, portable ZIP, `appcast.xml`,
   `SHA256SUMS`. Write the release notes. Then publish.

**Done when:** <https://github.com/fgerster1/fgScanner/releases> shows a published v0.1.0 with all
four asset types attached.

---

### R4 — Prove the auto-update loop end to end

**Why:** the Ed25519 public key is live and real in the binary
(`src/FgScanner.App/Services/UpdateService.cs:22` = `AYGJKjx0kHdK1dPayOwD71kSEa4yS7j0iVMofJ9RVm4=`,
no longer the `REPLACE...` placeholder), and `SecurityMode.Strict` means the app will **silently
refuse** any appcast it cannot verify. That failure mode is invisible — a signing mismatch looks
identical to "no update available". You must observe a successful update once.

**Steps:**
1. After v0.1.0 is published, install it from the setup exe on a machine (ideally a clean Win11 VM —
   this doubles as the phase-9 checklist item).
2. Tag and publish a throwaway `v0.1.1` (a docs-only commit is enough).
3. Launch the installed **v0.1.0** build. Within a few seconds of startup it should offer the update.
4. Accept it, and confirm the silent upgrade (`/VERYSILENT /NORESTART`) completes and the app
   relaunches at 0.1.1.
5. Confirm the upgrade preserved state: groups, `fgscanner.db`, settings, and the stored Gemini key
   in Credential Manager all survive. (There should also be a `fgscanner.db.bak-<version>` file —
   the automatic pre-migration backup.)
6. Tick the corresponding boxes in `docs/manual-tests.md` § Phase 9.

**Done when:** an installed old build has visibly self-updated to a newer one.

---

### R5 — Apply to SignPath Foundation (unsigned = SmartScreen wall)

**Why:** `gh secret list` shows only `SPARKLE_ED25519_PRIVATE_KEY`. **`SIGNPATH_API_TOKEN`,
`SIGNPATH_ORG_ID`, and `SIGNPATH_ENABLED` are all absent**, so both signing steps in `release.yml`
are skipped and every release ships unsigned. Users will hit a SmartScreen "Windows protected your
PC" dialog and most will bounce.

This is the longest-lead item in the whole list — the application is reviewed by humans and can
take weeks. **Start it today, in parallel with everything else.**

**Steps:**
1. Apply at <https://signpath.org/apply>. Free for OSS. Your repo already satisfies the
   eligibility bar: it is **public**, MIT-licensed, ships an install-time privacy policy plus the
   Gemini AI opt-out, and `THIRD-PARTY-NOTICES.md` is complete.
2. When accepted, create project `fgScanner` with signing policy `release-signing` and **two**
   artifact configurations, named exactly as `release.yml` expects:
   - `publish-payload` — the exe/dll set, signed *before* the installer is packed
   - `installer` — the setup exe
3. Add to GitHub → Settings → Secrets and variables → Actions:
   - secret `SIGNPATH_API_TOKEN`
   - variable `SIGNPATH_ORG_ID`
   - variable `SIGNPATH_ENABLED` = `true`
4. Cut a new patch release and confirm both signing steps now run and the installer shows a
   verified publisher in its properties.

**Done when:** a downloaded setup exe shows "Verified publisher" and no SmartScreen block.

---

### R6 — First winget submission (manual, one time)

**Why:** `winget.yml` auto-submits *updates*, but it cannot create a package that does not exist yet.

**Steps:**
1. Create a GitHub PAT with `public_repo` scope; add it as the repo secret **`WINGET_TOKEN`**
   (currently absent).
2. After v0.1.0 is published, run once locally:
   ```powershell
   wingetcreate new https://github.com/fgerster1/fgScanner/releases/download/v0.1.0/<setup-exe-name>
   ```
   Use identifier **`FranzGerster.FGScanner`** (this exact string is what `winget.yml` expects).
3. Submit the generated manifest PR to `microsoft/winget-pkgs` and wait for the merge.
4. From then on, every published release auto-submits its version bump.

**Note:** winget submissions of unsigned installers get more scrutiny. Consider doing R5 before R6.

**Done when:** `winget install FranzGerster.FGScanner` works from a clean machine.

---

## 3. Engineering debt — close these before writing new feature code

These are the items that make *future* work more expensive. R1–R6 are one-time chores; the items
below compound.

### D1 — Localization: no `.resx` exists anywhere (CLAUDE.md hard-rule violation) 🔴

**Evidence:** `find src -name "*.resx"` returns nothing. `src/FgScanner.App` is 175 files /
9,652 lines with every user-visible string hardcoded in XAML and view-models. PLAN §9 phase 9 also
specified a first-run wizard step for **language** — `FirstRunDialog.xaml` has no language step,
because there is nothing to switch between.

**Why it matters now:** every new feature you add multiplies the eventual extraction cost. This is
the clearest case in the repo of "fix it before you grow it".

**Decision to make first:** are you actually shipping non-English UI? Two honest options:

- **Option A — do the extraction (recommended if localization is ever real).** Effort: 1–2 focused days.
- **Option B — formally drop the requirement.** Effort: 10 minutes. Edit the CLAUDE.md hard rule to
  say English-only, write an ADR recording the decision and why (see D2), and remove the language
  step from the PLAN. *This is a legitimate choice — but make it explicitly, don't let it stay a
  silently-violated rule.*

**If Option A, the mechanics:**
1. Add `src/FgScanner.App/Resources/Strings.resx` (+ `Strings.Designer.cs`, public access modifier)
   and matching files in `FgScanner.Core` / `FgScanner.Cli` for their own user-facing text.
2. Extract in vertical slices, one view at a time, so each commit is reviewable — Settings first
   (largest string surface), then Groups, Scan, Search, and the dialogs.
3. In XAML, bind via a static resource wrapper:
   `Content="{x:Static res:Strings.Settings_AiSectionHeader}"`.
4. Naming convention: `<Area>_<Element>_<Purpose>` — keeps the resx browsable at 400+ entries.
5. Add a guard test so this cannot regress: walk every `.xaml` under `src/FgScanner.App` and fail
   on any literal `Content="…"` / `Header="…"` / `Text="…"` containing a space and a letter, with an
   explicit allowlist for symbols and glyphs.
6. Add `Strings.de.resx` (or whichever language) only *after* the neutral file is complete.

**Done when:** the guard test passes and CLAUDE.md's rule is either satisfied or formally amended.

---

### D2 — `docs/adr/` does not exist 🔴

**Evidence:** `ls docs/adr` → *No such file or directory*. But CLAUDE.md's process rule says
*"update FEATURE-PARITY.md and docs/adr/ when a decision lands"*, and PLAN §6 lists **10 resolved
decisions** with no ADR behind any of them. All the reasoning currently lives in PLAN prose and in
commit messages — which is fine for you today and useless in six months.

**Steps:**
1. `mkdir docs/adr`, add `docs/adr/README.md` explaining the format (MADR is fine: Context /
   Decision / Status / Consequences).
2. Backfill the decisions that a future reader would otherwise re-litigate. At minimum these seven:
   - **ADR-0001** — SDK-over-fork: use NAPS2.Sdk (LGPL) and write our own MIT app rather than fork
     the GPL NAPS2 application. *This is the load-bearing licensing decision of the whole project*
     (PLAN §3) and it must be findable in one file, not buried in a plan.
   - **ADR-0002** — .NET 10 / WPF over Python (PLAN §3).
   - **ADR-0003** — one page = one document in v1 (PLAN §5.1, decision #2).
   - **ADR-0004** — Gemini-only for v1 behind an `IChatClient` seam; BYO-key, Credential Manager storage.
   - **ADR-0005** — Tesseract via shell-out rather than in-process bindings, and why OCR tests never
     mock the engine.
   - **ADR-0006** — SQLite/EF Core as a first-class deliverable behind the index files, with stable
     `v_index` / `v_pages` / `v_ocr_text` read views.
   - **ADR-0007** — Ed25519-signed appcast in `SecurityMode.Strict`; never accept an unsigned update.
3. Going forward: one ADR per decision, in the same commit as the code that implements it.

**Done when:** `docs/adr/` holds those seven files and CLAUDE.md's process rule is true again.

---

### D3 — The WPF layer is effectively untested; FlaUI is declared but unused 🟠

**Evidence:** `tests/FgScanner.App.Tests/` contains exactly **two** files (`ShellTests.cs`,
`RetroPdfIntegrationTests.cs`) against **9,652 lines** of App code. CLAUDE.md lists **FlaUI** in the
stack, but `grep FlaUI Directory.Packages.props tests/*/*.csproj` returns **nothing** — it was never
added. Meanwhile `FgScanner.Data.Tests` has 14 test files and `FgScanner.Scanning.Tests` has 8, so
the discipline is real everywhere *except* the layer where new features will actually land.

**Why it matters now:** every new feature you described wanting to add is a UI feature. Without
view-model tests, each one is a manual-regression tax forever.

**Steps (do the cheap half first):**
1. **View-model unit tests (high value, no new dependency).** The view-models are already
   MVVM/CommunityToolkit and constructor-injected, so they are testable as-is. Cover, in priority
   order: `SettingsViewModel` (largest, 300+ lines, holds the AI/feature-flag/shortcut logic),
   `GroupDetailViewModel` and its `.Editing` partial (undo/redo stack correctness — this is
   genuinely tricky logic with zero coverage), and the Search view-model.
2. **Then FlaUI, if you want the declared stack to be true.** Add `FlaUI.Core` + `FlaUI.UIA3` to
   `Directory.Packages.props`, create `tests/FgScanner.UiTests`, and write **three** smoke tests
   only — launch with `--fake-scanner`, scan → commit → assert `index.csv` on disk; open Settings
   and toggle a feature flag; open a group and rotate a page. Keep them out of the default
   `dotnet test` run (separate CI job) so they never make the inner loop slow or flaky.
3. **Add coverage visibility to CI.** `ci.yml` runs build/test/format/CodeQL but collects no
   coverage. Add `--coverage` to the test step and publish the report as an artifact — you don't
   need a hard gate, you need to be able to *see* the App number stop being ~0%.

**Done when:** `FgScanner.App.Tests` covers the three view-models above, and CI publishes a coverage
artifact.

---

### D4 — Repo hygiene files are missing 🟡

**Evidence:** no `CHANGELOG.md`, no `CONTRIBUTING.md`, no `SECURITY.md`, and `.github/` contains
*only* `workflows/` — no `dependabot.yml`, no issue templates.

For a **public** repo (confirmed `PUBLIC` via `gh repo view`) that is about to have a downloadable
installer and an auto-updater, two of these are not optional:

**Steps:**
1. **`SECURITY.md`** — required, because you ship a signed auto-updater and handle a user's Gemini
   API key in Credential Manager. State a private reporting channel (enable GitHub Private
   Vulnerability Reporting in repo settings) and your supported-version policy.
2. **`.github/dependabot.yml`** — you have a large third-party surface (NAPS2.Sdk, PDFsharp,
   ClosedXML, Google.GenAI, SQLitePCLRaw, ZXing) plus GitHub Actions pins. Weekly `nuget` +
   `github-actions` update PRs. Note the licensing guard: **review every bump against the CLAUDE.md
   forbidden list** — a Dependabot PR must never pull in `FluentAssertions ≥8`, `iText*`,
   `EPPlus ≥5`, `Emgu.CV`, `System.Data.SQLite`, or `NAPS2.Images.ImageSharp`. Consider adding a CI
   step that greps the restored package graph for those names and fails the build.
3. **`CHANGELOG.md`** — Keep-a-Changelog format; the auto-updater shows release notes to users, so
   you want one canonical source. Seed it with a `0.1.0` entry summarizing phases 0–10.
4. **`CONTRIBUTING.md`** and issue templates — lower priority, do them if/when you take outside
   contributions.

**Done when:** `SECURITY.md`, `dependabot.yml`, and `CHANGELOG.md` exist on main.

---

### D5 — Decide and document the feature-flag policy 🟡

**Evidence:** `src/FgScanner.Data/FeatureFlags.cs` ships all four phase-10 differentiators behind
flags. `Search` defaults **on**; `PatchT`, `BlankPolicy`, and `CommitHook` default **off**.

That was correct for phase 10, but it leaves three of your four differentiators invisible to a new
user out of the box — including the two that are the strongest selling points against NAPS2.

**Steps:**
1. Decide per flag: stays opt-in permanently, or flips on by default in a future version once the
   manual checks in `docs/manual-tests.md` § Phase 10 pass on real hardware.
   - *Suggested:* `BlankPolicy` → default on after hardware validation (it is pure win on a duplex
     feeder); `PatchT` and `CommitHook` → stay opt-in (they need user setup to be meaningful).
2. Record the decision as an ADR (D2) and reflect it in `docs/user-guide.md`.
3. If any flag graduates to default-on, remove it from `FeatureFlags` entirely rather than flipping
   the fallback string — dead flags are worse than no flags.

**Done when:** each of the four flags has a documented terminal state.

---

## 4. Parity gaps still marked `◐` — scope them or close them

Four rows in `docs/FEATURE-PARITY.md` are partial. Two of them (scanning hardware, installer/signing)
are covered by R2/R5 above. The other two are genuine unbuilt scope:

### P1 — Profile settings surface is thin

**Evidence:** `src/FgScanner.Scanning/ScanModels.cs` — `ScanProfileOptions` has **six** knobs:
`Source`, `Dpi`, `BitDepth`, `PageSize`, `Brightness`, `Contrast`. `ScanPageSize` is a fixed enum of
seven sizes with **no custom size**.

Against NAPS2's profile surface the notable absences are: custom page size (W×H + units),
auto-deskew-at-scan-time, horizontal alignment for feeder scans, flip-duplexed-back-pages, JPEG
quality / max-quality for the captured image, "use native TWAIN UI", and per-driver WIA offsets.

**Steps:**
1. Open `docs/research/research-1-naps2.md` and extract the full NAPS2 8.3.2 profile field list into
   a table in `docs/FEATURE-PARITY.md` — one row per field, marked keep / drop / defer. **Do this
   before writing code**; the value here is deciding what you *don't* want.
2. Implement only the keeps. Realistically the high-value four are: **custom page size**,
   **auto-deskew on scan**, **flip duplexed back pages**, **use native TWAIN UI**.
3. Each new field needs: the `ScanProfileOptions` property, the NAPS2.Sdk mapping in
   `Naps2ScanService`, the profile editor UI, a `.fgprofile` round-trip test (the import/export
   format is schema-versioned — **bump the schema version and add a migration test for the old
   version**), and a `FakeScanService` assertion.
4. Update the parity row to `☑` with the deliberate-drop list noted inline.

---

### P2 — MAPI email is not implemented

**Evidence:** the parity row reads `◐ (print + clipboard + drag-out done; MAPI email in 9)` but
phase 9 shipped without it — `grep -ri "mapi\|MailTo\|SendMail" src` returns **zero** hits in any
production path.

**Steps (pick one):**
- **Build it (~half a day).** P/Invoke `MAPISendMail` from `mapi32.dll`, attach the exported
  PDF/images from the existing export pipeline, and add an "Email…" command next to Print in
  `GroupDetailViewModel.Editing`. Guard it: MAPI fails silently when no MAPI client is registered,
  so detect that and fall back to a `mailto:` shell-execute with the file paths in the body, or grey
  the command out.
- **Or drop it.** Mark the row `[D]` (deliberately different) with a note that Print + drag-out cover
  the workflow, and write it up in an ADR. Given that MAPI is a dying API and most users are on
  webmail, **this is the defensible choice** — but make it explicitly.

---

### P3 — Two phase-10 items in the PLAN were never built

**Evidence:** PLAN §9 phase 10 lists **six** deliverables: *"Patch-T/barcode separation + printable
sheets, blank-page policies, FTS search UI, webhook-on-commit, batch-level fields, watch folder."*
Four shipped. `grep -ri "watchfolder\|FileSystemWatcher" src` → **nothing**;
`grep -ri "BatchLevelField\|GroupField" src` → **nothing**.

Neither is a bug — they simply weren't built, and `FEATURE-PARITY.md` doesn't mention them, so
they are currently invisible work. Both are also on the §7 backlog (items **#7 batch-level fields, S**
and **#20 watch folders, M**).

**Steps:**
1. **Right now:** add both to `docs/FEATURE-PARITY.md` as explicit `☐` rows targeted at v1.2, so the
   checklist stops implying phase 10 was 100% complete. This is a 5-minute edit and it is the
   honest thing to do.
2. Then treat them as feature work, not cleanup — see §6.

---

### P4 — Local clutter (trivial, 2 minutes)

`git status --ignored` shows `dist/`, `publish/`, and `ss1.html` as ignored-but-present. All three
are correctly in `.gitignore`, so nothing leaks. But `ss1.html` is an untracked stray in the repo
root — delete it if it's a leftover scratch file, or move it under `docs/` if it's a real asset.

---

## 5. Suggested order of execution

The dependency chain matters more than the priority labels:

**Start today, in parallel (both are blocked on other people, not on you):**
- **R5** SignPath application — longest lead time in the list, weeks of human review
- **D1 decision** — Option A or Option B on localization; the *decision* is 10 minutes and unblocks
  every subsequent UI commit

**Week 1 — get to a real release:**
1. ~~R1 (Inno Setup local build)~~ → ✅ **done 2026-08-24**
2. R2 (hardware smoke pass) → half a day, and the highest-risk item in the project
3. R3 (tag v0.1.0, publish) → 1 hour
4. R4 (prove auto-update with a throwaway v0.1.1) → 1 hour
5. P4 (delete `ss1.html`), P3 step 1 (add the two missing rows to the parity checklist) → 15 minutes

**Week 2 — pay down what compounds:**
6. D2 (backfill 7 ADRs) → half a day
7. D4 (SECURITY.md, dependabot.yml + forbidden-package CI grep, CHANGELOG.md) → half a day
8. D1 Option A if chosen (resx extraction + guard test) → 1–2 days
9. D3 step 1 (view-model tests for Settings / GroupDetail-Editing / Search) → 1 day

**Week 3 — then and only then, new features:**
10. D3 step 2–3 (FlaUI smoke tests + CI coverage artifact)
11. D5 (feature-flag terminal states)
12. R6 (winget), once R5 has landed
13. P1 / P2 decisions, then §6

---

## 6. Where new features should come from

Do **not** invent a new backlog. `docs/PLAN.md` §7 already contains a research-grounded, effort-sized
v1.1–v2 list of **29 items** with prior-art attribution for each, and phase 10 consumed only four of
them. The highest-value unbuilt entries, by effort:

| # | Feature | Effort | Note |
|---|---|---|---|
| 7 | Batch-level fields stamped on every row (box number, operator) | **S** | Already promised in phase 10 — see P3 |
| 4 | Rescan-in-place ("replace this page") | **S** | Small, and an obvious gap in the editing surface |
| 9 | Tags column (multi-valued, `;`-separated) | **S** | Slots straight into the existing schema editor |
| 10 | Operator identity per row (Windows username) | **S** | The `$(user)` naming token already exists |
| 2 | Barcode value → index field | **M** | ZXing is already a dependency from Patch-T |
| 20 | Watch folders (drop file → pipeline runs) | **M** | Already promised in phase 10 — see P3 |
| 6 | Lookup auto-fill from a CSV/database table | **M** | Strong differentiator vs. Epson DCP |
| 16 | Gemini Batch API mode (50% cost on backfills) | **M** | The AI queue is already durable |

**Suggested first feature after cleanup:** items **#7 + #10 + #9** together as a single "batch and
row metadata" phase. All three are **S**, they share the schema-editor and index-exporter code paths,
one of them is already an unfulfilled phase-10 promise, and they will immediately exercise whatever
you decided in D1 (resx) and D3 (view-model tests) — which is exactly the validation you want on
fresh process changes before attempting something large.

When you start it: new branch `phase-11-<name>` per the CLAUDE.md process rule, CI green before
merge, and update **both** `FEATURE-PARITY.md` and `docs/adr/` on the way out.
