# SPEC-2026-001 — Fit fills the page, and every screen scrolls on small displays

| | |
|---|---|
| **Status** | Approved |
| **Revision** | B |
| **Tier** | Feature |
| **Author** | Claude, for Franz Gerster |
| **Date** | 2026-09-13 |
| **Project** | FgmakerScanner |
| **Supersedes** | — |

Part of the 2026-09-13 "Fixes and Changes" request, split into three specs: this one (Fit and
scrolling), [SPEC-2026-002](./SPEC-2026-002-group-record-editor.md) (record editor and field
lengths) and [SPEC-2026-003](./SPEC-2026-003-scan-viewer-delete-and-quick-scan-tools.md) (Scan page
viewer and delete; tools into Quick Scan). SPEC-002 depends on this spec's Prompts 1 and 2.

---

## 01 · Management summary

**What we are building.** Two fixes. First, the **Fit** button (on the Groups page preview and in
the full-size page viewer) will make the page fill the available space. Today it shows a normal
scan at about a third of the size it should. Second, **every screen and dialog gets scroll bars**
when the window is too small to show everything, so nothing is cut off on a laptop screen.

**Why.** Both are defects, not wishes. Fit was measured on 2026-09-13: a 300-DPI page in a
700-unit-tall viewer renders 224 units tall. The main window opens at 1200×760, which is taller
than a 1366×768 laptop can show, and the only screens that scroll today are Settings and the Scan
page's left panel. On a smaller screen, buttons — including dialog OK/Cancel buttons — are simply
unreachable.

**What it costs.** About a day: 6 prompts across 2 phases. No new dependencies, no database change,
no recurring cost.

**What could go wrong.** Wrapping screens in scroll areas the obvious way (one big scroll area
around everything) breaks the Groups data grid's performance on large groups and the image zoom.
This spec avoids that and names the tests that prove it.

**What we need from you.** Decided 2026-09-13: Fit shows the whole page. One thing is still to
provide: the scanning station's screen resolution and scale, which set the minimum sizes. Two
minutes, steps in §11.2. Prompts 1–2 do not need it; Prompt 3 waits for it.

## 02 · Outcomes

- **Goal** — an operator on the scanning station's laptop can reach every control and read a
  page at a glance, without maximising windows or guessing what is off-screen.
- **Who benefits** — Jim (the evidence station operator) and anyone using FG Scanner on a laptop.
- **How we will know it worked**
  - Fit on a portrait 300-DPI page fills the viewer's height to within 1 layout unit (unit test,
    AC-1).
  - At the smallest supported screen (§05 Q2), every button in every section and dialog can be
    reached by scrolling (manual matrix, AC-9 and AC-10).

## 03 · Scope and non-goals

**In scope**
- Fit math corrected in the Groups preview and in `PageViewerWindow`.
- The preview re-fits when its panel or the window is resized, until the operator zooms by hand.
- A zoom percentage label next to the preview's −/+/Fit buttons (the viewer already has one).
- One reusable scroll host that gives each section scroll bars only below a minimum size, and
  fills the window as today above it.
- The main window opens no larger than the screen's work area and has a minimum size.
- Groups, Scan, Search, Trash and Settings use the scroll host.
- The Groups top area (batch values, index values, both toolbars) gets a height cap and its own
  vertical scroll, so the grid and preview keep room.
- Every dialog: body scrolls, buttons stay visible, window never taller than the screen.
- `PageViewerWindow` opens clamped to the screen.
- Saved splitter sizes are clamped to the space available when restored.

**Non-goals**
- No redesign of the Groups toolbars (no overflow menu, ribbon or icon-only buttons).
- No remembering of the main window's size or position (see §05 N4).
- No FlaUI or other automated UI-test harness.
- No change to image decode sizes or memory use.
- No touch or pinch zoom.
- No change to `app.manifest` DPI awareness.
- The record editor (SPEC-002) and Quick Scan are not built here; they reuse the scroll host.

## 04 · Current state

