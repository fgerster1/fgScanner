# SPEC-2026-006 — Prompt pack

Prompts for [SPEC-2026-006-duplex-capture.md](./SPEC-2026-006-duplex-capture.md).
Work through them in order. Each stops at a checkpoint; paste the results back before
starting the next.

Branch: `phase-25-duplex`. Approved 2026-09-20; build third.

**This is the one spec that cannot be finished without the scanner.** Prompts 1–5 run on
`FakeScanService` alone; Prompt 7 needs hardware in front of you.

---

```
PROMPT 1 of 8 — Ask the scanner what it can do
Spec: SPEC-2026-006 §04, §08, §10 (AC-1), §16 R5    Phase: 1    Depends on: nothing

Read SPEC-2026-006 §04 and §08 before starting. Create the branch phase-25-duplex from
main if it does not exist.

Context: the Source combo offers all three ScanSource values unconditionally
(ScanViewModel.cs:73, ScanView.xaml:32). NAPS2.Sdk 1.3.0 exposes ScanController.GetCaps
with PaperSourceCaps.SupportsFlatbed/SupportsFeeder/SupportsDuplex — never called in this
repo. A flatbed-only scanner set to Duplex therefore hits NoDuplexSupportException and the
operator sees a raw driver message from the generic catch (ScanViewModel.cs:322-327).

§05 N1 was answered (a): show the option DISABLED with the reason, do not hide it.

Do only this:
1. Failing tests FIRST in tests/FgScanner.App.Tests/DuplexOptionTests.cs (new): a fake
   device reporting no duplex leaves the Duplex source disabled with a reason string; one
   reporting duplex leaves it enabled.
2. Add a capabilities method to IScanService, implemented in Naps2ScanService via
   GetCaps and in FakeScanService as settable test state (default: everything supported,
   so existing tests are untouched).
3. Probe once per device selection, never on the scan path. Any failure degrades to
   "offer everything" rather than blocking the scan (§16 R5).
4. Give the Source combo display names: "Flatbed", "Feeder (one side)",
   "Feeder (both sides, one pass)".

Do NOT in this prompt: FlipDuplexedPages, the two-pass sequence, or any UI beyond the
Source combo. Those are Prompts 2, 3 and 5.

Stop when the checkpoint below is satisfied and report the results.

CHECKPOINT 1 — paste back:
  · dotnet test -c Release → count and "passed"; no lower than 692
  · the new test names, failing first
  · confirm every existing scanning test still passes unmodified
  · with --fake-scanner: the Source list now reads as display names, not enum names
```

```
PROMPT 2 of 8 — FlipDuplexedPages, and named errors
Spec: SPEC-2026-006 §07, §10 (AC-2, AC-10), §16 R2    Phase: 1    Depends on: Prompt 1 (green)

Read SPEC-2026-006 §07 before starting.

THE CONSTRAINT: ScanProfileOptions is on the legal-evidence capture path. Every new
property must default to today's behaviour, or every existing capture changes
(quick-scan plan :351, §16 R2). FlipDuplexedPages defaults to false.

Do only this:
1. Failing tests FIRST in tests/FgScanner.Scanning.Tests/ScanOptionMappingTests.cs (new):
   Source=Duplex maps to PaperSource.Duplex; FlipDuplexedPages is carried through;
   the default options produce exactly what they produce today.
2. Add FlipDuplexedPages to ScanProfileOptions (default false) and map it in
   Naps2ScanService.BuildOptions.
3. Add a checkbox on the Scan page, enabled only when the source is Duplex.
4. Replace the raw-message path for NoDuplexSupportException and DeviceFeederEmptyException
   with named, plain-English handling (ScanViewModel.cs:322-327 keeps its generic catch as
   the last resort).

Do NOT in this prompt: anything two-pass.

Stop when the checkpoint below is satisfied and report the results.

CHECKPOINT 2 — paste back:
  · dotnet test -c Release → count and "passed"
  · the new test names, failing first
  · git diff src/FgScanner.Scanning/ScanModels.cs → one added property, defaulted false
  · confirm FakeScanServiceTests.cs:23-37 is unmodified and green
```

