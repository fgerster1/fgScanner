# FG Scanner — Status & Remaining Work

**Reviewed:** 2026-08-27 · **Branch:** `main` @ `ce26c89` · **Reviewer:** Claude (repo audit)
**Supersedes:** the 2026-08-24 review at `9829c2c`. Twenty-nine commits and eight phases (11–18)
have landed since; several claims in that version are now factually wrong and are corrected below.

Purpose: a plain account of where the app actually stands, and a prioritized punch list of
everything that must be closed out before it is safe/sensible to start adding *new* features.
Every claim below was verified against the repo today — the evidence is quoted.

---

## 1. Verified state as of today

| Check | Command | Result |
|---|---|---|
| Release build | `dotnet build -c Release` | **Succeeded — 0 warnings, 0 errors** (warnings-as-errors is on in Release) |
| Test suite | `dotnet test -c Release` | **430 passed, 0 failed, 0 skipped** (MTP mode; was 253 on 08-24) |
| Format gate | `dotnet format --verify-no-changes` | **Clean (exit 0)** |
| CI on main | `gh run list` | **Green** on the last 3 pushes (latest: run 33106962528, 2026-08-27) |
| Working tree | `git status` | **Clean**, `main` in sync with `origin/main` |
| Declared version | `Directory.Build.props` | **`<Version>0.3.2</Version>`** |
| Local installers | `ls dist/` | 5 built: 0.1.0 → **0.3.2** (99.2 MB, 2026-08-27 10:20) |
| Parity checklist | `docs/FEATURE-PARITY.md` | 30 rows: **25 ☑ done, 5 ◐ partial**, 0 ☐ todo |

**Bottom line:** the codebase is healthier than it was three days ago — the test count has grown
70 %, the App layer is no longer untested, the localization question is settled, and `docs/adr/`
exists. Nothing is broken. What is *still* missing is everything downstream of "code complete."

**Two facts that anchor this whole document:**

1. `git tag -l` → **empty**. `gh release list` → **empty**. **Nothing has ever been released.**
   Eighteen phases are merged and five installers sit in `dist/`, all built by hand on this
   machine. The entire phase-9 shipping apparatus (`release.yml`: sign → portable ZIP → Inno
   installer → appcast → SHA256SUMS → provenance → draft release) has **never executed once**.
   Every day this stays true, more of that pipeline rots untested.
2. **FG Scanner now has a downstream consumer and an imminent hardware hand-off.** It is the
   capture station for the JimsStuff legal-evidence pipeline (`docs/spec-evidence-export.md`),
   whose importer parses committed group folders. A USB stick with the 0.3.2 installer, the
   portable build, and the operator walkthrough is staged for a dedicated TWAIN scanning machine.
   That reorders the list below: **R2 (hardware smoke) is now the highest-stakes item in the
   project**, and winget/SmartScreen polish matters less than it did when the audience was
   the public.

---

## 2. Release blockers — do these first (nothing has ever shipped)

### R1 — ✅ DONE 2026-08-24 — Inno Setup installed, installer builds clean

Kept for the record; the corrections it captured still hold. Inno Setup 7.1.0 installs **per-user**
to `%LOCALAPPDATA%\Programs\Inno Setup 7` when run unelevated, *not* Program Files — CLAUDE.md,
`docs/release.md`, and the `setup.iss` header all carry a discovery snippet that searches both
roots. `release.yml` was deliberately left alone: CI installs machine-wide via choco.

Also fixed then: `setup.iss` set no `VersionInfo*` directives, so the setup exe shipped a blank
FileVersion resource. `VersionInfoVersion={#AppVersion}` closed that.

**Superseded since:** phase 18 replaced the hand-typed `/DAppVersion=`. `setup.iss` now reads the
version off the published exe by default (`GetFileVersion`), whose number comes from `<Version>`
in `Directory.Build.props`. See **R3a** — CI has not caught up with this.

---

### R2 — Run the full manual hardware smoke test and record the result 🔴 **highest risk**