- **Stack** — .NET 10 WPF, `ThemeMode="System"` Fluent (`App.xaml:4`), CommunityToolkit MVVM,
  per-monitor-v2 DPI (`app.manifest:20-21`). Tests: xunit.v3 in MTP mode; **516 passed** in Debug
  at `605ce9d` on 2026-09-13. The Release test run could not build that day because FG Scanner was
  running from `src\FgScanner.App\bin\Release` and locked its DLLs.
- **Fit — the defect, verified.**
  - `ZoomController.Fit` (`src/FgScanner.App/Views/ZoomController.cs:36-46`) computes
    `min(viewportW/contentW, viewportH/contentH)`, capped at 1.0.
  - Both callers pass **pixel** dimensions: `GroupsView.xaml.cs:245-247` and
    `PageViewerWindow.xaml.cs:92` pass `PixelWidth/PixelHeight`.
  - Both images use `Stretch="None"` (`GroupsView.xaml:325`, `PageViewerWindow.xaml:38`), so WPF
    lays them out at their **DPI-scaled size**: `pixels × 96 / DPI`.
  - Scans are saved with their true DPI stamped (`Naps2ScanService.cs:75-86`,
    `ScanResolutionPolicy.cs`). A 2550×3300 px scan at 300 DPI lays out at 816×1056 units.
  - Measured with a synthetic 300-DPI JPEG: in a 950×700 viewport, Fit picks scale 0.212, which
    renders the page **224 units tall of 700**. Same result for the preview's 1200-px decode
    (`PreviewImageConverter.cs:20,34` keeps DPI 300, so layout is 384×497 and scale 0.451 → 224).
  - The cap `Math.Min(scale, 1.0)` is in the same wrong unit.
  - The preview has no deferred fit for a not-yet-laid-out viewport; the viewer does
    (`PageViewerWindow.xaml.cs:84-89,103-109`). The preview has no zoom % label.
  - Tests: `tests/FgScanner.App.Tests/PageViewerTests.cs:11-119` (`ZoomControllerTests`) — the
    tests feed pixel numbers and one asserts "never enlarges" (`:80-88`).
- **Scrolling — what exists.**
  - Shell: `ShellWindow.xaml:10-11` `Width="1200" Height="760"`, no min size; sections swap into a
    plain `ContentControl` (`:29-30`, `ShellWindow.xaml.cs:187-189`).
  - GroupsView: root `Grid` 270 | `*` (`GroupsView.xaml:9-13`); the detail's top `StackPanel`
    (`:84-261`) holds two expanders and two `WrapPanel` toolbars of ~30 buttons; below it grid
    `*` MinWidth 240 | 6 | preview 300 MinWidth 200 (`:263-268`). Only the preview image scrolls
    (`:318-321`); the DataGrid virtualizes rows (`:279`). Splitter sizes restored from
    `Session.PreviewPanelWidth/Height`, clamped 200–1600 / 90–2000 (`GroupsView.xaml.cs:15-16,38-60`).
  - ScanView: left panel already scrolls vertically (`ScanView.xaml:16`); thumbnails are a
    virtualized ListBox (`:96-105`).
  - SettingsView: whole view in a vertical `ScrollViewer` (`SettingsView.xaml:9`), horizontal
    disabled; long unwrapped CheckBox labels are cut off (`:117,121,125`).
  - SearchView and TrashView: DataGrid only; low risk.
  - Dialogs: Batch, Adjust, ExportImages, ExportPdf, Input, GroupPicker, Reprocess, DeleteGroup,
    FirstRun are fixed width, `SizeToContent="Height"`, `ResizeMode="NoResize"`, no ScrollViewer.
    DuplicateReview is 760×460 resizable. PageViewerWindow is 1000×820 (`PageViewerWindow.xaml:4`),
    taller than a 768-high screen, with Close docked at the bottom (`:23-27`).
  - No global ScrollViewer style (`App.xaml:6-7` empty). No UI tests anywhere.
- **Patterns to match** — comments explain why; view logic that can go wrong numerically lives in
  WPF-free classes with tests (`ZoomController`, `PageNavigator`); inline English strings (ADR-0001).
- **Not examined** — dialog code-behind files; `ShellWindow.xaml.cs` beyond lines 55-200;
  `App.xaml.cs` beyond theme/window-state lines; whether any dialog is shown before the shell
  exists (FirstRunDialog) and so has no owner work area.

