# SPEC-2026-006 — Duplex capture: one-pass hardware duplex and two-pass manual duplex

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

- **What we are building** — two ways to capture both sides of a stack of paper. First,
  making the existing hardware-duplex option honest: labelled so an operator knows what it
  is, offered only on scanners that can actually do it, and correcting upside-down backs.
  Second, a guided two-pass flow for the far more common scanner that cannot: scan all the
  fronts, flip the stack, scan all the backs, and the app puts them in the right order.
- **Why** — reported after a working session. Most of a legal box is double-sided; today
  the operator either loses the backs or fixes the order by hand on the Groups page after
  the fact, which is error-prone on a stack of sixty sheets.
- **What it costs** — 8 prompts across 3 phases, two to four days. No new dependency; the
  scanning SDK already exposes everything needed.
- **What could go wrong** — silent page loss. Saving a group drops any page whose
  checksum already exists, and a stack of blank backs produces identical images. Without
  a deliberate answer, a two-pass run would lose backs and mis-pair every sheet after the
  first loss. §05 Q2 is that decision, and it is the most important line in this spec.
- **What we need from you** — four decisions in §05: blank backs, the order the backs come
  off a flipped stack, what happens when the two passes disagree on page count, and
  whether an unsupported scanner hides the Duplex option or explains it.

## 02 · Outcomes

- **Goal** — a double-sided stack is captured in page order in one guided operation,
  whatever the scanner can do, without the operator repairing the order afterwards.
- **Who benefits** — Jim at the station, on every double-sided document in the box; and
  the case itself, because a mis-ordered exhibit is a real evidentiary problem that is
  invisible until someone reads it.
- **How we will know it worked** — a 10-sheet double-sided stack captured on a
  feeder-only scanner lands as 20 pages in the order 1F,1B,2F,2B… with no manual
  reordering, and the duplex rows in `docs/manual-tests.md` — unticked since the project
  began — are finally ticked.

## 03 · Scope and non-goals

**In scope**

- Hardware duplex: a labelled option, capability detection, a clear message when the
  scanner cannot do it, and `FlipDuplexedPages` for backs that arrive upside down.
- A two-pass manual duplex flow on the Scan page: start, scan fronts, prompt to flip,
  scan backs, interleave, one cancel that leaves nothing half-done.
- Ordering guaranteed before the pages reach a group, so the `scan_NNNNN` numbering is
  the final order.
- Handling for odd stacks (a last sheet with no back) and for blank backs.

**Non-goals**

- No automatic detection of whether a page is a front or a back from its content.
- No blank-back discarding by default — a blank back of an evidence page is evidence that
  the back is blank (ADR-0003 neighbourhood; §05 Q2 confirms).
- No two-pass flow in the CLI; `fgscanner.exe` keeps its existing per-pass options.
- No change to the existing Groups-page Reverse / Interleave / Deinterleave buttons,
  which remain the repair tool for stacks captured before this feature.
- No Bates numbering, ever, on this path (CLAUDE.md).
- No change to the evidence export contract.

## 04 · Current state

**Hardware duplex already reaches the driver.** `ScanSource` has three members —
`Flatbed`, `Feeder`, `Duplex` (`src/FgScanner.Scanning/ScanModels.cs:10-15`) — and
`Naps2ScanService` maps them straight through
(`src/FgScanner.Scanning/Naps2ScanService.cs:101-106`):

```csharp
PaperSource = options.Source switch
{
    ScanSource.Feeder => PaperSource.Feeder,
    ScanSource.Duplex => PaperSource.Duplex,
    _ => PaperSource.Flatbed,
},
```

The Scan page shows it as a bare enum name in the Source combo
(`src/FgScanner.App/Views/ScanView.xaml:32`, bound to `Enum.GetValues<ScanSource>()` at
`ScanViewModel.cs:73`) — no display names, no tooltip, nothing saying "both sides in one
pass". **No test anywhere sets `ScanSource.Duplex`**, and `docs/manual-tests.md:31`
("Duplex scan (if hardware supports) → front/back pages in order") has never been ticked.