**Why:** `docs/manual-tests.md` is **6 boxes ticked out of 62**. The parity row *"WIA / TWAIN /
eSCL scanning"* is still `◐ (logic done; hardware smoke pending)`. All 430 automated tests run
against `FakeScanService` — by design, they prove **zero** about real drivers.

**Why it is more urgent than it was on 08-24:** the 0.3.2 build is about to be carried to a
scanning station with *different TWAIN hardware than this machine*, to capture *legal evidence*.
An unproven driver path there does not produce a bug report, it produces a gap in an evidence
record. The doc's own closing note is honest about the size of this: *"Thumbnail streaming, cancel
mid-run, the crash-recovery prompt, duplex, empty-feeder error surfacing, `--fake-scanner` startup,
and everything in the phase 4–10 sections"* still require a human at the GUI.

**Steps:**
1. Work top-to-bottom through `docs/manual-tests.md`, ticking boxes in the file as you go.
   Prioritize, in this order, because these have no automated backstop at all:
   - **Setup + Device discovery** (WIA, TWAIN 32-bit worker, eSCL over mDNS)
   - **Scanning** (flatbed 300 DPI, 3+ page feeder, duplex, cancel mid-run, empty feeder)
   - **Crash recovery** (kill mid-scan → relaunch → recovery prompt)
   - **TWAIN specifics** (32-bit-only vendor driver; unplug mid-scan)