## 05 · Questions for Franz

**Blocking**

1. **What should "Fit" do?**
   - (a) **Show the whole page** — fits height on a tall page and width on a wide one. *(recommended)*
   - (b) **Always fit the height** — a landscape page or a narrow panel gets a horizontal scroll bar.
   - (c) **Two buttons** — "Fit page" and "Fit width".
   *Why it matters:* after the fix, (a) already fills the height for portrait pages in both the
   preview (wide, short panel) and the viewer, so it matches "fit vertically" in every normal
   layout. (b) differs only when the preview is dragged tall and narrow, where it overflows
   sideways. (c) adds a button and a test; it suits reading small print across a page.
   **Answer (Franz, 2026-09-13, Round A): (a) whole page.**
2. **What is the smallest screen we must fully support?**
   - (a) **1366×768 at 125% scaling** — about 1093×614 usable. *(recommended)*
   - (b) 1366×768 at 100%.
   - (c) 1280×720 at 100%.
   - (d) Find out the station's actual screen first.
   *Why it matters:* it sets every section's minimum content size and the manual test matrix.
   Choosing too large leaves the station cut off; too small forces cramped minimums that scroll
   on every normal laptop.
   **Answer (Franz, 2026-09-13, Round A): (d) check the station's screen first.** The section
   minimums (§08 Part 2) and the manual matrix (AC-9, AC-10) use the station's actual resolution
   and scale, read with the steps in §11.2. Prompts 1 and 2 go ahead; **Prompt 3 does not start
   until that reading is recorded here:**
   - Station display: **1280 × 1024** (lowest resolution FG Scanner must support) — recorded by
     Franz on 2026-09-13. **Scale not stated**, so the minimums are chosen to fit at both 100% and
     125%. 150% (≈853 × 683 layout units) would need a second pass on the Groups page.

**Non-blocking** — all six confirmed by Franz, 2026-09-13, Round A.

1. **Fit may enlarge** a page past 100% when the panel is bigger than the page, but only up to
   the point where one image pixel covers one screen pixel — never blurrier than the scan.
   *Proceeding as if:* yes. Today's "never enlarge" rule is in the wrong unit and is replaced.
2. **The preview re-fits on resize** until the operator zooms by hand; turning to another page
   re-fits, as today.
3. **"100%" means actual paper size** on screen. Today it means one image pixel per layout unit,
   which is a third of paper size for a 300-DPI scan.
4. **The window's size is not remembered** — it opens clamped to the screen each launch.
5. **The Groups top area is capped at 45%** of the section's height and scrolls on its own.
6. **Design system:** follow the app's own WPF Fluent theme. The FG Maker "Organic" kit is a web
   design system and does not apply to this WPF app.

## 06 · Assumptions

| # | Assumption | If wrong |
|---|---|---|
| A1 | Stored page images carry their scan DPI (stamped by `Naps2ScanService`) | Fit is still right: an image with no DPI lays out at 96 DPI, where layout size equals pixels |
| A2 | The station's display, once read (§05 Q2), is the smallest screen FG Scanner is used on | Another machine with a smaller screen needs its minimums lowered and a second manual pass |
| A3 | No automated UI tests exist, so layout is verified by hand | If FlaUI is added later, the manual matrix becomes its first test list |
| A4 | Imported files at odd DPI (e.g. 72) look larger at 100% | Cosmetic; Fit still correct |

## 07 · Data model

_Not applicable — no schema change, no new settings keys._ The existing
`Session.PreviewPanelWidth/Height` keys stay; only their restore clamp changes.

## 08 · Architecture and approach

### Part 1 — Fit

- `ZoomController.Fit(contentWidth, contentHeight, viewportWidth, viewportHeight, maxScale)`.
  Callers pass the image's **layout size** (`BitmapSource.Width/Height`, which are DPI-aware) and
  `maxScale = PixelWidth / Width` (image pixels per layout unit). Scale is
  `clamp(min(vw/cw, vh/ch), Minimum, min(maxScale, Maximum))`. The zero-viewport guard stays.
- `Reset()` keeps `Scale = 1.0`, which now reads as actual paper size because the layout size is
  DPI-aware. Update the XML comment ("1 image pixel to 1 device pixel" is wrong today).