**What the SDK offers and we never call.** NAPS2.Sdk 1.3.0 exposes
`ScanController.GetCaps(...)` with `PaperSourceCaps.SupportsFlatbed / SupportsFeeder /
SupportsDuplex`, and `ScanOptions.FlipDuplexedPages`. Neither appears anywhere in this
repo. A flatbed-only scanner set to Duplex therefore reaches NAPS2's
`NoDuplexSupportException`, which lands in the one generic handler
(`ScanViewModel.cs:322-327`) and shows the operator a raw driver message.

**Two-pass exists only as a repair.** `ReorderService.InterleaveAsync`
(`src/FgScanner.Data/ReorderService.cs:27-49`) already merges the first half with the
second half — "manual duplex: fronts then backs become front/back/front/back" — with
`half = (docs.Count + 1) / 2`, so an odd stack puts the extra sheet in the fronts half.
It is a button on the Groups page (`GroupsView.xaml:256-258`), applied **after** the
pages are already in a group, and it is pinned by
`tests/FgScanner.Data.Tests/ReorderServiceTests.cs:71-80`.

**The machinery for a guided flow is all present.**

- `BatchDialog` already runs several passes with a prompt between each
  (`ScanViewModel.BatchScanAsync:338-402`; the prompt at `:356-367` breaks the loop on
  Cancel). It accumulates fronts-then-backs and saves without interleaving.
- `AnnotatedCaptureSequence` (`src/FgScanner.Core/Evidence/AnnotatedCaptureSequence.cs`)
  is the proven pattern for a multi-step capture: a tiny state machine in Core, a prompt
  bound to `AnnotatedPrompt` and a Cancel control that stay on screen while the sequence
  is active (`ScanView.xaml:70-81`), and `Cancel()` returning the ids to discard so no
  half-pair survives. CLAUDE.md makes the visible prompt and reachable Cancel a hard
  requirement for that sequence, and the same reasoning applies here.
- Ordering authority is `RecoverySession.ReserveNextPagePath`
  (`src/FgScanner.Scanning/Recovery/RecoverySession.cs:66-73`), a monotonic counter
  producing `page-00001.jpg`. The final `scan_NNNNN` names are assigned at adoption, in
  the order the list is handed over (`GroupService.cs:391-395`, `:636-653`), and
  `SaveToGroupAsync` hands it over sorted by sequence (`ScanViewModel.cs:551`).

**The trap.** Adoption skips a page whose SHA-256 already exists in the group
(`GroupService.cs:386-390`). A stack of blank backs scanned at the same settings can
produce byte-identical images, so a naive two-pass flow loses backs silently and every
later pairing shifts. `FakeScanService` deliberately stamps a run number into its
bitmaps for this reason (`FakeScanService.cs:45-48, 86-87`).

**Evidence constraint.** `ScanProfileOptions` is on the legal-evidence capture path;
every new property must default to today's behaviour
(`docs/superpowers/plans/2026-08-30-quick-scan-program.md:351`).

**Not examined** — eSCL duplex behaviour on the network scanner; the WIA duplex path
(research notes that WIA's automation layer cannot do duplex, which is why NAPS2 wraps
the low-level API — `docs/research/research-2-stack.md:12`).

## 05 · Questions for Franz