```
PROMPT 3 of 8 — The ordering logic, with no UI and no scanner
Spec: SPEC-2026-006 §07, §08, §10 (AC-3..AC-5), §11.1    Phase: 2    Depends on: Prompt 2 (green)

Read SPEC-2026-006 §07 and §08 before starting. This prompt is pure domain logic in Core
— no WPF, no database, no scanner. Model it on
src/FgScanner.Core/Evidence/AnnotatedCaptureSequence.cs, which is the proven shape.

Decisions already made (§05): a count mismatch REFUSES to interleave and reports both
counts (Q1a); backs are assumed REVERSED with a checkbox to switch (Q3a); one stack per
sequence (N2a).

Do only this:
1. Failing tests FIRST in tests/FgScanner.Core.Tests/DuplexPassSequenceTests.cs (new):
   - 5 fronts + 5 backs → F1,B1,F2,B2,F3,B3,F4,B4,F5,B5;
   - reversed backs pair from the end;
   - 5 fronts + 4 backs → the last front is left unpaired, nothing is mis-paired;
   - a count mismatch returns a refusal carrying both counts, not an order;
   - Cancel from every state returns every captured path;
   - Start is refused while a sequence is already in hand (mirror
     AnnotatedCaptureSequence's wording).
2. Write src/FgScanner.Core/Capture/DuplexPassSequence.cs to make them pass: states
   Inactive / Fronts / AwaitingFlip / Backs, FrontCount, BackCount, BacksReversed,
   Start, RecordPass, Cancel, Interleave.
3. Doc comments must say WHY interleaving happens before adoption and what a mismatch
   means — not what the code does.

Do NOT in this prompt: touch ScanViewModel, the XAML, GroupService or ReorderService.

Stop when the checkpoint below is satisfied and report the results.

CHECKPOINT 3 — paste back:
  · dotnet test -c Release → count and "passed"
  · the six test names, failing first
  · grep -n "using" src/FgScanner.Core/Capture/DuplexPassSequence.cs → no WPF, no EF
```

```
PROMPT 4 of 8 — Blank backs survive the save
Spec: SPEC-2026-006 §05 Q2, §08, §10 (AC-7), §16 R1    Phase: 2    Depends on: Prompt 3 (green)

Read SPEC-2026-006 §16 R1 before starting. This is the prompt that stops pages
disappearing.

Context: GroupService.AdoptPagesAsync skips a page whose SHA-256 already exists in the
group (GroupService.cs:386-390). Blank backs are byte-identical, so ten sheets with blank
backs would save one blank and drop nine, shifting every pairing after it.
FakeScanService deliberately stamps a run number into its bitmaps to avoid this
(FakeScanService.cs:45-48), which is why no existing test catches it.

§05 Q2 was answered (a): keep EVERY back in a duplex run.

Do only this:
1. Failing tests FIRST in tests/FgScanner.Data.Tests/DuplexAdoptionTests.cs (new): ten
   byte-identical images adopted as part of a duplex run all survive; the same ten adopted
   normally still de-duplicate as they do today.
2. Add a mode to FakeScanService that returns byte-identical pages, defaulted OFF so no
   existing test changes.
3. Pass an explicit flag on the adoption call for a duplex run. Do NOT change
   GroupService's default behaviour — the checksum skip protects every other path.

Do NOT in this prompt: the blank-page policy, the UI, or the trash.

Stop when the checkpoint below is satisfied and report the results.

CHECKPOINT 4 — paste back:
  · dotnet test -c Release → count and "passed"
  · the two new test names, failing first
  · confirm the normal (non-duplex) de-duplication test still passes
  · say in one line where the flag is set and where it is read
```

```
PROMPT 5 of 8 — The flow on the Scan page
Spec: SPEC-2026-006 §08, §09, §10 (AC-6, AC-8, AC-9), §16 R3, R4    Phase: 3    Depends on: Prompt 4 (green)

Read SPEC-2026-006 §08 and §09, and read how the annotated sheet does it:
ScanViewModel.cs:408-511 and ScanView.xaml:61-81.

THE RULE (CLAUDE.md, and §16 R3): while a sequence is active, the prompt AND the Cancel
control stay on screen, and every state change is announced — the annotated sheet has
AnnouncedAnnotatedState() for exactly this reason. A stack half captured with nothing on
screen saying so is the failure this design exists to prevent.

§05 N3 was answered (a): its own button beside "Batch scan…", using the same prompt block.

Do only this:
1. Failing tests FIRST in tests/FgScanner.App.Tests/DuplexScanTests.cs (new):
   - the prompt and the Cancel control are available at every step of a run;
   - a count mismatch reports both numbers and does not interleave;
   - cancelling discards both passes and leaves nothing on disk;
   - starting a duplex sequence while an annotated sheet is in hand is refused, and
     vice versa (§16 R4).
2. Wire DuplexPassSequence into ScanViewModel: a "Both sides (two passes)" command, the
   flip prompt, the reversed-backs checkbox, the Cancel, and the interleave applied to the
   page list BEFORE SaveToGroupAsync hands it to adoption.
3. Bind the prompt and Cancel in ScanView.xaml in the same block the annotated flow uses.

Do NOT in this prompt: the CLI, a second stack per sequence, or any change to
ReorderService and the Groups-page buttons.

Stop when the checkpoint below is satisfied and report the results.

CHECKPOINT 5 — paste back:
  · dotnet test -c Release → count and "passed"
  · the four new test names, failing first
  · with --fake-scanner: run a two-pass sequence and paste the prompt text at each step
  · confirm the Groups-page Interleave button is untouched
```