- Preview: add a `FitPolicy` (WPF-free, beside `ZoomController`) holding `UserZoomed`. Fit clears
  it, In/Out set it, a page change clears it. `GroupsView` re-fits on `PreviewScroller.SizeChanged`
  when `!UserZoomed`, and defers the first fit until the viewport has a size (the viewer's pattern).
  The viewer uses the same policy.
- Add a zoom % `TextBlock` to the preview button row.

**Alternatives rejected.** `Stretch="Uniform"` in fit mode: fits without math, but the zoom and pan
model splits in two. Re-decoding images at 96 DPI: also fixes the math, but costs a bitmap copy per
page and makes "100%" mean pixels again.

### Part 2 — Scrolling

- **`SectionScrollHost`** (`src/FgScanner.App/Views/Controls/SectionScrollHost.cs`): a
  `ContentControl` whose template is a `ScrollViewer` (both bars Auto). The content's `Width` and
  `Height` are bound to the ScrollViewer's `ViewportWidth/ViewportHeight`, and its
  `MinWidth/MinHeight` come from `MinContentWidth/MinContentHeight` properties. Above the minimum,
  content is exactly viewport-sized, so star rows, the grid's virtualization and fill layouts behave
  as today. Below it, the minimum wins and the bars appear. No converter needed.
- **Minimums** — confirmed in Prompt 3 against the recorded 1280 × 1024 screen (§05 Q2).
  - **Section area available at full window size:**
    - 100% scale: 1280 × 976 work area − window frame (~16 × 39) − nav 160 − host margins 16
      ≈ **1088 × 921**
    - 125% scale: 1024 × 781 − frame − nav − margins ≈ **832 × 726**
  - **Per section:**
    - Groups **760 × 520** (270 + 12 + 240 + 6 + 200 = 728, plus margin)
    - Scan **600 × 480**
    - Search and Trash **600 × 400**
    - Settings **700** wide, height unbounded (replaces its own ScrollViewer)
  - Every minimum fits inside the 125% figure, so at full window size on the station nothing
    scrolls at section level; scroll bars appear only when the window is made smaller. The Groups
    top area still caps at 45% (§05 N5).
  - The host is applied once, in `ShellWindow`, with a per-section minimum table, rather than in
    each view's XAML.
- **Shell.** `WindowSizing.FitToWorkArea(Size requested, Rect workArea)` — a WPF-free pure function
  returning a size no larger than the work area, centred. Applied in `ShellWindow` on
  `SourceInitialized`, which is when the window's monitor is known. `MinWidth="800" MinHeight="560"`.
- **Groups top area.** Wrap the docked-top `StackPanel` in a `ScrollViewer` (vertical Auto) with
  `MaxHeight` bound to 45% of the detail panel's `ActualHeight`.
- **Splitter restore.** Clamp the restored preview width to `available − 240 − 6` and the height
  to the preview area; pure clamp function, tested.
- **Dialogs.** Each fixed dialog moves its body into a `ScrollViewer` (vertical Auto), keeps its
  button row outside the scroll area, and sets `MaxHeight` to the work area's height.
  `PageViewerWindow` calls `WindowSizing.FitToWorkArea` on `SourceInitialized`.

**Alternatives rejected.** A single ScrollViewer around `SectionHost`: gives every view unlimited
height, so the Groups DataGrid and Scan thumbnails lose virtualization. It also nests inside the
Settings and Scan ScrollViewers so they never scroll. A `Viewbox` that shrinks everything: text
becomes unreadable, which is the opposite of the goal.

**Data access.** None. **System evolution.** The Quick Scan plan (§7.2) already calls for a project
skill `fgscanner-wpf-section`. It should record "sections sit in `SectionScrollHost`" and "fit uses
layout size, not pixels" once this lands. No new skill here.

## 08a · AI opportunity assessment

Not a fit. Both problems are deterministic layout and arithmetic, and there is nothing to classify,
extract or rank.

## 09 · Design system compliance

The app's own WPF Fluent theme governs (§05 N6). No new colours or controls beyond the default
Fluent `ScrollViewer`. The zoom % label copies the viewer's style (`PageViewerWindow.xaml:20`,
Opacity 0.7).

