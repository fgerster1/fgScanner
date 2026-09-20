# SPEC-2026-007 — Prompt pack

Prompts for [SPEC-2026-007-email-scanned-pages.md](./SPEC-2026-007-email-scanned-pages.md).
Work through them in order. Each stops at a checkpoint; paste the results back before
starting the next.

Branch: `phase-26-email`. Approved 2026-09-20; build fourth and last.

**Prompt 1 can veto the rest.** It changes the target framework, and if that breaks the
build, the tests, the publish or the installer, the answer is to stop and report — not to
ship the fallback route (§05 N3a).

---

```
PROMPT 1 of 7 — The spike  [MAY STOP THE SPEC]
Spec: SPEC-2026-007 §08, §10 (AC-8), §16 R1    Phase: 1    Depends on: nothing

Read SPEC-2026-007 §08 and §16 R1, and read the quick-scan plan's Phase 10 spike
conditions (docs/superpowers/plans/2026-08-30-quick-scan-program.md:880-888).

Create the branch phase-26-email from main. This prompt writes NO feature code.

Do only this:
1. Change the App (and Scanning, if it must follow) target framework from
   net10.0-windows to the Windows SDK TFM the WinRT projection needs
   (net10.0-windows10.0.x), and nothing else.
2. Prove all four, and write the evidence into SPEC-2026-007 §22 as you go:
   a. dotnet build -c Release → clean (warnings are errors)
   b. dotnet test -c Release → green, count no lower than before
   c. dotnet publish src/FgScanner.App -p:PublishProfile=win-x64 → the output still has
      SEPARATE NAPS2.*.dll files, is not single-file and is not trimmed. This is the
      LGPL separation in CLAUDE.md and is not negotiable.
   d. build the installer per CLAUDE.md, install it, and confirm the app starts and scans
      with --fake-scanner.
3. If ANY of the four fails: stop. Revert the branch, write what failed into §22, and hand
   it back to Franz. Do not proceed to Prompt 2, and do not substitute the MAPI/Explorer
   route on your own — Franz decided (§05 N3a) that he makes that call.

Do NOT in this prompt: add any email code, any P/Invoke, any UI, or any package.

Stop when the checkpoint below is satisfied and report the results.

CHECKPOINT 1 — paste back:
  · the four results, each with its command output
  · ls of publish/win-x64/*.dll showing NAPS2 assemblies present and separate
  · git diff → *.csproj only
  · an explicit GO or STOP
```

```
PROMPT 2 of 7 — The share service and its three routes
Spec: SPEC-2026-007 §08, §10 (AC-5, AC-6, AC-7), §13    Phase: 2    Depends on: Prompt 1 (GO)

Read SPEC-2026-007 §08 and §13 before starting.

The route order, decided 2026-09-13 and not up for revisiting:
  1. Windows Share sheet — DataTransferManager via IDataTransferManagerInterop.GetForWindow
  2. Simple MAPI (MAPISendMailW) — ONLY when the probe passes
  3. Explorer with the file selected — already written at GroupDetailViewModel.cs:429-434

The probe (spike 3, research doc :117-119) — PROBE, NEVER INVOKE. Offer MAPI only when all
three hold: HKLM\SOFTWARE\Clients\Mail has a non-empty default; that client's
DLLPathEx/DLLPath points at a file that EXISTS; Windows Messaging Subsystem has a
non-empty MAPI value. All three are false on this station.

NEVER SENDS: the interface has no send method. It opens a message and returns.

Do only this:
1. Failing tests FIRST:
   - tests/FgScanner.App.Tests/MapiProbeTests.cs (new): three cases, each with one
     condition missing → MAPI not offered. Registry readings go through a small
     abstraction; no test touches the real registry.
   - tests/FgScanner.App.Tests/EmailCommandTests.cs (new): with every route unavailable,
     the result is the plain fallback sentence, never an exception message or an error code.
2. Write IShareService (Core) and its Windows implementation (App), plus FakeShareService
   in the test project recording what it was asked to share and which route was chosen —
   modelled on how FakeScanService stands in for hardware.
3. Follow the existing P/Invoke style (SHFileOperationW in IStagedPageDiscarder.cs:63).
   Do not copy anything from NAPS2.Lib — it is GPL (CLAUDE.md).

Do NOT in this prompt: buttons, dialogs, attachment building, or the evidence warning.

Stop when the checkpoint below is satisfied and report the results.

CHECKPOINT 2 — paste back:
  · dotnet test -c Release → count and "passed"
  · the new test names, failing first
  · confirm IShareService exposes no method that sends
  · the probe's three conditions, quoted from your implementation
```