```
PROMPT 6 of 8 — Code review  [START A NEW SESSION FOR THIS]
Spec: SPEC-2026-006    Depends on: Prompt 5 (green)

Run /code-review max over the changes made in Prompts 1..5.

Then check compliance with the spec: does the code do what SPEC-2026-006 §10 says, and
only that? Report any drift.

Pay particular attention to §16: blank backs kept (R1), ordinary scans unchanged (R2), the
sequence never invisible (R3), the two sequences mutually exclusive (R4), capability
probing degrading safely (R5), recovery after a crash (R6), and interleaving happening
before adoption so the originals\ archive stays aligned (R7).

Resolve every correctness finding. For anything you decline to change, write one line in
the spec's §22 saying what and why.

CHECKPOINT 6 — paste back:
  · the findings list with each one's resolution
  · dotnet test -c Release → count and "passed"
  · dotnet format --verify-no-changes → clean
```

```
PROMPT 7 of 8 — On the scanner  [NEEDS HARDWARE — FRANZ RUNS THIS]
Spec: SPEC-2026-006 §11.3    Depends on: Prompt 6 (green)

Build the installer, install it, and work through these with real paper. Record each in
docs/manual-tests.md as it is done — do not tick anything not actually performed.

NAME THE SCANNER ON EVERY ROW. The dev station has an HP ENVY 7640; Jim's station has a
different machine. Duplex support is per-device, so a row passed on one proves nothing
about the other. Anything the dev scanner cannot exercise stays OPEN and is repeated on
Jim's station before this spec is Done.

1. Hardware duplex on a duplex-capable scanner: 3 double-sided sheets. Check order AND
   rotation; if the backs are upside down, tick the flip checkbox and repeat.
2. Duplex selected on a scanner that cannot do it: confirm it is disabled with a readable
   reason rather than a driver error.
3. Two-pass on a feeder-only scanner: 10 double-sided sheets. Check the pages land
   1F,1B,2F,2B… all the way down.
4. Two-pass with blank backs: confirm 10 sheets give 20 pages, not 11.
5. Deliberate mismatch: remove a sheet before the back pass. Confirm both counts are
   reported and nothing is interleaved.
6. Cancel mid-sequence: confirm the session folder is empty afterwards.
7. Odd stack: 5 sheets where the last is single-sided.

CHECKPOINT 7 — paste back:
  · which of the seven passed, and the exact wording of anything that surprised you
  · the scanner's make and model
  · %LOCALAPPDATA%\FGScanner\logs\app-*.log for the session, if anything failed
```

```
PROMPT 8 of 8 — Documentation, ADR and memory
Spec: SPEC-2026-006 §18    Depends on: Prompt 7 (green)

Do only this:
1. Write docs/adr/0011-duplex-ordering.md: why interleaving happens before adoption rather
   than through ReorderService afterwards, and the blank-back decision with its reason.
2. CLAUDE.md — add DuplexPassSequence beside the annotated-sheet paragraph, with the same
   rule about the prompt and Cancel staying visible.
3. docs/user-guide.md — a "Both sides of the paper" section: which option when, what the
   flip prompt means, what happens when the counts disagree.
4. docs/manual-tests.md — replace the three old unticked duplex rows with the results from
   Prompt 7.
5. docs/FEATURE-PARITY.md — duplex moves to shipped; note the two-pass flow goes beyond
   NAPS2's own behaviour.
6. Memory: record that two-pass duplex lives on the Scan page, so a later session does not
   send an operator to the Groups-page Interleave button.
7. Tick §21 in the spec and fill §22's Built row.

Do NOT in this prompt: change any source file.

CHECKPOINT 8 — paste back:
  · git diff --stat → docs and memory only, no src/
  · the ADR filename
  · dotnet test -c Release → unchanged count, still green
```