**Accessibility.**
- Tab order is unchanged; the hosts are not focusable.
- Keyboard focus moving to a scrolled-off control scrolls it into view (WPF `RequestBringIntoView`);
  verify by tabbing through Settings.
- Dialog buttons never scroll out of reach.

## 10 · Acceptance criteria

- **AC-1** — Fitting 816×1056 layout units into a 950×700 viewport gives scale 700/1056.
  *Proven by:* `PageViewerTests.cs` → "Fit uses the layout size so a tall page fills the height"
- **AC-2** — Fit never exceeds `maxScale`: a 200×200 image in a 1000×1000 viewport with maxScale 1
  stays at 1.0; with maxScale 3.125 it stops at 3.125 rather than 5.
  *Proven by:* `PageViewerTests.cs` → "Fit stops where image pixels would be magnified"
- **AC-3** — The old "never enlarges" test is replaced by AC-2, with a comment saying why.
  *Proven by:* the diff of `PageViewerTests.cs` in code review (Prompt 5)
- **AC-4** — An unusable viewport (0, negative, NaN) leaves the scale unchanged.
  *Proven by:* existing theory `Fitting_against_a_viewport_with_no_size_leaves_the_scale_alone`
- **AC-5** — `FitPolicy`: manual zoom suppresses re-fit on resize; Fit and a page change restore it.
  *Proven by:* `PageViewerTests.cs` → "FitPolicy re-fits on resize only until the user zooms"
- **AC-6** — `WindowSizing.FitToWorkArea` returns the request unchanged when it fits, and shrinks
  to the work area and centres it when it does not.
  *Proven by:* `tests/FgScanner.App.Tests/WindowSizingTests.cs`
- **AC-7** — A saved preview width larger than the available space restores clamped so the grid
  keeps its 240 minimum.
  *Proven by:* `WindowSizingTests.cs` → "restored splitter width leaves the grid its minimum"
- **AC-8** — On the Groups page and in the viewer, a 300-DPI page shows whole on first open and
  after pressing Fit, and the zoom % label shows the scale.
  *Proven by:* manual — Franz, `docs/manual-tests.md` § Fit and small screens
- **AC-9** — At the §05 Q2 screen, every button on Scan, Groups, Search, Trash and Settings is
  reachable by scrolling.
  *Proven by:* manual — Franz (and Jim on the station), same checklist
- **AC-10** — At that screen, every dialog's buttons are visible without scrolling, and its body
  scrolls if taller than the screen.
  *Proven by:* manual — same checklist, one row per dialog
- **AC-11** — A Groups grid of 1,000+ rows still scrolls smoothly (virtualization intact).
  *Proven by:* manual — any large group, or 1,000 copies of one image imported with
  "Import PDF/images…"
- **AC-12** — In the preview, Ctrl+wheel zooms; plain wheel scrolls the image when it overflows.
  *Proven by:* manual
- **AC-13** — Full suite green, count ≥ 516 plus the new tests.
  *Proven by:* `dotnet test -c Release`

## 11 · Test strategy

**11.1 — The failing tests to write first**

| Feature | Test file | The failing assertion |
|---|---|---|
| Fit in layout units | `tests/FgScanner.App.Tests/PageViewerTests.cs` | scale == 700/1056 for 816×1056 in 950×700 |
| Magnification cap | `PageViewerTests.cs` | 200×200 in 1000×1000 with maxScale 3.125 → 3.125 |
| Re-fit policy | `PageViewerTests.cs` | after `In()`, a resize does not re-fit |
| Window clamp | `tests/FgScanner.App.Tests/WindowSizingTests.cs` | 1200×760 in a 1093×614 work area → ≤1093×614, centred |
| Splitter clamp | `WindowSizingTests.cs` | saved 900 width in a 1000-wide area → ≤ 754 |

**11.2 — Test data.** Pure numbers; no fixtures. For the manual checks, any group scanned at 300 DPI.