> **Answered 2026-09-20, Round A part 2** — [review page](https://claude.ai/artifact/DHsGWBY5RVTi5ub4nwgyST),
> db doc `review/SPEC-2026-006-rA-p2`, verdict **approve**. Every item took option (a).
> No notes.
>
> | | |
> |---|---|
> | Q1 | (a) **Refuse to interleave on a count mismatch.** Report both counts, keep the pages unordered, let the operator rescan. Never guess a pairing. |
> | Q2 | (a) **Keep every back in a duplex run** — the checksum skip is suppressed for the run, so blank backs all survive. |
> | Q3 | (a) **Assume the backs are reversed**, with a checkbox to switch. |
> | N1 | (a) Unsupported scanners show Duplex disabled, with the reason as text. |
> | N2 | (a) One stack per sequence. |
> | N3 | (a) Its own button on the Scan page, annotated-style prompt block. |

**Blocking**

1. **When the two passes produce different page counts, what should happen?** — *why it
   matters:* it is the normal failure of a flipped stack — a double feed, a jam, a sheet
   left in the tray. Refusing to interleave and keeping both passes as they are is safe
   but leaves the operator 40 unordered pages. Interleaving as far as the shorter pass
   and appending the remainder guesses at which sheet lost its partner, and on evidence a
   confident wrong pairing is worse than an obvious mess. My recommendation is refuse,
   say exactly what was counted, and offer to keep the pages unordered so the operator can
   rescan the backs.
2. **Blank backs.** — *why it matters:* adoption drops byte-identical pages by checksum
   (`GroupService.cs:386-390`), and blank backs are identical. Three answers: keep every
   back and suppress the duplicate check for a duplex run (page count always doubles, the
   record shows a blank back exists, storage grows); keep them but let the blank-page
   policy mark them; or let them be dropped, which silently breaks pairing. The first is
   my recommendation and matches "the blank back of an evidence page is evidence".
3. **Which way do the backs come off the stack?** — *why it matters:* flipping a whole
   stack over end-for-end yields backs in **reverse** order; flipping sheet by sheet
   yields them in the same order. Getting it wrong reverses every pairing. I can assume
   reversed (the usual ADF gesture) and offer a checkbox, assume forward, or make the
   operator choose each time in the flip prompt.

**Non-blocking**

1. **Unsupported hardware: hide Duplex, or show it disabled with a reason?** —
   *proceeding as if:* show it, disabled, with "this scanner reports no duplex support" —
   a missing option reads as a missing feature, and capability reports are not always
   right.
2. **Should the two-pass flow offer to keep going for a second stack?** — *proceeding as
   if:* no. One stack, one sequence, ending in a save. Batch scan already exists for
   repeated passes.
3. **Where does the flow live on screen?** — *proceeding as if:* a "Both sides (two
   passes)" button beside "Batch scan…", with the prompt and Cancel appearing in the same
   block the annotated sheet uses (`ScanView.xaml:70-81`).

## 06 · Assumptions

| # | Assumption | If wrong |
|---|---|---|
| 1 | `GetCaps` is safe to call before a scan and does not itself open the source or wake the ADF | Capability detection would slow or disturb every device selection; fall back to offering Duplex always |
| 2 | The scanner returns fronts in stack order on pass 1 | Interleaving is wrong from the start; no software can fix it |
| 3 | A two-pass run is one scan session, so `RecoverySession` sequence numbers span both passes | Ordering must instead be tracked per pass in the view model |
| 4 | `FlipDuplexedPages` only rotates backs and does not reorder them | Rotation and ordering would both change at once and mask each other |
| 5 | The operator saves to a group after the sequence, not during it | Interleaving before adoption is impossible and it must happen after, via `ReorderService` |

## 07 · Data model

**No schema change, no migration.** Pages and documents are unchanged; the difference is
only the order in which files reach `AdoptPagesAsync`.

One record gains properties, defaulted to today's behaviour per the evidence constraint:

```
ScanProfileOptions (src/FgScanner.Scanning/ScanModels.cs:37-50)
  + FlipDuplexedPages : bool = false     // corrects upside-down backs on hardware duplex
```

And a new pure-domain type in Core, mirroring `AnnotatedCaptureSequence`:

```
DuplexPassSequence (src/FgScanner.Core/Capture/DuplexPassSequence.cs)
  State        : Inactive | Fronts | AwaitingFlip | Backs
  FrontCount   : int
  BackCount    : int
  BacksReversed: bool          // §05 Q3
  Start() / RecordPass(int pages) / Cancel() → the page paths to discard
  Interleave(IReadOnlyList<string> fronts, IReadOnlyList<string> backs) → ordered paths
```

It holds no database and no WPF types, so it is unit-testable without a scanner, which is
what CLAUDE.md requires. Documented with doc comments explaining why interleaving happens
before adoption (§08) and what a mismatch means (§05 Q1).

## 08 · Architecture and approach