2. Then the **evidence path specifically**, since that is what the station exists for: Evidence
   profile with all nine field names → scan → commit → confirm `index.json` carries `sequence`,
   `pageId`, `checksum`, `isBlank`, `originalChecksum`, that `manifest.json` has `evidenceExport`,
   and that `originals\` is populated with `Feature.PreserveOriginals` on.
3. Watch for the known issue already logged in that file: after force-killing `FgScanner.exe`,
   check Task Manager for lingering `NAPS2.Worker.exe`. If any linger, that is a real bug in the
   Job-object assignment race and must be filed.
4. For anything that fails, open a GitHub issue rather than fixing inline — you want the smoke
   pass to produce a written record.
5. Commit the ticked checklist.

**Done when:** Setup / Device discovery / Scanning / Crash recovery / TWAIN sections are fully
ticked on the *station's* hardware, the evidence round-trip in step 2 is confirmed, and
`docs/FEATURE-PARITY.md` row 1 is upgraded from `◐` to `☑`.

---

### R3 — Cut the first release and verify the pipeline

**Why:** unchanged from 08-24, and now three months of features deep. `release.yml` does eight
things in sequence and any one of them can fail on first contact.

**Steps:**
1. Confirm pre-conditions from `docs/release.md`: `dotnet test -c Release` green locally
   (✅ verified today, 430/430), CI green on main (✅), R1 (✅) and R2 done.
2. **Fix R3a below first**, then tag to match `<Version>`:
   ```powershell
   git tag v0.3.2
   git push origin v0.3.2
   ```
3. Watch it: `gh run watch` (or `gh run list --workflow=release.yml`).
4. Expected non-fatal skips on this first run: the two **SignPath** steps skip (R5) and **winget**
   does not fire until you publish (R6). The **appcast** step *will* run — `APPCAST_ENABLED=true`
   and `SPARKLE_ED25519_PRIVATE_KEY` are both set on the repo (re-verified today via
   `gh variable list` / `gh secret list`).
5. Review the **draft** release. Confirm the assets: setup exe, portable ZIP, `appcast.xml`,
   `SHA256SUMS`. Write the release notes. Then publish.

**Correction to the 08-24 version of this item:** it said *"`Directory.Build.props` contains no
`<Version>` property — the tag **is** the version; there is nothing else to bump."* **That is no
longer true.** Phase 18 (`530ffa5`) made `<Version>` in `Directory.Build.props` the single source,
and CLAUDE.md now states it as a hard rule. Bump it there *and* tag to match.

**Done when:** the releases page shows a published v0.3.2 with all four asset types attached.

---

### R3a — Reconcile the two version sources before tagging 🟠 *(new finding)*

**Evidence:** `.github/workflows/release.yml:21-24` still derives the version from the tag name
and passes `-p:Version=` to both publishes (lines 30, 37) and `/DAppVersion=` to ISCC (line 87).
That **overrides** `<Version>` in `Directory.Build.props`, which phase 18 established as the single
source of truth and which CLAUDE.md states as a hard rule. `setup.iss` documents the override as
deliberate, so the installer is fine either way — but nothing checks that the tag and the props
file agree.

**Why it matters:** tag `v0.4.0` against a repo declaring `0.3.2` ships artifacts stamped `0.4.0`
while the source says otherwise, silently. The evidence exports carry `appVersion` into
`manifest.json` and `index.xml`, so a mismatch ends up written into evidence records that a portal
parses — the one place a wrong build number is expensive rather than cosmetic.

**Steps (pick one, ~15 minutes either way):**
- **Preferred:** add a guard step to `release.yml` right after *"Version from tag"* that reads
  `<Version>` out of `Directory.Build.props` and fails the run if it differs from the trimmed tag.
  Keeps the tag as the artifact name and makes drift impossible.
- **Or:** drop the `-p:Version=` overrides entirely and let the props file flow through, using the
  tag only for asset filenames. Simpler, but then a forgotten props bump ships a duplicate number.

**Done when:** a tag that disagrees with `Directory.Build.props` cannot produce a release.

---

### R4 — Prove the auto-update loop end to end

**Why:** unchanged. The Ed25519 public key is live and real in the binary
(`src/FgScanner.App/Services/UpdateService.cs:22`), and `SecurityMode.Strict` means the app will
**silently refuse** any appcast it cannot verify. That failure mode is invisible — a signing
mismatch looks identical to "no update available." You must observe a successful update once.

**Steps:**
1. After the first release is published, install it from the setup exe (ideally a clean Win11 VM —
   this doubles as the phase-9 checklist item).
2. Tag and publish a throwaway patch (a docs-only commit is enough).
3. Launch the installed older build. Within a few seconds of startup it should offer the update.
4. Accept it, confirm the silent upgrade (`/VERYSILENT /NORESTART`) completes and the app relaunches
   at the new number.
5. Confirm the upgrade preserved state: groups, `fgscanner.db`, settings, and the stored Gemini key
   in Credential Manager all survive. There should be a `fgscanner.db.bak-<version>` file — the
   automatic pre-migration backup (observed working on 2026-08-27).
6. Tick the corresponding boxes in `docs/manual-tests.md` § Phase 9.

**Extra check now that evidence groups exist:** confirm an upgrade leaves committed group folders
and their `originals\` subfolders untouched.

**Done when:** an installed old build has visibly self-updated to a newer one.

---

### R5 — Apply to SignPath Foundation (unsigned = SmartScreen wall)

**Re-verified today:** `gh secret list` shows **only** `SPARKLE_ED25519_PRIVATE_KEY`;
`gh variable list` shows **only** `APPCAST_ENABLED`. `SIGNPATH_API_TOKEN`, `SIGNPATH_ORG_ID`, and
`SIGNPATH_ENABLED` are all still absent, so both signing steps skip and every build ships unsigned.

Longest-lead item in the list — human review, can take weeks. **Start it in parallel with
everything else.**

**Steps:**
1. Apply at <https://signpath.org/apply>. Free for OSS; the repo is public, MIT-licensed, ships an
   install-time privacy policy plus the Gemini AI opt-out, and `THIRD-PARTY-NOTICES.md` is complete.
2. When accepted, create project `fgScanner` with signing policy `release-signing` and **two**
   artifact configurations, named exactly as `release.yml` expects:
   - `publish-payload` — the exe/dll set, signed *before* the installer is packed
   - `installer` — the setup exe
3. Add secret `SIGNPATH_API_TOKEN`, variables `SIGNPATH_ORG_ID` and `SIGNPATH_ENABLED=true`.
4. Cut a patch release and confirm both signing steps run and the installer shows a verified
   publisher.

**Note on priority:** this is now *lower* urgency than it looked on 08-24. The immediate audience is
one known operator installing from a USB stick, not the public — a SmartScreen prompt they can be
told about in advance is an inconvenience, not a bounce. Still start the application, because the
lead time is the whole cost.

**Done when:** a downloaded setup exe shows "Verified publisher" and no SmartScreen block.

---

### R6 — First winget submission (manual, one time)

Unchanged and still last. `winget.yml` auto-submits *updates* but cannot create a package that does
not exist. `WINGET_TOKEN` is still absent.

**Steps:** create a PAT with `public_repo` scope as repo secret `WINGET_TOKEN`; after the first
release, run `wingetcreate new <installer-url>` once locally with identifier
**`FranzGerster.FGScanner`** (the exact string `winget.yml` expects); submit the manifest PR to
`microsoft/winget-pkgs`. Do R5 first — unsigned installers get more scrutiny.

**Done when:** `winget install FranzGerster.FGScanner` works from a clean machine.

---

## 3. Engineering debt — status since 08-24

### D1 — Localization — ✅ **RESOLVED 2026-08-25 (Option B)**

`ac24d05` *"Settle the localization question: English-only, and start docs/adr/"* took the explicit
drop. `find src -name "*.resx"` → still **0**, and that is now correct rather than a violation:
**ADR-0001** records the decision, and CLAUDE.md's hard rule was amended to *"UI is English-only;
user-visible strings are written inline, no .resx (docs/adr/0001)."* Nothing further to do.

---

### D2 — ADRs — ◐ **PARTIAL: the directory exists, the backfill does not** 🟡

**Evidence:** `docs/adr/` now holds a `README.md` plus **three** ADRs — 0001 (English-only UI),
0002 (auto-orient to any angle), 0003 (preserve originals). The process rule is being followed
*going forward*: each was written in the same phase as the code it justifies.

**What is still missing:** the backfill. PLAN §6 lists ten resolved decisions with no ADR behind
them. Of the seven the 08-24 audit named, only the localization one got written. Still unrecorded:

- **SDK-over-fork** — NAPS2.Sdk (LGPL) + our own MIT app rather than forking the GPL NAPS2 app.
  *This is the load-bearing licensing decision of the whole project* (PLAN §3) and it is findable
  only in plan prose and in CLAUDE.md's hard-rules list.
- **.NET 10 / WPF over Python** (PLAN §3).
- **One page = one document in v1** (PLAN §5.1, decision #2).
- **Gemini-only for v1** behind an `IChatClient` seam; BYO-key, Credential Manager storage.
- **Tesseract via shell-out** rather than in-process bindings, and why OCR tests never mock it.
- **SQLite/EF Core as a first-class deliverable** behind the index files, with stable `v_index` /
  `v_pages` / `v_ocr_text` read views.
- **Ed25519-signed appcast in `SecurityMode.Strict`** — never accept an unsigned update.

Add an eighth now that it exists: **the evidence-export contract** — why `index.json` row keys,
`manifest.json`'s `evidenceExport`, and the nine Evidence field names are frozen, and why FG Scanner
deliberately has no Bates support. That reasoning currently lives only in CLAUDE.md and
`docs/spec-evidence-export.md`; it is exactly the kind of thing a future contributor would
"helpfully" rename.

**Done when:** those eight files exist in `docs/adr/`.

---

### D3 — WPF test coverage — ◐ **MUCH IMPROVED; FlaUI and coverage still missing** 🟡

**Evidence, re-measured today:** `tests/FgScanner.App.Tests/` now holds **11 test files** (was 2),
**7 of which exercise view-models** — `ShellTests`, `ScanReturnTests`, `SchemaNoticeTests`,
`TrashMultiSelectTests`, `PendingFieldValueTests`, `BlankRowFieldValueTests`,
`RowContentVisibilityTests` — against 59 source files / 7,044 lines of App code. The phases-11–18
work carried its own view-model tests, which is exactly what the 08-24 item asked for. Consider
**step 1 of that item effectively done.**

**Still open, both from the original step list:**
1. **FlaUI is declared but unused.** CLAUDE.md lists it in the stack;
   `grep FlaUI Directory.Packages.props tests/*/*.csproj` returns **nothing**. Either add
   `FlaUI.Core` + `FlaUI.UIA3`, create `tests/FgScanner.UiTests`, and write **three** smoke tests
   (launch with `--fake-scanner`, scan → commit → assert `index.csv`; toggle a feature flag in
   Settings; open a group and rotate a page), kept out of the default `dotnet test` run so the
   inner loop stays fast — **or** strike FlaUI from CLAUDE.md's stack list. A declared-but-absent
   dependency is the same class of lie D1 was.
2. **No coverage visibility.** `.github/workflows/ci.yml` runs build / test / format / CodeQL and
   collects no coverage. Add `--coverage` to the test step and publish the report as an artifact —
   no hard gate needed, you just want to *see* the App number move.

---

### D4 — Repo hygiene files — 🔴 **UNCHANGED, all four still missing**

**Re-verified today:** no `CHANGELOG.md`, no `SECURITY.md`, no `CONTRIBUTING.md`. `.github/`
contains **only** `workflows/` — no `dependabot.yml`, no issue templates.

For a public repo about to publish a downloadable installer and an auto-updater, two of these are
not optional:

1. **`SECURITY.md`** — required, because you ship a signed auto-updater and handle a user's Gemini
   API key in Credential Manager. State a private reporting channel (enable GitHub Private
   Vulnerability Reporting) and a supported-version policy.
2. **`.github/dependabot.yml`** — large third-party surface (NAPS2.Sdk, PDFsharp, ClosedXML,
   Google.GenAI, SQLitePCLRaw, ZXing) plus Actions pins. Weekly `nuget` + `github-actions` PRs.
   **Licensing guard:** review every bump against CLAUDE.md's forbidden list. A CI step that greps
   the restored package graph for `FluentAssertions` ≥8, `iText*`, `EPPlus` ≥5, `Emgu.CV`,
   `System.Data.SQLite`, and `NAPS2.Images.ImageSharp` and fails the build would make that
   automatic — **verified today that no such check exists in any workflow.** With Dependabot on,
   it stops being optional.
3. **`CHANGELOG.md`** — Keep-a-Changelog. The auto-updater shows release notes to users, so you
   want one canonical source. It would now need to cover eighteen phases; seeding it is easiest
   immediately before R3.
4. **`CONTRIBUTING.md`** and issue templates — lower priority, do them if you take outside
   contributions.

**Done when:** `SECURITY.md`, `dependabot.yml` (+ the forbidden-package CI grep), and
`CHANGELOG.md` exist on main.

---

### D5 — Feature-flag policy — ◐ **PARTIAL; the flag set has doubled** 🟡

**Evidence:** `src/FgScanner.Data/FeatureFlags.cs` now carries **six** flags, not four:

| Flag | Default | Terminal state documented? |
|---|---|---|
| `Feature.Search` | on | ☐ no |
| `Feature.PatchT` | off | ☐ no |
| `Feature.BlankPolicy` | off | ☐ no |
| `Feature.CommitHook` | off | ☐ no |
| `Feature.AutoOrient` | **on** | ☑ ADR-0002 |
| `Feature.PreserveOriginals` | off | ☑ ADR-0003 (+ CLAUDE.md: stays **on** for evidence groups) |

The two new flags arrived with ADRs, so the *practice* has improved. The four phase-10 flags still
have no documented terminal state, and three of the four differentiators remain invisible to a new
user out of the box.

**Steps:** decide per flag — permanently opt-in, or default-on once the `docs/manual-tests.md`
§ Phase 10 checks pass on real hardware (R2). *Suggested:* `BlankPolicy` → default on after
hardware validation (pure win on a duplex feeder — and note it is now load-bearing for evidence
exports, since phase 16 made blank-flagged rows appear in `index.json`); `PatchT` and `CommitHook`
→ stay opt-in (they need user setup to mean anything). Record it as an ADR and reflect it in
`docs/user-guide.md`. If a flag graduates, delete it from `FeatureFlags` rather than flipping the
fallback — dead flags are worse than no flags.

**Done when:** each of the six flags has a documented terminal state.

---

## 4. Parity gaps still marked `◐` — scope them or close them

Five rows are partial. Two (scanning hardware, installer/signing) are R2/R5. `Import PDF/images;
file associations` is covered by the installer work. The other two are genuine unbuilt scope, and
**neither has moved since 08-24.**

### P1 — Profile settings surface is thin (unchanged)

**Re-verified:** `src/FgScanner.Scanning/ScanModels.cs:39-49` — `ScanProfileOptions` still has
**six** knobs: `Source`, `Dpi`, `BitDepth`, `PageSize`, `Brightness`, `Contrast`. `ScanPageSize` is
still a fixed enum with **no custom size**.

Against NAPS2's profile surface the notable absences: custom page size (W×H + units),
auto-deskew-at-scan-time, horizontal alignment for feeder scans, flip-duplexed-back-pages, JPEG
quality for the captured image, "use native TWAIN UI", and per-driver WIA offsets.

**Steps:** extract the full NAPS2 8.3.2 profile field list from `docs/research/research-1-naps2.md`
into a keep/drop/defer table in `docs/FEATURE-PARITY.md` **before writing code** — the value is in
deciding what you *don't* want. Then build only the keeps; realistically **custom page size**,
**auto-deskew on scan**, **flip duplexed back pages**, **use native TWAIN UI**. Each needs the
property, the `Naps2ScanService` mapping, the profile-editor UI, a `.fgprofile` round-trip test
(**bump the schema version and add a migration test for the old one**), and a `FakeScanService`
assertion.

**Note for the evidence path:** "use native TWAIN UI" is the one most likely to matter on Jim's
machine, where a vendor driver may expose settings the six knobs cannot reach. Let R2 tell you
whether it is needed before you build it.

### P2 — MAPI email is not implemented (unchanged)

**Re-verified:** `grep -rilE "mapi32|MAPISendMail|SendMail"` across `src/**/*.cs|*.xaml` (excluding
`bin`/`obj`) → **zero hits**. The parity row still reads *"MAPI email in 9"*; phase 9 shipped
without it and eight further phases have gone by.

**Pick one:** build it (~half a day: P/Invoke `MAPISendMail`, attach from the existing export
pipeline, guard the no-MAPI-client case with a `mailto:` fallback) — **or drop it**, mark the row
`[D]` with a note that Print + drag-out cover the workflow, and write the ADR. MAPI is a dying API
and this app's actual user hands over a USB stick; **dropping it is the defensible choice.** Make
it explicitly.

### P3 — Two phase-10 items were never built, and are *still* invisible (unchanged) 🟠

**Re-verified:** `grep -rilE "FileSystemWatcher|WatchFolder"` and
`grep -rilE "BatchLevelField|GroupField"` across source → **zero hits each**. PLAN §9 phase 10
promised six deliverables; four shipped.

The 08-24 audit called step 1 *"a 5-minute edit and the honest thing to do"* — **it was not done.**
`docs/FEATURE-PARITY.md` still has zero `☐` rows, which reads as "phase 10 was 100 % complete."
Add both as explicit `☐` rows targeted at a future version. Then treat them as feature work (§6).

### P4 — Local clutter (unchanged, 2 minutes)

`ss1.html` is **still** an untracked stray in the repo root. Delete it if it is scratch, move it
under `docs/` if it is a real asset. `dist/` and `publish/` remain correctly gitignored.

---

## 5. Suggested order of execution

The dependency chain matters more than the priority labels. The evidence hand-off dominates it now.

**Start today, in parallel (blocked on other people, not on you):**
- **R5** SignPath application — weeks of human review, and the only thing here with a lead time

**Next, because a scanning station is waiting on it:**
1. **R2** hardware smoke pass on the station's TWAIN hardware, including the evidence round-trip →
   half a day, and by far the highest-risk item in the project
2. **P3 step 1** + **P4** — add the two `☐` rows, delete `ss1.html` → 15 minutes, do them while
   waiting on hardware

**Then get to a real release:**
3. **R3a** version-source guard → 15 minutes, and it must land before any tag
4. **D4** `CHANGELOG.md` seed (R3 wants it anyway), `SECURITY.md`, `dependabot.yml` + the
   forbidden-package grep → half a day
5. **R3** tag v0.3.2 and publish → 1 hour
6. **R4** prove auto-update with a throwaway patch → 1 hour

**Then pay down what compounds:**
7. **D2** backfill the eight ADRs → half a day
8. **D3** FlaUI decision (build the three smokes or strike it from CLAUDE.md) + CI coverage
   artifact → half a day
9. **D5** terminal state for all six flags → 1 hour, once R2 has validated `BlankPolicy`

**Then and only then, new features:**
10. **R6** winget, once R5 has landed
11. **P1 / P2** decisions, then §6

---

## 6. Where new features should come from

Do **not** invent a new backlog. `docs/PLAN.md` §7 holds a research-grounded, effort-sized v1.1–v2
list of **29 items** with prior-art attribution. Phase 10 consumed four of them (#1 Patch-T, #19
webhook, #22 FTS search, plus the blank-page policy from the adopted set); phases 11–18 were driven
by the v0.2 gap analysis and the evidence spec rather than by §7, so the list is largely intact.

Highest-value unbuilt entries, by effort:

| # | Feature | Effort | Note |
|---|---|---|---|
| 7 | Batch-level fields stamped on every row (box number, operator) | **S** | Promised in phase 10 — see P3. **Now doubly relevant:** the Evidence profile already asks for `Box` and `Operator` on every page, by hand |
| 4 | Rescan-in-place ("replace this page") | **S** | Obvious gap in the editing surface; on an evidence station a bad feed currently means rebuilding the group |
| 10 | Operator identity per row (Windows username) | **S** | The `$(user)` token already backs the Evidence `Operator` default — this generalizes it |
| 9 | Tags column (multi-valued, `;`-separated) | **S** | Slots into the existing schema editor |
| 2 | Barcode value → index field | **M** | ZXing is already a dependency from Patch-T |
| 20 | Watch folders (drop file → pipeline runs) | **M** | Promised in phase 10 — see P3 |
| 6 | Lookup auto-fill from a CSV/database table | **M** | Strong differentiator vs. Epson DCP |
| 16 | Gemini Batch API mode (50 % cost on backfills) | **M** | The AI queue is already durable |
| 25 | Audit log of field changes (who/when/from/to) | **M** | Was low priority on 08-24; an evidence capture station is exactly where it earns its keep |

**Suggested next phase:** items **#7 + #10** together as "batch and row metadata." Both are **S**,
they share the schema-editor and index-exporter paths, one is an unfulfilled phase-10 promise, and
they directly remove hand-typing from the evidence workflow that is about to go live — the
operator currently types `Box` and `Operator` on every single page. Add **#4 rescan-in-place**
if the R2 pass shows misfeeds are common on the station's feeder.

**Anything touching the index exporters must respect the frozen contract** in CLAUDE.md: the
`index.json` row keys, `manifest.json`'s `evidenceExport`, and the nine Evidence field names
(`DocNo`, `DocDate`, `DocType`, `Title`, `Parties`, `Operator`, `Redact`, `Box`, `Notes`) are
external API for the JimsStuff importer. Adding columns is safe; renaming any of those breaks a
legal pipeline silently.

When you start: new branch `phase-N-<name>` per the CLAUDE.md process rule, CI green before merge,
and update **both** `FEATURE-PARITY.md` and `docs/adr/` on the way out.