**Reading the station's screen (needed before Prompt 3).** On the scanning computer:
1. Right-click an empty spot on the desktop.
2. Click **Display settings**.
3. Scroll down to **Scale & layout**.
4. Write down the number next to **Scale** (for example `125%`).
5. Write down the numbers next to **Display resolution** (for example `1366 × 768`).
6. If there are two screens, click the one FG Scanner is used on first (the numbered boxes at
   the top), then do steps 4–5.
7. Send both values to Franz, or write them into §05 Q2 of this spec.

**Simulating that screen on the dev machine.** Windows Settings → System → Display → set
Display resolution and Scale to the station's recorded values (§05 Q2), then reopen FG Scanner.
Put the dev machine's own values back afterwards.

**11.3 — Verification suite.**
- Close FG Scanner first: a copy running from `bin\Release` locks its DLLs and the Release build
  fails.
- `dotnet build -c Release` (warnings are errors), `dotnet test -c Release`,
  `dotnet format --verify-no-changes`.
- Manual: the `docs/manual-tests.md` § Fit and small screens block.

## 12 · Edge cases and failure modes

| Case | Expected behaviour | Where handled |
|---|---|---|
| Image without DPI metadata | Lays out at 96 DPI; Fit correct; maxScale 1 | `ZoomController.Fit` callers |
| Landscape page | Fit (a) fits width; zoom label shows scale | `ZoomController` |
| Tiny image (screenshot) | Fit stops at maxScale; no blur | AC-2 |
| Viewport not laid out on first show | Fit deferred to first `SizeChanged` | `FitPolicy` / view |
| Scroll bar visible when Fit runs | Fit uses the viewport without the bar. When the bar disappears, `SizeChanged` re-fits once, since no manual zoom | view |
| Window dragged to a monitor with other scaling | Layout units stable; re-fit on resize | `FitPolicy` |
| Image fails to load (locked, corrupt) | No fit; label blank; nothing thrown | existing converter returns null |
| Saved splitter size from a big monitor | Clamped on restore | AC-7 |
| Screen resolution changed while running | Next window or dialog open uses the new work area | `WindowSizing` |
| Dialog content taller than screen | Body scrolls, buttons fixed | AC-10 |
| FirstRunDialog shown before the shell exists | Uses primary work area | `WindowSizing` |

## 13 · Security and configuration

Not web-facing. No new input surface, no files, no dependencies, no environment variables.

## 14 · Observability

Nothing new is logged; this is visual behaviour. **A silent failure would look like** a page that
still does not fill the view. The new zoom % label makes that visible: "21%" on a panel that should
show "66%" is obvious. Small-screen breakage is caught by the manual matrix, which is also the only
coverage (A3).

## 15 · Performance and scale

One image at a time; a fit is one division. The real performance risk is losing DataGrid
virtualization, covered by the design (§08) and AC-11. Not a performance question otherwise.

## 16 · Regression risk

| # | What could break | Why | Evidence (file:line) | Mitigation |
|---|---|---|---|---|
| R1 | Groups grid becomes slow on big groups | A DataGrid given unlimited height stops virtualizing rows | `GroupsView.xaml:279` | Host binds content size to the viewport (finite); AC-11 |
| R2 | Plain mouse wheel over the preview scrolls the whole page when the image cannot scroll further | Nested ScrollViewers bubble the wheel | `GroupsView.xaml:318-321` | Accept as expected; checked in AC-12 |
| R3 | Existing test asserts Fit never goes above 1.0 | The rule changes on purpose (§05 N1) | `PageViewerTests.cs:80-88` | Replace the test with AC-2 and say why |
| R4 | Viewer's deferred first fit stops working | Fit's arguments change inside the deferral path | `PageViewerWindow.xaml.cs:77-94,103-109` | Keep the `_fitOnNextLayout` path; AC-8 manual |
| R5 | Preview restores a width that squeezes the grid off-screen | Restore clamps only to 200–1600 | `GroupsView.xaml.cs:38-60` | Clamp to available space; AC-7 |
| R6 | Settings text stops wrapping | Horizontal scrolling gives text unlimited width | `SettingsView.xaml:9,110,166,210,237` | Host gives finite width = max(viewport, minimum), so wrapping still works |
| R7 | A dialog grows past the screen or clips its buttons | `SizeToContent="Height"` with no `MaxHeight` | each dialog's line 4-5 | Buttons outside the scroll area; `MaxHeight` = work area; AC-10 |
| R8 | "100%" and Ctrl+0 show a different size than before | "100%" now means paper size | `ZoomController.cs:28-29`, `PageViewerWindow.xaml.cs:172-174` | Intended (§05 N3); tooltip "Actual size" is now true |
| R9 | Release test run fails while FG Scanner is open | The running app locks `bin\Release` DLLs (seen 2026-09-13) | build output MSB3027 | Checkpoints tell the reader to close the app first |
| R10 | Scan thumbnails lose virtualization | Same as R1 | `ScanView.xaml:96-105` | Same host design |
| R11 | Scroll bars flicker on and off when the window is almost exactly the minimum size | Content sized to the viewport: a bar appearing shrinks the viewport, which can remove the need for the bar | WPF layout behaviour; new `SectionScrollHost` | Size content from the host's own `ActualWidth/ActualHeight` (which includes the bar area), not `Viewport*`, so a bar never changes the input. Checked by dragging the window edge slowly across the minimum in the manual matrix |