**Interleave before adoption, not after.** `AdoptPagesAsync` numbers pages strictly in
the order it receives them (`GroupService.cs:391-395`), so reordering the list before the
save means the `scan_NNNNN` filenames, the `Document.Sequence` values and the index rows
all agree from the start. The alternative — adopt fronts-then-backs and run
`ReorderService.InterleaveAsync` afterwards — renumbers rows that are already written,
leaves the filenames in capture order, and on `PreserveOriginals` groups leaves the
`originals\` archive ordered differently from the pages. Rejected for that reason; the
Groups-page buttons remain for repairing older stacks.

**The sequence is a Core state machine with a visible prompt.** `DuplexPassSequence`
follows `AnnotatedCaptureSequence` exactly: state in Core, prompts and a Cancel control
bound in `ScanView.xaml` and shown for as long as the sequence is active, and every
transition announced so the Cancel button cannot go missing while a stack is half
captured. CLAUDE.md pins that rule for annotated sheets because an invisible sequence
strands half a pair; a half-captured duplex stack is the same failure with more pages.

**Capability detection.** `Naps2ScanService` gains a `GetCapabilitiesAsync(device)` that
calls `ScanController.GetCaps` and returns which sources the device reports. `IScanService`
grows the same method so `FakeScanService` can answer it without hardware. The Source
list is built from that answer; `NoDuplexSupportException` and `DeviceFeederEmptyException`
get named handling instead of the generic catch (`ScanViewModel.cs:322-327`).

**Blank backs.** Whatever §05 Q2 decides is implemented as an explicit flag on the
adoption call rather than as a change to `GroupService`'s default behaviour — the
checksum skip protects every other path and must stay the default.

**Alternatives considered.** *Extending `BatchDialog` to two passes with a flip prompt* —
tempting, since it is 95% of the mechanics, but a `MessageBox` between passes cannot show
the state, cannot be cancelled cleanly, and would leave the operator with an unordered
group if they dismissed it. *Detecting fronts and backs from content* — rejected under
§08a. *Doing nothing and teaching the Groups buttons* — that is today, and it is what was
reported as not good enough.

**Data access.** No new query patterns; interleaving is in memory over a list of file
paths.

**System evolution.** `DuplexPassSequence` belongs in `FgScanner.Core/Capture/` beside
the existing capture types, not in a skill.

## 08a · AI opportunity assessment

| | |
|---|---|
| **What it would do** | Decide from the image whether a page is a blank back, and whether a back belongs to the front above it |
| **Why AI beats deterministic code here** | It does not, at the price. Blankness is already decided deterministically by the existing blank-page policy, and pairing is a counting problem, not a perception problem |
| **Model and rough cost per operation** | Gemini Flash-class, a fraction of a cent per page — but 40 pages per stack, on the capture path, adds latency to every sheet |
| **What happens when it is wrong** | A mis-paired exhibit that nobody notices until it is read in a deposition |
| **Recommendation** | **Not worth it** |

AI has a genuine place in this product (descriptions, OCR reconstruction) but not between
the scanner and the page order. A wrong answer here is invisible and consequential, and
the deterministic version is exact. Not recommended, and no learning loop is proposed
because no model is used.

## 09 · Design system compliance

The app's own WPF Fluent theme governs.

- The Source combo gains display names rather than raw enum text: "Flatbed",
  "Feeder (one side)", "Feeder (both sides, one pass)".
- The two-pass prompt and Cancel reuse the annotated block's layout
  (`ScanView.xaml:70-81`) so the operator meets one idiom for "a sequence is in hand".
- Accessibility: the prompt is a labelled `TextBlock` read in order before the buttons;
  Cancel is keyboard reachable at all times while active; the disabled Duplex option
  carries its reason as a tooltip **and** as text, since a tooltip alone is not reachable
  by keyboard.
- A new shortcut, if any, must be declared with the section it belongs to
  (`ShortcutRouter`, SPEC-2026-003) — the two-pass flow belongs to Scan only.

## 10 · Acceptance criteria

> **AC-1** — Given a scanner reporting no duplex support, the Duplex source is not
> selectable and the reason is shown as text.
> *Proven by:* `tests/FgScanner.App.Tests/DuplexOptionTests.cs` → "an unsupported device
> disables the duplex source"

> **AC-2** — Given a scanner reporting duplex support, selecting it passes
> `PaperSource.Duplex` to the driver, and `FlipDuplexedPages` follows the checkbox.
> *Proven by:* `tests/FgScanner.Scanning.Tests/ScanOptionMappingTests.cs` → "duplex and
> flip reach the scan options"

> **AC-3** — A two-pass run of 5 fronts then 5 backs produces 10 pages in the order
> F1,B1,F2,B2,F3,B3,F4,B4,F5,B5 before they reach the group.
> *Proven by:* `tests/FgScanner.Core.Tests/DuplexPassSequenceTests.cs` → "fronts and backs
> interleave into sheet order"

> **AC-4** — With backs captured from a stack flipped end-for-end, the last back pairs
> with the first front.
> *Proven by:* same file → "reversed backs are paired from the end"

> **AC-5** — An odd stack (5 fronts, 4 backs by design — the last sheet is single-sided)
> is handled per §05 Q1 and never pairs a back with the wrong front.
> *Proven by:* same file → "an odd stack leaves the last sheet unpaired"

> **AC-6** — When the two passes disagree on count, the behaviour is exactly what §05 Q1
> chose, and the operator is told both counts.
> *Proven by:* `tests/FgScanner.App.Tests/DuplexScanTests.cs` → "a count mismatch is
> reported with both numbers"

> **AC-7** — Blank backs are handled per §05 Q2: with the recommended answer, 10 sheets
> with blank backs produce 20 pages, not 11.
> *Proven by:* `tests/FgScanner.Data.Tests/DuplexAdoptionTests.cs` → "identical blank
> backs are all kept in a duplex run"

> **AC-8** — While a two-pass sequence is active, the prompt and the Cancel control are
> both visible, and every state change announces itself.
> *Proven by:* `tests/FgScanner.App.Tests/DuplexScanTests.cs` → "the sequence is visible
> and cancellable at every step" (the analogue of the annotated-sheet test CLAUDE.md
> requires)

> **AC-9** — Cancelling a two-pass sequence leaves no pages on disk and no rows in the
> database from either pass.
> *Proven by:* same file → "abandoning a stack discards both passes"

> **AC-10** — An ordinary single-sided scan is unchanged: same options, same order, same
> files.
> *Proven by:* existing `tests/FgScanner.Scanning.Tests/FakeScanServiceTests.cs:23-37`,
> unmodified and still passing

## 11 · Test strategy

**11.1 — The failing tests to write first**

| Feature | Test file | The failing assertion |
|---|---|---|
| Interleaving | `tests/FgScanner.Core.Tests/DuplexPassSequenceTests.cs` (new) | 5 fronts + 5 backs → F1,B1,F2,B2,… |
| Reversed backs | same | Backs consumed from the end |
| Odd stack | same | 5 fronts + 4 backs → last front unpaired, no wrong pairing |
| State machine | same | Cancel from each state returns every captured path; no transition leaves it invisible |
| Count mismatch | `tests/FgScanner.App.Tests/DuplexScanTests.cs` (new) | Both counts reported; no interleave performed |
| Capability gating | `tests/FgScanner.App.Tests/DuplexOptionTests.cs` (new) | `FakeScanService` reporting no duplex disables the option |
| Option mapping | `tests/FgScanner.Scanning.Tests/ScanOptionMappingTests.cs` (new) | `Duplex` → `PaperSource.Duplex`; `FlipDuplexedPages` carried |
| Blank backs kept | `tests/FgScanner.Data.Tests/DuplexAdoptionTests.cs` (new) | Ten identical blank images all adopted in a duplex run |

**11.2 — Test data.** `FakeScanService` throughout — no scanner, per CLAUDE.md. It needs
two additions: a reported capability set, and a mode that returns **byte-identical**
pages, so the blank-back case is real rather than hypothetical (today it deliberately
stamps a run number to avoid exactly this, `FakeScanService.cs:45-48`). Both default to
current behaviour so existing tests are untouched.

**11.3 — Verification suite**

- `dotnet build -c Release`, `dotnet test -c Release` (≥ 692), `dotnet format --verify-no-changes`.
- Manual, and this one genuinely needs the scanner Franz is testing:
  - hardware duplex on a duplex-capable device, 3 sheets, checked for order and rotation;
  - two-pass on a feeder-only device, 10 double-sided sheets, checked page by page;
  - a deliberate mismatch (remove a sheet before the back pass) to see the message;
  - a stack with blank backs;
  - cancel mid-sequence, then confirm nothing is left in the session folder.
  These become new rows in `docs/manual-tests.md`, replacing the three that have sat
  unticked since the project started.

## 12 · Edge cases and failure modes

| Case | Expected behaviour | Where handled |
|---|---|---|
| Zero pages in the front pass | Sequence ends, nothing captured, plain message | `DuplexPassSequence.RecordPass` |
| Zero pages in the back pass | Treated as a mismatch per §05 Q1 | same |
| Feeder empty on the back pass | Named message ("no pages in the feeder"), sequence stays at AwaitingFlip so the operator can retry | `DeviceFeederEmptyException` handler |
| Jam mid-back-pass | Pages already captured are kept; the sequence reports the partial count and offers cancel or retry | existing partial-run behaviour (`FakeScanServiceTests.cs:57`) |
| Cancel during the flip prompt | Both passes discarded; session folder empty | AC-9 |
| App killed mid-sequence | Crash recovery offers the pages as an ordinary session; the sequence is not resumed, and the operator is told the pages are unordered | `RecoverySession`; §14 |
| Duplex chosen on a flatbed | Option disabled beforehand; if the driver still refuses, the named handler explains it | AC-1 |
| Hardware duplex returns backs upside down | `FlipDuplexedPages` checkbox corrects it | AC-2 |
| Two-pass started while an annotated sheet is in hand | Refused with "finish or cancel the sheet in hand first", mirroring `AnnotatedCaptureSequence.Start` | `DuplexPassSequence.Start` |
| A duplicate page that is genuinely a rescan | Still skipped outside a duplex run; inside one, kept per §05 Q2 | explicit flag, §08 |

## 13 · Security and configuration

_Not applicable in the web sense._ No new dependency, no new configuration, no network
surface, no new file-path input — the paths come from `RecoverySession`, which owns its
own folder. Hardware access stays behind `IScanService` (CLAUDE.md).

## 14 · Observability

- **Logged** at Information: sequence started; pages captured per pass; the interleave
  decision with both counts and whether backs were reversed; sequence cancelled with the
  number of pages discarded.
- **Logged** at Warning: a count mismatch, a capability probe that failed, a duplex scan
  refused by the driver.
- **Surfaced to the operator**: the prompt block on the Scan page carries the state in
  words ("10 fronts captured — turn the stack over and scan the backs"), and the status
  line reports the final order.
- **A silent failure** would be a lost back and a shifted pairing. What makes it
  non-silent: the counts are logged and shown, a mismatch stops the interleave, and the
  blank-back decision removes the one path that could drop a page without saying so.
- **Who notices** — the operator, immediately, from the count in the prompt; and anyone
  reading the log afterwards.

## 15 · Performance and scale

A stack is tens of pages, not thousands: the largest group in the real data is 223 pages
(`Multiple pages in stack`). Interleaving is a list operation over file paths, trivially
fast. The work is dominated by the scanner itself.

The one thing to watch: a two-pass run holds both passes in the session folder before
saving, so peak disk use is the whole stack at once — about 2 MB per page at 300 DPI
colour, so a 200-sheet double-sided stack is roughly 800 MB in `%APPDATA%`. Worth a note
in the docs; revisit if stacks routinely exceed ~500 sheets.

## 16 · Regression risk

| # | What could break | Why | Evidence (file:line) | Mitigation |
|---|---|---|---|---|
| R1 | Backs silently lost | Adoption skips pages whose checksum already exists, and blank backs are identical | `GroupService.cs:386-390` | §05 Q2; AC-7; the flag is scoped to duplex runs only |
| R2 | Ordinary scans change behaviour | `ScanProfileOptions` is on the evidence capture path; a new property that defaults to anything but today's behaviour changes every capture | `ScanModels.cs:37-50`; quick-scan plan `:351` | `FlipDuplexedPages = false` by default; AC-10 keeps the existing test unmodified |
| R3 | A half-captured stack becomes invisible | If a state change is not announced, the prompt and Cancel disappear while pages are in hand — the exact failure CLAUDE.md pins for annotated sheets | `ScanViewModel.cs:431-444`; `ScanView.xaml:66-81` | AC-8 pins visibility at every step |
| R4 | The annotated-sheet sequence is disturbed | Both sequences live in `ScanViewModel` and both own the Scan page's prompt area | `ScanViewModel.cs:408-444` | Mutually exclusive: each refuses to start while the other is active; tested |
| R5 | Capability probing breaks device listing | `GetCaps` is unproven in this solution and may be slow or may throw on some drivers | NAPS2 1.3.0 API, never called here | Probe once per device selection, never on the scan path; any failure degrades to "offer everything" |
| R6 | Recovery after a crash restores an unordered stack | The recovery session knows nothing about passes | `RecoverySession.cs`; `ScanSessionService.cs:44-60` | Recovered pages are presented as ordinary pages with a message saying the order was not applied |
| R7 | `PreserveOriginals` archive ordering | Originals are archived per page at adoption; interleaving before adoption keeps them aligned, but interleaving after would not | ADR-0003; `GroupService.MoveIntoGroup:636-653` | Interleave before adoption (§08) |

## 17 · Migration and rollback

_No data migration._ Code only.

- **Forward** — ships in the next installer.
- **Backward** — reinstall the previous installer. Pages already captured and saved are
  ordinary pages; nothing about them depends on this feature.
- **Point of no return** — none. A group captured with two-pass duplex is indistinguishable
  from one ordered by hand.
- **Backup** — unchanged; the automatic pre-migration database backup still applies.

## 18 · Documentation updates

- **CLAUDE.md** — add `DuplexPassSequence` beside the annotated-sheet paragraph, with the
  same rule: the prompt and Cancel stay visible while a stack is in hand, and every state
  change is announced.
- **docs/user-guide.md** — a "Both sides of the paper" section: which option to use when,
  what the flip prompt means, and what happens if the counts disagree.
- **docs/manual-tests.md** — replace the three unticked duplex rows with the five checks
  in §11.3.
- **docs/FEATURE-PARITY.md** — duplex moves from pending to shipped, with the two-pass
  flow noted as beyond NAPS2's own behaviour.
- **docs/adr/0011-duplex-ordering.md** — new ADR: why interleaving happens before
  adoption, and what the blank-back decision was.
- **Memory** — record that two-pass duplex exists on the Scan page, so a later session
  does not send an operator to the Groups-page Interleave button.

## 19 · Phase plan

### Phase 1 — Honest hardware duplex
- **Objective** — the existing option tells the truth.
- **Delivers** — capability detection through `IScanService`, display names, the disabled
  option with a reason, `FlipDuplexedPages`, named handling for no-duplex and empty-feeder.
- **Not in this phase** — anything two-pass.
- **Done when** — AC-1, AC-2, AC-10 pass.

### Phase 2 — The two-pass sequence
- **Objective** — the ordering logic exists and is proven without a scanner.
- **Delivers** — `DuplexPassSequence` in Core with its tests: interleave, reversed backs,
  odd stacks, mismatch, cancel.
- **Not in this phase** — UI, adoption, blank-back handling.
- **Done when** — AC-3, AC-4, AC-5 pass.

### Phase 3 — The flow on screen
- **Objective** — the operator can run a stack end to end.
- **Delivers** — the Scan page button, prompt and Cancel; the mismatch message; the
  blank-back handling at adoption; the manual test rows.
- **Not in this phase** — CLI support, a second stack in one sequence.
- **Done when** — AC-6..AC-9 pass and the manual checks in §11.3 are recorded.

## 20 · Prompt pack

[SPEC-2026-006-duplex-capture-PROMPTS.md](./SPEC-2026-006-duplex-capture-PROMPTS.md)
— written once this spec is approved (§22).

## 21 · Definition of done

- [ ] All acceptance criteria met
- [ ] Failing tests written first, now passing
- [ ] Full suite green (≥ 692)
- [ ] `/code-review max` run, findings resolved or accepted in writing
- [ ] Security review — _not applicable_
- [ ] Documentation updated per §18
- [ ] Manual scanner checks in §11.3 recorded in `docs/manual-tests.md`
- [ ] Rollback tested or explicitly waived by Franz

## 22 · Sign-off

| | |
|---|---|
| **Review round answered** | ☑ 2026-09-20 — [Round A, part 2 of 3](https://claude.ai/artifact/DHsGWBY5RVTi5ub4nwgyST) · db doc `review/SPEC-2026-006-rA-p2` |
| **Franz approved** | ☑ 2026-09-20 (verdict `approve`, all items option (a)) |
| **Built** | ☐ date: |
| **Verified in production** | ☐ date: |