```
PROMPT 3 of 7 — Attachments
Spec: SPEC-2026-007 §07, §08, §10 (AC-9), §12, §15    Phase: 2    Depends on: Prompt 2 (green)

Read SPEC-2026-007 §07 and §15 before starting.

Context: PdfExportService.ExportAsync and ImageExportService.ExportAsync already produce
exactly the artefacts the export buttons produce. Reuse them — an emailed PDF must be the
same file an exported PDF would be.

Do only this:
1. Failing tests FIRST in tests/FgScanner.App.Tests/EmailCommandTests.cs:
   - building attachments writes nothing into the group folder (count files before and
     after);
   - a missing page file produces a named message identifying the page, and nothing is
     shared.
2. Build attachments into a per-send temp folder; clean them on app exit.
3. Add the size guard from §15: warn above 20 MB, let the operator continue.
4. Add the Email.Attachment setting (Pdf | Images, default Pdf) through
   AppSettingsService, read fresh per send.

Do NOT in this prompt: the buttons, the dialog, or the evidence warning.

Stop when the checkpoint below is satisfied and report the results.

CHECKPOINT 3 — paste back:
  · dotnet test -c Release → count and "passed"
  · the two new test names, failing first
  · say where temp attachments land and what deletes them
```

```
PROMPT 4 of 7 — The two buttons
Spec: SPEC-2026-007 §05 N2, §09, §10 (AC-1..AC-4)    Phase: 3    Depends on: Prompt 3 (green)

Read SPEC-2026-007 §09 before starting.

Decisions already made: email appears on BOTH pages (§05 Q1a — this amends SPEC-2026-003,
handled in Prompt 7); one selected row means THAT row for email (§05 N2a); the attachment
dialog remembers the last choice (§05 Q3a).

THE TRAP: ExportImagePaths (GroupDetailViewModel.Editing.cs:281-285) treats a selection of
ONE as "the whole group", and it has no tests. Email needs its own selection rule. Do not
change the export commands' behaviour.

Do only this:
1. Failing tests FIRST in tests/FgScanner.App.Tests/EmailCommandTests.cs:
   - three selected rows → exactly those three, in sequence order;
   - one selected row → that one row;
   - no selection → the whole group;
   - the Scan page hands over its session pages ordered by sequence.
2. Add the Groups toolbar button beside Export PDF… / Export images… / Print… / Copy
   (GroupsView.xaml:259-262) and the Scan page button beside Separator sheet…
   (ScanView.xaml:86-89).
3. Add the attachment dialog, shaped like ExportPdfDialog/ExportImagesDialog.
4. Disable both commands when there is nothing to send, with the reason.

Do NOT in this prompt: the evidence warning (Prompt 5), any change to the export commands,
or any new keyboard shortcut.

Stop when the checkpoint below is satisfied and report the results.

CHECKPOINT 4 — paste back:
  · dotnet test -c Release → count and "passed"
  · the four new test names, failing first
  · git diff src/FgScanner.App/Views/GroupDetailViewModel.Editing.cs → ExportImagePaths
    UNCHANGED
  · both buttons visible at the app's minimum window size (§16 R7)
```