Checked and found nothing: SearchView and TrashView hold only a DataGrid; no automated UI test
exists that could break; no other caller of `ZoomController` (`grep ZoomController` → GroupsView and
PageViewerWindow only).

## 17 · Migration and rollback

- **Forward** — ship in the next release; nothing to migrate.
- **Backward** — revert the commits; no data involved.
- **Point of no return** — none.
- **Backup** — not needed.

## 18 · Documentation updates

- **CLAUDE.md** — no change.
- **`docs/manual-tests.md`** — new block "Fit and small screens": the display-settings steps from
  §11.2, one row per section and per dialog, plus the Fit rows.
- **`docs/user-guide.md`** — one line: Fit shows the whole page; 100% is actual size.
- **`docs/specs/README.md`** — status update.
- **Memory** — record the trap: "WPF `Image Stretch=None` lays out in DPI-scaled units, so zoom
  math must use `BitmapSource.Width`, never `PixelWidth`." It was invisible because every test
  used pixel numbers.

## 19 · Phase plan

### Phase 1 — Fit is right
- **Objective** — the page fills the view on Fit, in the preview and the viewer.
- **Delivers** — AC-1 to AC-5, AC-8, AC-12.
- **Not in this phase** — any scroll bar or window sizing work.
- **Done when** — the new tests pass and Fit is confirmed by hand on a 300-DPI group.

### Phase 2 — Nothing is out of reach
- **Objective** — every section and dialog usable at the smallest supported screen.
- **Delivers** — AC-6, AC-7, AC-9, AC-10, AC-11, AC-13.
- **Not in this phase** — toolbar redesign, window size memory, SPEC-002's editor.
- **Gate** — Prompt 3 starts only once the station's display is recorded in §05 Q2.
- **Done when** — the manual matrix passes at the station's recorded screen and the suite is green.

## 20 · Prompt pack

See [SPEC-2026-001-fit-and-scrolling-PROMPTS.md](./SPEC-2026-001-fit-and-scrolling-PROMPTS.md) —
6 prompts, written once this spec is approved:
1. Fit
2. Scroll host and window sizing
3. Sections
4. Dialogs and viewer
5. Code review (fresh session)
6. Docs and manual tests

## 21 · Definition of done

- [ ] All acceptance criteria met — _automated AC-1..AC-7 and AC-13 met; AC-8..AC-12 are manual rows in `docs/manual-tests.md` § Fit and small screens, written 2026-09-13, not yet walked_
- [x] Failing tests written first, now passing — each prompt watched its tests fail first (Prompts 1, 2, 4; review fix `e964e66`; preview fix `f467ec2`)
- [x] Full suite green — `dotnet test -c Release`: 545 passed, 0 failed (2026-09-13, `f467ec2`); Release build 0 warnings; format clean
- [x] `/code-review max` run, findings resolved or accepted in writing — Prompt 5 by a fresh-context reviewer: 17 fix commits; declined items listed below §22
- [x] Security review — _not applicable, not web-facing_
- [x] Documentation updated per §18 — `docs/manual-tests.md` § Fit and small screens, `docs/user-guide.md` Editing pages; memory updated
- [ ] Installed on the station and the manual matrix walked there
- [x] Rollback — _waived: revert only, no data_

## 22 · Sign-off

| | |
|---|---|
| **Review round answered** | ☑ 2026-09-13 · Round A: https://claude.ai/code/artifact/21aadd69-fe36-48d5-b1fc-3d8b069f12a9 (db doc `review/SPEC-2026-001-rA`) |
| **Franz approved** | ☑ 2026-09-13 (Round A verdict: approve; station screen to be read before Prompt 3) |
| **Built** | ☐ date: |
| **Verified in production** | ☐ date: |

### Review findings accepted without change

Prompt 5 review, 2026-09-13: a manual review plus the `/code-review max` run. Every fix is its own commit after `f6b26a9`.

- §08 describes `SectionScrollHost` as a ContentControl bound to the ScrollViewer's `Viewport*`; the code subclasses `ScrollViewer` (through `ShortcutScrollViewer`) and sizes content from its own measure constraint (`ScrollFill`), as R11 and Prompt 2 require — same behaviour, §08's wording is out of date.
- R11: with Fluent's style now applied to the host, its bars overlay the content, so a bar never shrinks the viewport and cannot flicker. `ScrollFill` still reserves the system bar width (17.33) beside the 12-unit Fluent bar — a thin strip, visible only while a section scrolls.
- §12 row "scroll bar visible when Fit runs … `SizeChanged` re-fits" and R4 "keep `_fitOnNextLayout`": the re-fit and the deferred first fit run on the viewport change `ScrollChanged` reports (a ScrollViewer publishes its viewport after `SizeChanged`), with `FitPolicy` holding the pending fit. Overlay bars never change the viewport, so a bar appearing does not re-fit.
- §08 dialogs ("`MaxHeight` via `WindowSizing`", viewer "`FitToWorkArea` on `SourceInitialized`"): built as `DialogFit.KeepOnScreen` — a work-area cap (a size clamp for the resizable viewer), then `WindowSizing.KeepInside` once rendered and whenever a size-to-content dialog grows. Same outcome, and a dialog centred off-screen is pulled back on.
- `ShellWindow` on a work area under 800×560 layout units (e.g. 1366×768 at 150%): WPF enforces `MinWidth/MinHeight` over `FitToWorkArea`, so the window overhangs — below the §05 Q2 support floor.
- Shortcuts bound to bare PageUp/PageDown do nothing while focus is inside a scrolled area: ScrollViewer's `ComponentCommands.ScrollPageUp/Down` gestures take those keys during command routing, before `ShortcutScrollViewer` sees them. No default shortcut uses them; arrow and Home/End shortcuts pass through.
- **Decided by Franz, 2026-09-13 — fixed, not accepted:** the preview's zoom % was relative to `PreviewImageConverter`'s 1200-px decode (a 300-DPI letter page laid out at 384×497 there, 816×1056 in the viewer), and a scan narrower than 1200 px was upscaled past N1's limit. Franz allowed the converter change that Prompt 1 had fenced off: it now decodes at 1200 px only for wider files, never enlarges, and re-stamps the DPI by the decode ratio so the copy lays out at paper size. The decode width itself (§03 non-goal) is unchanged. Proven by `PreviewImageConverterTests`.
- `MaxScale` is image pixels per layout unit, as §08 defines it, not per device pixel: at 125–150% scaling Fit can enlarge a small image past one image pixel per screen pixel by that factor. 300-DPI scans never reach the cap.
- Keyboard focus rings on controls flush with a scroll area's edge lose that edge (Fluent draws the ring 3 units outside the control; the ScrollContentPresenter's adorner layer clips it). Standard WPF behaviour Settings already had on main; the ring stays visible on its other sides, and insetting every scroll area would move every section's layout.
- Not acted on (cleanup, no behaviour change): zoom wiring duplicated between preview and viewer, nine near-identical dialog shells, code-behind constants mirroring XAML values, `WindowBounds`/`ContentSize` next to `Rect`/`Size`. For Prompt 6: no ADR or `FEATURE-PARITY.md` entry yet, and `ShellWindow.xaml.cs` cites SPEC-2026-001, which is not committed.