```
PROMPT 5 of 7 — The evidence warning, and the log
Spec: SPEC-2026-007 §05 Q2, §07, §10 (AC-11), §13, §14, §16 R3    Phase: 3    Depends on: Prompt 4 (green)

Read SPEC-2026-007 §13 and §14 before starting.

§05 Q2 was answered (b) — Franz corrected the review page's (a) in the terminal on
2026-09-20 after I flagged it. The answer is: allow email everywhere, but the FIRST send
from a committed group on an evidence profile shows a one-time warning saying the copy
leaves the folder whose checksums and originals\ archive are its integrity. Dismissed with
"don't show again", stored as Email.EvidenceWarningSeen. Every send is logged regardless.

Do only this:
1. Failing tests FIRST in tests/FgScanner.App.Tests/EmailCommandTests.cs: the warning
   appears once for a committed group on an evidence profile; not again after dismissal;
   never for a non-evidence group; never for an uncommitted one.
2. Implement the warning and its setting.
3. Add the log lines from §14: per send, the surface, page count, attachment format and
   route, at Information; the unavailable routes and the fallback at Warning. Never log a
   recipient — the app does not know one and should not start recording who case material
   went to.

Do NOT in this prompt: block any send, or add a second confirmation.

Stop when the checkpoint below is satisfied and report the results.

CHECKPOINT 5 — paste back:
  · dotnet test -c Release → count and "passed"
  · the four warning cases, failing first
  · one real log line from a send, pasted verbatim
  · confirm no recipient or address appears anywhere in the log output
```

```
PROMPT 6 of 7 — Code review and security review  [START A NEW SESSION FOR THIS]
Spec: SPEC-2026-007 §13, §16    Depends on: Prompt 5 (green)

Run /code-review max over the changes made in Prompts 1..5, then /security-review over
the same changes.

Then check compliance with the spec: does the code do what SPEC-2026-007 §10 says, and
only that? Report any drift.

The security review must cover §13 specifically: the two new P/Invoke surfaces
(MAPISendMailW, IDataTransferManagerInterop) and that they are called with app-controlled
arguments only; that no credential is introduced anywhere; that attachment paths come from
the database and the session and never from user text; that the temp folder is app-created;
and that the TFM change brought in no new package.

Also confirm §16 R5: no NAPS2.Lib reference was added, and the licensing guard list in
CLAUDE.md and Directory.Packages.props is unchanged.

Resolve every correctness finding. For anything you decline to change, write one line in
the spec's §22 saying what and why.

CHECKPOINT 6 — paste back:
  · both findings lists with each one's resolution
  · grep -rn "NAPS2.Lib" src/ *.props → no hits
  · dotnet test -c Release → count and "passed"
  · dotnet format --verify-no-changes → clean
```

```
PROMPT 7 of 7 — The amendment, documentation and memory
Spec: SPEC-2026-007 §18    Depends on: Prompt 6 (green)

This one carries an amendment to an approved spec. Do it properly, in writing.

Do only this:
1. docs/specs/SPEC-2026-003-scan-viewer-delete-and-quick-scan-tools.md — amend the scope
   line at :92 ("No rotate, crop, adjust, OCR, PDF, email or print on the evidence Scan
   page"). Do not delete it: strike the word email, add a dated note saying Franz approved
   email on that page on 2026-09-20 (SPEC-2026-007 §05 Q1a) and pointing here.
2. docs/superpowers/plans/2026-08-30-quick-scan-program.md — Phase 10 keeps its route
   decision but its implementation is superseded; point it here.
3. Write docs/adr/0012-email-route-and-evidence-policy.md: the route order and why MAPI is
   not first, and the evidence policy from §05 Q2b with its reasoning.
4. CLAUDE.md — a line in the evidence section: pages can leave the station by email, what
   is logged, and the one-time warning on committed evidence groups.
5. docs/user-guide.md — how to email a session and selected pages, and what each fallback
   message means.
6. docs/manual-tests.md — the Prompt 1 spike checklist and four manual sends (session as
   PDF; three selected pages as images; new Outlook receives the attachments; the forced
   fallback). Leave unticked until performed.
7. Update the memory note fg-scanner-quick-scan-plan, which currently says email is decided
   but unbuilt.
8. Tick §21 in the spec and fill §22's Built row.

Do NOT in this prompt: change any source file.

CHECKPOINT 7 — paste back:
  · git diff --stat → docs and memory only, no src/
  · the amended SPEC-2026-003 line, pasted
  · the ADR filename
  · dotnet test -c Release → unchanged count, still green
```
