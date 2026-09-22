# SPEC-2026-007 — Email scanned pages from the Scan and Groups pages

| | |
|---|---|
| **Status** | Approved |
| **Revision** | D — amended 2026-09-22 after the code review, then for webmail; see the notes marked **Amended** |
| **Tier** | Feature |
| **Author** | Claude, for Franz Gerster |
| **Date** | 2026-09-20 |
| **Project** | FgmakerScanner |
| **Supersedes** | — |

---

## 01 · Management summary

- **What we are building** — an Email button in two places: on the Scan page, sending what
  was scanned in the current session, and on the Groups page, sending the pages the
  operator has selected in a group.
- **Why** — reported after a working session. Today the only way to send a scan is to
  export it to a folder, find it in Explorer, and attach it by hand.
- **What it costs** — 7 prompts across 3 phases, two to four days — but the first phase is
  a spike that may come back negative, and the honest estimate depends on it. No
  recurring cost; no mail account, no server, no credentials.
- **What could go wrong** — two things. First, the technical route: Windows' Share sheet
  needs a target-framework change that has never been tried in this solution, and the
  licence rules require the published app to stay non-single-file. If that change breaks
  the build or the installer, the feature falls back to a worse route. Second, and more
  important, **this puts case material into a mail client**. A scan that leaves the
  station is outside the folder whose integrity the evidence pipeline depends on.
- **What we need from you** — five decisions in §05, including one that is not technical
  at all: whether emailing evidence directly from the capture station is something you
  want to be possible.

## 02 · Outcomes

- **Goal** — the operator can send what they just scanned, or a few chosen pages from a
  group, without leaving the app and without hunting through folders.
- **Who benefits** — Franz, sending a document to a lawyer or to Jim; Jim, sending a page
  that needs a second opinion.
- **How we will know it worked** — from a standing start, sending three selected pages
  takes one button and one Send, with no Explorer window and no manual attaching. The
  operator always sees the message before it goes.

## 03 · Scope and non-goals

**In scope**

- An Email action on the Scan page covering the current session's pages.
- An Email action on the Groups page covering the selected pages, or the whole group when
  nothing is selected.
- The route order already decided on 2026-09-13: Windows Share sheet first; Simple MAPI
  only where a MAPI client is genuinely present; otherwise open the containing folder with
  the file selected and say so plainly.
  > **Amended 2026-09-22 (Franz):** MAPI comes **first** when the probe finds a client — see §08.
- **A one-time warning** before the first send from a committed group on an evidence
  profile, saying the copy leaves the folder whose checksums are its integrity (§05 Q2b).
  Dismissed with "don't show again", stored in `Settings`; every send is logged either way.
- Attachments produced by the existing export services — a PDF, or one or more images.
  > **Amended 2026-09-22 (Franz):** images are the page files copied byte for byte — see §07.
- A spike, before any of it, proving the target-framework change is safe.

**Non-goals**

- **Never sends silently.** Every route ends with the operator looking at a message and
  pressing Send themselves. No SMTP, no stored mail credentials, no background sending.
- No address book, no recipient memory, no message templates.
- No `mailto:` route — it cannot carry attachments by design (RFC 6068), so it would
  produce an empty message and look like a bug.
- No copying of NAPS2's email code: it lives in NAPS2.Lib, which is GPL (CLAUDE.md).
  Re-implemented from Windows APIs.
- No change to the evidence export contract, and no email of `index.json`/`manifest.json`
  unless §05 Q4 says otherwise.
- No Quick Scan page — that programme is separate and still unstarted.

## 04 · Current state

**The route was decided and nothing was built.** SPEC-2026-003 §05 Q4, answered
2026-09-13 with verdict *approve*
(`docs/specs/SPEC-2026-003-scan-viewer-delete-and-quick-scan-tools.md:151-192`):

> (a) **Windows Share sheet first; MAPI when classic Outlook is present; otherwise open
> the folder with the file selected.** *(recommended)*

MAPI-first and no-email were both explicitly rejected. The owning phase is Quick Scan
Phase 10 (`docs/superpowers/plans/2026-08-30-quick-scan-program.md:853-889`), rewritten
2026-09-14 after spike 3, which found (`docs/superpowers/research/2026-08-30-quick-scan-spikes.md:62-67`):

> **Verdict: not viable on this machine.** No MAPI-capable mail client is installed or
> registered. The only mail app is **new Outlook**, which does not implement Simple MAPI.

The probe rule is written down (`:117-119`): offer MAPI only when
`HKLM\SOFTWARE\Clients\Mail` has a non-empty default, that client's `DLLPathEx`/`DLLPath`
points at a file that exists, and `Windows Messaging Subsystem` has a non-empty `MAPI`
value. Probe, never invoke.

**Implementation status is zero.** A grep of `src/` for `email|mailto|mapi|DataTransferManager`
returns nothing but false positives. The target framework is still plain
`net10.0-windows` (`src/FgScanner.App/FgScanner.App.csproj:4`), so the WinRT projection
the Share sheet needs is not available yet — that change is the Phase-10 gate, and the
plan says to stop and report if it breaks the build, the tests, the non-single-file
publish or the installer (`plan:880-888`).

**The scope conflict.** That decision scoped email to Quick Scan. SPEC-2026-003
explicitly excludes it from the evidence Scan page (`:92`): "No rotate, crop, adjust,
OCR, PDF, email or print on the evidence Scan page." This request contradicts that line
directly, which is §05 Q1.

**What already exists to build on.**

| Piece | Where |
|---|---|
| The session's pages | `ScanSessionService.Session.Pages` / `ScanViewModel.Pages` (`ScanViewModel.cs:82`) |
| The selected rows | `GroupDetailViewModel.SelectedRows` (`GroupDetailViewModel.cs:78`), mirrored from the grid at `GroupsView.xaml.cs:195-211` |
| Selection→paths | `ExportImagePaths` (`GroupDetailViewModel.Editing.cs:281-285`) |
| PDF attachment | `PdfExportService.ExportAsync` (`src/FgScanner.Scanning/Export/PdfExportService.cs:63`) |
| Image attachments | `ImageExportService.ExportAsync` (`.../ImageExportService.cs:47`) |
| Route 3, already written | `Process.Start` + `explorer.exe /select,` (`GroupDetailViewModel.cs:429-434`) |
| Shell P/Invoke precedent | `SHFileOperationW` in `IStagedPageDiscarder.cs:63` |
| Shell-open precedent | the separator-sheet PDF (`ScanViewModel.cs:660-661`) |

**The sharp edge.** `ExportImagePaths` treats a selection of **one** row as "export the
whole group" (`Editing.cs:281-285`), and `DeleteSelectedCommand` is single-row despite its
name (`GroupDetailViewModel.cs:786-800`). Neither is a safe model to copy blindly. There
are also **no view-model tests** for the export commands or the selection rule — a grep
for `ExportImagePaths|ExportPdfCommand` in `tests/` returns nothing.

**Not examined** — new Outlook's behaviour as a share target (it is listed as an open
manual check in the spike notes, never run); the Windows App SDK as an alternative to the
WinRT projection.

## 05 · Questions for Franz

> **Answered 2026-09-20, Round A part 3** — [review page](https://claude.ai/artifact/XkzFVnPicnfjNrc4C1ZwPV),
> db doc `review/SPEC-2026-007-rA-p3`, verdict **approve**.
>
> | | |
> |---|---|
> | Q1 | (a) **Email on both pages.** SPEC-2026-003's scope line barring email from the evidence Scan page is amended — see §18. |
> | Q2 | (b) **Allow it, with a one-time warning on a committed group on an evidence profile.** The page answer read (a); Franz corrected it to (b) in the terminal the same day, after I flagged that it was the only answer diverging from the recommendation and that every other item on all three pages had also come back (a). The correction is the answer. |
> | Q3 | (a) **A small dialog that remembers the last choice** (PDF or images). |
> | N1 | (a) Index files are never attached. |
> | N2 | (a) Email honours a single selected row; the export commands keep their existing rule. |
> | N3 | (a) If the framework spike fails, stop and report — do not ship the Explorer-only fallback. |

**Blocking**

1. **Do you want email on the evidence Scan page at all?** — *why it matters:* SPEC-2026-003
   §Scope bars it, deliberately, on the page that captures legal evidence. Saying yes
   amends an approved spec, and this one will record the amendment. Saying no still leaves
   the Groups-page half of your request, which covers "send these pages" and is where a
   considered send usually happens.
2. **Emailing case material off the capture station — is that something you want
   possible?** — *why it matters:* this is not a technical question. A page sent by email
   leaves the group folder whose checksums and `originals\` archive are its evidentiary
   integrity (ADR-0003), and lands in a mail store nobody controls. I am not raising it to
   block the feature — you may well want exactly this — but it should be a decision on the
   record rather than a side effect of a button. Options: allow it anywhere; allow it but
   warn once for a committed evidence group; or restrict it to groups that are not on an
   evidence profile.
3. **What gets attached?** — *why it matters:* a single PDF is one clean attachment and
   the usual thing to send; individual images are what someone wants when they will edit
   or re-file them. Asking every time is a dialog on a path meant to be quick. My
   recommendation is a small dialog that remembers the last choice, matching the existing
   export dialogs.

**Non-blocking**

1. **Should a committed group's index files be attached alongside?** — *proceeding as
   if:* no. The importer reads the folder, not an email, and attaching `manifest.json`
   invites someone to treat the email as the record.
2. **Should one selected row mean "that row", or keep today's "whole group"?** —
   *proceeding as if:* one selected row means that row, for email only. The existing
   export commands keep their current rule so nothing silently changes underneath them —
   but this means email and export behave differently on the same selection, which is
   worth your eye.

## 06 · Assumptions

| # | Assumption | If wrong |
|---|---|---|
| 1 | The TFM change to `net10.0-windows10.0.x` leaves the build, tests, non-single-file publish and installer intact | Phase 1 stops and reports; the feature falls back to MAPI-probe-then-Explorer, which on this station means Explorer every time |
| 2 | New Outlook accepts file attachments through the Share sheet | The Share sheet route produces an empty message; the spike's manual check catches it before any code is written |
| 3 | Attachments can be written to a temporary folder and handed to the Share sheet without the mail client needing them to persist | The attachment must live somewhere durable, and cleanup becomes a question |
| 4 | Nothing in the evidence pipeline depends on pages *not* leaving the station | §05 Q2 is a formality rather than a real constraint |

## 07 · Data model

**No schema change, no migration.** Nothing about email is persisted except, if §05 Q3's
recommendation is taken, one `Settings` key for the remembered attachment format:

| Key | Values | Default |
|---|---|---|
| `Email.Attachment` | `Pdf` \| `Images` | `Pdf` |
| `Email.EvidenceWarningSeen` | `true` \| `false` | `false` |
| `Email.SendWith` *(added 2026-09-22)* | `MailApp` \| `Gmail` \| `Yahoo` | `MailApp` |

Written through `AppSettingsService` like every other setting, and read fresh per send.
Documented as a doc comment where it is read.

Attachments themselves are temporary files, not records: written under the system temp
path in a per-send folder, and not registered in the database.

> **Amended 2026-09-22 (Franz), after code review finding #8.** "Separate images" attaches **the
> page files themselves, copied byte for byte**, each keeping its own extension
> (`<subject>_001.jpg`…), rather than running them through `ImageExportService`. That exporter's
> default re-encoded the scanner's JPEGs as PNG — on real station scans the median page went from
> 1.04 MB to 3.38 MB, against 1.02 MB inside a PDF — and the size warning then advised switching
> to images. A byte-for-byte copy is also the better thing to send from an evidence record: its
> checksum is the one `index.json` holds. The PDF still comes from `PdfExportService`, as §08 says.
>
> The temp folders are removed **all of them, at startup as well as exit** (finding #10): a crash
> never reached exit, and the per-session list forgot any folder whose delete failed. Safe because
> the app is single-instance.

## 08 · Architecture and approach

**A service in Core behind an interface, routes in App.** `IShareService` with one
method — "hand these files to the operator's mail path and tell me which route was used"
— implemented in the App layer where the Windows dependencies live. The view models take
the interface, so both call sites are testable without Windows' share UI, which is the
same shape `IScanService` uses for hardware (CLAUDE.md: hardware access only through the
interface; the same reasoning applies to shell UI).

**Route order, exactly as decided:**

> **Amended 2026-09-22 (Franz), after code review finding #11.** The order is now **MAPI first when
> the probe finds a client, then the Share sheet, then Explorer.** With the Share sheet working it
> opened on every Windows 10 and 11 machine, so route 2 was unreachable — and classic Outlook is
> not a share target, so a classic-Outlook station got a sheet without Outlook in it. A station
> with no MAPI client (new Outlook; this station) is unaffected. Route 1 is also no longer the
> hand-declared `IDataTransferManagerInterop`, which could never work (finding #1): it uses the SDK
> projection's `DataTransferManagerInterop`, parented to the main window on the UI thread
> (finding #2). ADR-0012 records the whole decision.

> **Amended 2026-09-22 (Franz): webmail.** Franz sends from Gmail and Jim from Yahoo, both in a
> browser — and a browser page is neither a MAPI client nor a share target, so as built the Share
> sheet opened with no Gmail in it and closing it stranded the operator. Settings gains **"Send
> email with"** (`Email.SendWith`): *a mail program on this PC* (the routes above, and the
> default), *Gmail in the browser* or *Yahoo Mail in the browser*. For webmail a send opens the
> service's compose page with the subject filled in, and Explorer beside it with the file
> selected, to be dragged in — the one step no desktop app can take for a webmail service. The
> mail-app routes are never tried on a webmail station. Neither compose link is an official API;
> Yahoo documents none at all, so its link is checked on Jim's station before this is Done.
> Whichever the station, the Share sheet's status now also says where the file is.

1. **Windows Share sheet** — `DataTransferManager` via `IDataTransferManagerInterop.GetForWindow`,
   which is supported for unpackaged WPF. Real file attachments, and new Outlook is a
   share target.
2. **Simple MAPI** (`MAPISendMailW`) — offered only when the three-part registry probe
   passes. Probe, never invoke to find out.
3. **Explorer with the file selected** — already written
   (`GroupDetailViewModel.cs:429-434`), with a plain sentence: the scan is saved here,
   attach it yourself. Never a raw error code.

**The spike comes first and can veto the design.** Phase 1 changes the target framework
and proves four things: the solution builds, the tests pass, the win-x64 publish is still
non-single-file and non-trimmed (the LGPL separation in CLAUDE.md), and the Inno Setup
installer still produces a working install. If any fails, the work stops and reports
rather than quietly substituting route 2 — the plan is explicit about that
(`plan:880-888`).

**Attachment production reuses the export services.** `PdfExportService` for a PDF,
`ImageExportService` for images — the same code the export buttons use, so an emailed PDF
is the same artefact as an exported one. Temp folder per send, cleaned on app exit.

**Alternatives considered.** *`mailto:`* — cannot carry attachments (RFC 6068), rejected
in the spike notes. *Writing an `.eml` and shell-opening it* — considered and ranked
below the Share sheet in the spike (`:100-115`): new Outlook's `.eml` handling is
partial and may open a read view rather than a draft. *Classic Outlook COM automation* —
rejected, classic Outlook is absent. *Copying NAPS2's implementation* — forbidden, GPL.

**Data access.** None beyond reading the page paths already in memory.

**System evolution.** `IShareService` is app infrastructure. If Quick Scan is built later,
Phase 10 consumes this rather than writing its own — this spec supersedes the
implementation half of that phase, and §18 records it.

## 08a · AI opportunity assessment

| | |
|---|---|
| **What it would do** | Draft the subject and body from the pages' OCR text and index values — "Farm Folder, 3 pages, deed dated 1998-04-02" |
| **Why AI beats deterministic code here** | Only marginally: index values already give a decent subject deterministically. A model would write a nicer sentence |
| **Model and rough cost per operation** | Gemini Flash-class, a fraction of a cent per send |
| **What happens when it is wrong** | A misleading subject line on a message about case material, which the operator may not re-read before sending |
| **Recommendation** | **Not worth it** — compose the subject from the group name and page count, deterministically |

A generated summary of evidence, sitting in a message the recipient may treat as a
description of what is attached, is a bad trade for a nicer sentence. No model is used, so
no learning loop is proposed.

## 09 · Design system compliance

The app's own WPF Fluent theme governs.

- The Groups toolbar gains "Email…" beside "Export PDF… / Export images… / Print… / Copy"
  (`GroupsView.xaml:259-262`), matching their style and ellipsis convention.
- The Scan page gains "Email…" in the button column beside "Separator sheet…"
  (`ScanView.xaml:86-89`), subject to §05 Q1.
- The attachment dialog follows `ExportPdfDialog` / `ExportImagesDialog` in shape and
  wording.
- Accessibility: both buttons keyboard reachable with visible focus; the fallback message
  is on-screen text, not a tooltip; any new shortcut declares its section
  (`ShortcutRouter`, SPEC-2026-003), and email would belong to Scan and Groups separately.

## 10 · Acceptance criteria

> **AC-1** — Given pages in the current scan session, when Email is pressed, a message is
> opened with those pages attached and the operator presses Send themselves.
> *Proven by:* `manual` — Franz, on the station; plus
> `tests/FgScanner.App.Tests/EmailCommandTests.cs` → "the session's pages are handed to
> the share service in order"

> **AC-2** — Given three rows selected in a group, when Email is pressed, exactly those
> three pages are attached, in sequence order.
> *Proven by:* `tests/FgScanner.App.Tests/EmailCommandTests.cs` → "only the selected rows
> are shared"

> **AC-3** — Given one row selected, the behaviour matches §05 N2 and is different from
> the export commands only where that answer says so.
> *Proven by:* same file → "a single selected row emails that row"

> **AC-4** — Given no selection, the whole group is attached.
> *Proven by:* same file → "no selection emails the whole group"

> **AC-5** — Nothing is ever sent without the operator pressing Send in their mail
> client: the service opens a message and returns; it never transmits.
> *Proven by:* same file → "the share service is never asked to send" (the fake records
> the call and asserts no send API exists on the interface)

> **AC-6** — With no share target and no MAPI client, the fallback opens the containing
> folder with the file selected and shows a plain sentence — never an error code.
> *Proven by:* same file → "the fallback explains itself"

> **AC-7** — The MAPI route is offered only when all three registry conditions hold, and
> the probe never invokes MAPI to find out.
> *Proven by:* `tests/FgScanner.App.Tests/MapiProbeTests.cs` → three cases, each missing
> one condition

> **AC-8** — After the target-framework change, the published output is still
> non-single-file and non-trimmed, and the installer produces a working install.
> *Proven by:* `manual` — Phase 1 spike checklist, recorded in the spec

> **AC-9** — Attachments land in a temp folder and are not registered as pages; the group
> folder is unchanged by sending.
> *Proven by:* `tests/FgScanner.App.Tests/EmailCommandTests.cs` → "sending writes nothing
> into the group folder"

> **AC-10** — The evidence export is unchanged: existing Verify snapshots pass untouched.
> *Proven by:* `tests/FgScanner.Core.Tests/IndexExporterTests.cs`

> **AC-11** — The first send from a committed group on an evidence profile shows the
> warning; once dismissed it does not return, and a non-evidence group never shows it.
> *Proven by:* `tests/FgScanner.App.Tests/EmailCommandTests.cs` → "the evidence warning
> is shown once and only for committed evidence groups"

> **AC-12** *(added 2026-09-22)* — On a station set to Gmail or Yahoo Mail, a send opens that
> service's compose page with the subject filled in and escaped, and Explorer with the file
> selected; MAPI and the Share sheet are never tried; if the browser cannot be opened the file is
> still shown and the status says so.
> *Proven by:* `EmailCommandTests.cs` → "Gmail opens a compose page beside the file and never tries
> the mail app routes", "the subject is escaped", "a browser that will not open…", and
> `AiModelAndThemeSettingTests.cs` → the two "send with" cases; `manual` — Gmail on Franz's
> station, Yahoo on Jim's

## 11 · Test strategy

**11.1 — The failing tests to write first**

| Feature | Test file | The failing assertion |
|---|---|---|
| Selection → attachments | `tests/FgScanner.App.Tests/EmailCommandTests.cs` (new) | Three selected rows produce exactly three paths, sequence-ordered |
| Whole group when unselected | same | No selection → every row |
| Session pages | same | Scan page hands over `Pages` ordered by sequence |
| Never sends | same | The fake share service records "opened", never "sent" |
| Fallback wording | same | With every route unavailable, the status text is the sentence, not an exception message |
| MAPI probe | `tests/FgScanner.App.Tests/MapiProbeTests.cs` (new) | Each of the three conditions missing → not offered |
| Group folder untouched | same as first | File count in the group folder is identical before and after |

**11.2 — Test data.** A `FakeShareService` in the App tests, mirroring `FakeScanService`'s
role: it records what it was asked to share and which route was chosen. The MAPI probe
takes its registry readings through a small abstraction so the three cases can be faked —
no test touches the real registry. Existing in-memory fixtures for groups and rows.

**11.3 — Verification suite**

- `dotnet build -c Release`, `dotnet test -c Release` (≥ 692), `dotnet format --verify-no-changes`.
- **Phase 1 spike checklist**, all four recorded in writing before any feature code:
  build green · tests green · `dotnet publish -p:PublishProfile=win-x64` still
  non-single-file and non-trimmed · installer built and installed.
- Manual on the station: send a session as PDF; send three selected pages as images;
  confirm new Outlook receives the attachments; force the fallback (no share target) and
  read the message.

## 12 · Edge cases and failure modes

| Case | Expected behaviour | Where handled |
|---|---|---|
| No pages in the session | Email disabled, with the reason | `CanExecute` |
| Group with zero rows | *"There are no pages to email."* — the Groups button has no `CanExecute`, like the export buttons beside it, because one tied to rows went stale when rows refilled *(amended 2026-09-22, finding #3)* | command |
| A page file missing from disk | Named message identifying the page; nothing sent | attachment build |
| Share sheet dismissed without sending | Nothing sent, no error, temp files cleaned | route 1 result |
| MAPI draft closed without sending | *"You closed the message without sending it — nothing left the app."*; the Share sheet is not tried next *(added 2026-09-22)* | route 2 result |
| Mail client not installed at all | Route 3, plain sentence | AC-6 |
| Very large selection (200 pages) | A warning above a threshold, since mail servers reject large attachments; operator may continue | §15 |
| PDF export fails mid-build | The export error surfaces; nothing is shared | existing export error path |
| Temp folder not writable | Named message; no crash | attachment build — every failure on the send path is caught in `EmailSender` *(finding #4)* |
| Save or Delete pressed mid-build on the Scan page | Held for the length of the send, including the save a batch scan makes directly *(finding #5)* | `ScanViewModel._sending` |
| A background OCR/AI reload mid-selection | The selection survives the reload, so "no selection = whole group" cannot trigger by accident *(finding #6)* | `GroupDetailViewModel.RestoreSelection` |
| Send pressed twice quickly | Second press is ignored while the first is building | `CanExecute` guard |
| Sending during an annotated sheet or a duplex sequence | Allowed — sending copies, it does not move or alter pages | — |

## 13 · Security and configuration

This is the section that matters most in this spec, because it is the first feature that
moves case material **out** of the app.

- **Data exposure.** An emailed page leaves the group folder whose checksums and
  `originals\` archive are its evidentiary integrity (ADR-0003, CLAUDE.md). The copy is
  outside FG Scanner's control from then on. §05 Q2 decides the policy; whatever it is,
  the log records that a send happened and how many pages (§14), so the act is at least
  visible afterwards.
- **No credentials.** No SMTP, no OAuth, no password, no token. The operator's own mail
  client does the sending, under their own identity. Nothing new goes into the credential
  store.
- **Input validation.** Attachment paths come from the database and the session, never
  from user text. The temp folder is created by the app; no user-supplied path is joined.
  Filenames for attachments go through the existing `NamingEngine` sanitisation used by
  the export dialogs.
- **New dependency.** None as a package — the Share sheet uses the WinRT projection that
  comes with the Windows SDK target framework. That TFM change is the one build-affecting
  item and is gated by the Phase 1 spike. The LGPL separation rule (publish stays
  non-single-file, non-trimmed) is part of that gate.
- **P/Invoke surface.** `MAPISendMailW` and `IDataTransferManagerInterop` are added.
  Both are called with app-controlled arguments only, following the existing
  `SHFileOperationW` precedent (`IStagedPageDiscarder.cs:63`).
- **Configuration.** One optional setting (`Email.Attachment`), no environment variable,
  nothing to configure on any host.

## 14 · Observability

- **Logged** at Information, per send: which surface (Scan or Groups), how many pages,
  attachment format, and which of the three routes was used. Never the recipient — the
  app does not know it, and should not start recording who case material was sent to
  without that being a decision of its own.
- **Logged** at Warning: the Share sheet unavailable, the MAPI probe failing, the
  fallback used.
- **Surfaced to the operator**: the status line names the route in plain words — "opened
  in your mail app", or "no mail app found — the scan is in this folder".
  > **Amended 2026-09-22.** "Opened in your mail app" was not true of the Share sheet, and the
  > fallback said "attached" and counted files as pages (finding #9). The wording that shipped is
  > in `docs/user-guide.md` → "Emailing pages". The subject is **not** logged either (finding #12):
  > it is free text, and a recipient named in it would otherwise sit in the 14-day log.
- **A silent failure** would be a Send that quietly attaches nothing. What makes it
  non-silent: the attachment count is logged and shown before the route is invoked, and
  AC-5's fake proves the service never claims to have sent.
- **Who notices** — the operator, when the message opens; and the log, for anything that
  happened before the message appeared.

## 15 · Performance and scale

Sends are a handful of pages, occasionally a whole group. Building a 20-page PDF takes
about a second with the existing exporter; a 223-page group (the largest in the real data)
is the outlier worth guarding.

Attachment size is the real limit, and it is external: most mail servers reject above
about 25 MB, and a 300 DPI colour page is roughly 2 MB. So the practical ceiling is around
a dozen images or a compressed PDF of a few dozen pages. Warn above 20 MB rather than
fail; the operator decides. Revisit if anyone routinely sends whole boxes.

> **Amended 2026-09-22.** The 20 MB is measured on the **message**, not the files: attachments
> travel base64-encoded, a third larger, so the warning now starts at about 15 MB of files.

## 16 · Regression risk

| # | What could break | Why | Evidence (file:line) | Mitigation |
|---|---|---|---|---|
| R1 | The build, publish or installer | The TFM change to `net10.0-windows10.0.x` is untried here, and CLAUDE.md requires a non-single-file, non-trimmed publish for the LGPL separation | `FgScanner.App.csproj:4`; CLAUDE.md licensing guards; `plan:880-888` | Phase 1 is a spike with four written checks and a stop condition |
| R2 | An approved spec is contradicted quietly | SPEC-2026-003 `:92` bars email from the evidence Scan page | that file | §05 Q1; the amendment is recorded in both specs |
| R3 | Evidence leaves the managed folder unnoticed | Sending copies pages outside the group | ADR-0003; CLAUDE.md evidence section | §05 Q2 sets policy; §14 logs every send |
| R4 | Export behaviour changes underneath the existing buttons | The selection rule is shared with `ExportImagePaths`, which has **no tests** | `Editing.cs:281-285`; no hits in `tests/` | Email gets its own path; the export commands are left alone; new tests cover both rules |
| R5 | A GPL contamination claim | NAPS2's own email code is in NAPS2.Lib | CLAUDE.md; `plan:889` | Re-implemented from Windows APIs; no NAPS2.Lib reference is added, and the licensing guard list is unchanged |
| R6 | Temp attachments accumulate | Files are written per send and never registered | §07 | Per-send folder cleaned on app exit; named in §12 |
| R7 | The Scan page's button column grows past its layout | It already holds seven buttons and two sequence controls | `ScanView.xaml:49-99` | Place with the existing buttons and check at the minimum window size |

## 17 · Migration and rollback

- **Forward** — the TFM change ships with the feature in one installer.
- **Backward** — reinstall the previous installer. No data was written, so there is
  nothing to undo. Sent copies are outside the app either way.
- **Point of no return** — none for data. The TFM change is reversible in source but
  affects the whole app, so it is committed only after the spike's four checks pass.
- **Backup** — unchanged.

## 18 · Documentation updates

- **docs/specs/SPEC-2026-003-scan-viewer-delete-and-quick-scan-tools.md** — record the
  amendment to its §Scope line 92, pointing here (§05 Q1's answer).
- **docs/superpowers/plans/2026-08-30-quick-scan-program.md** — Phase 10's implementation
  is superseded by this spec; the phase keeps its route decision and points here.
- **CLAUDE.md** — a line in the evidence section: pages can now leave the station by
  email, what is logged when they do, and whatever §05 Q2 decided.
- **docs/user-guide.md** — how to email a session and selected pages, and what each
  fallback message means.
- **docs/manual-tests.md** — the spike checklist and the four manual sends.
- **docs/adr/0012-email-route-and-evidence-policy.md** — new ADR recording the route order
  (carried from SPEC-003 Q4) and the evidence policy from §05 Q2.
- **Memory** — update `fg-scanner-quick-scan-plan`, which currently says email is decided
  but unbuilt, and record that Phase 10's implementation moved here.

## 19 · Phase plan

### Phase 1 — The spike (may veto the rest)
- **Objective** — prove the Share sheet is reachable without breaking anything.
- **Delivers** — the TFM change on a branch, plus written evidence for the four checks:
  build, tests, non-single-file publish, installer.
- **Not in this phase** — any email code, any UI.
- **Done when** — AC-8 is recorded, or the spike reports failure and the spec returns to
  Franz with the fallback plan.

### Phase 2 — The service and the routes
- **Objective** — a testable share path with all three routes and no UI.
- **Delivers** — `IShareService`, the Share-sheet implementation, the MAPI probe and
  route, the Explorer fallback, `FakeShareService`, and the tests in §11.1.
- **Not in this phase** — buttons, dialogs, attachment format choice.
- **Done when** — AC-5, AC-6, AC-7 pass.

### Phase 3 — The two buttons
- **Objective** — the operator can send from both pages.
- **Delivers** — the Groups toolbar button and selection rule, the Scan page button
  (§05 Q1a), the attachment dialog and its remembered setting, the one-time evidence
  warning (§05 Q2b), the size warning, the log lines.
- **Not in this phase** — Quick Scan, address memory, templates.
- **Done when** — AC-1..AC-4, AC-9, AC-10 pass and the manual sends are recorded.

## 20 · Prompt pack

[SPEC-2026-007-email-scanned-pages-PROMPTS.md](./SPEC-2026-007-email-scanned-pages-PROMPTS.md)
— written once this spec is approved (§22).

## 21 · Definition of done

- [ ] All acceptance criteria met — **AC-2..AC-11 are proven by tests** (AC-8 by the spike);
      **AC-1's manual half is open**: the Share sheet opened with the session's PDF attached on
      2026-09-22 but was dismissed, so no send has yet reached a mail app. See `docs/manual-tests.md`
      → SPEC-2026-007.
- [x] Failing tests written first, now passing — each watched red before its fix, including every
      code-review fix.
- [x] Full suite green (≥ 692) — **849 of 849**, `dotnet test -c Release`, 2026-09-22.
- [x] `/code-review max` run, findings resolved or accepted in writing — 2026-09-22, 15 findings
      and 4 smaller ones, **all fixed** (commits `156e2f8` … `24b334e`). Two review claims were
      corrected on the evidence: #7's "no warning on Jim's station" (all 18 of his groups are on the
      profile named "Evidence"; the fix stands for renamed and imported profiles), and #11's route
      order, which Franz changed rather than kept.
- [ ] Security review run against §13 — **not run as a separate pass.** The code review covered
      part of it: the P/Invoke calls (the interop defect, #1), data exposure (temp copies, #10; the
      subject in the log, #12) and the new TFM (the lost analyzer floor, #15). A dedicated
      security review has not been done.
- [x] Documentation updated per §18, including the SPEC-003 amendment — 2026-09-22.
- [x] Spike checklist recorded (AC-8) — §22 below.
- [ ] Rollback tested or explicitly waived by Franz

## 22 · Sign-off

| | |
|---|---|
| **Review round answered** | ☑ 2026-09-20 — [Round A, part 3 of 3](https://claude.ai/artifact/XkzFVnPicnfjNrc4C1ZwPV) · db doc `review/SPEC-2026-007-rA-p3`, with Q2 corrected to (b) in the terminal |
| **Franz approved** | ☑ 2026-09-20 (verdict `approve`) |
| **Built** | ☑ 2026-09-22 — branch `phase-26-email`, 16 commits from `979d641` (the spike) to `24b334e`, then the documentation; not yet merged to `main`, no release cut |
| **Verified in production** | ☐ date: |

### Phase 1 spike — result: **GO**, 2026-09-21

Branch `phase-26-email` off `main` (`0ec1d8d`). The only change is the target framework:
`net10.0-windows` → **`net10.0-windows10.0.26100.0`** in `FgScanner.App.csproj`, and in
`FgScanner.App.Tests.csproj`, which had to follow — a test project cannot reference a project
with a higher platform version. `10.0.26100.0` is the only Windows Kit on this machine.
`FgScanner.Scanning` did **not** need to change: a `net10.0-windows10.0.x` project can reference
a plain `net10.0-windows` one. `git diff` was two `.csproj` lines and nothing else.

**The projection resolved**, which is what the spike existed to find out:
`Microsoft.Windows.SDK.NET.Ref 10.0.26100.57` restored from NuGet with no feed or SDK
intervention, and `WinRT.Runtime.dll` (1,364 KB) is in the output.
`IDataTransferManagerInterop` is therefore reachable, so route 1 in §08 is open.

| Check | Result |
|---|---|
| a · `dotnet build -c Release` | `0 Warning(s), 0 Error(s)` — warnings are errors in Release |
| b · `dotnet test -c Release` | `total: 793, failed: 0, succeeded: 793` — unchanged |
| c · `dotnet publish -p:PublishProfile=win-x64` | **LGPL separation intact** — see below |
| d · installer built, installed, runs | `fgscanner-0.5.1-win-x64.exe`, exit code 0, app scans |

**c — the separation, in numbers.** Nine NAPS2 assemblies present as separate files
(`NAPS2.Sdk.dll` 1,420 KB, `NAPS2.Images.dll` 332 KB, `NAPS2.Internals.dll` 256 KB,
`NAPS2.Escl.dll` 252 KB, `NAPS2.Wia.dll` 100 KB, `NAPS2.Images.Gdi.dll` 52 KB, plus the three
small binaries packages) alongside the out-of-process `NAPS2.Worker.exe`. 340 files, 329 DLLs,
and `FgScanner.exe` is 0.16 MB — a launcher, not a bundle. Not trimmed:
`PresentationFramework.dll` 15,446 KB and `System.Private.CoreLib.dll` 15,658 KB are full size.

**d — installed, not merely compiled.** The installer grew from **94.8 MB to 104.1 MB**; the
WinRT projection assemblies are the difference, and that is the visible cost of this route.
Installed over the existing 0.5.1 into `C:\Program Files\FGScanner` (`PrivilegesRequired=admin`),
exit code 0, log reads *"Installation process succeeded."* The installed copy was then launched
with `--fake-scanner`: **"1 device(s) found."**, **"Scan complete — 1 page(s)."**, and the
phase-25 two-pass button present and enabled. 344 files in the install directory with the NAPS2
assemblies still separate, so the separation survives packaging as well as publishing.

**Not proven by this spike**, and not claimed: that the Share sheet itself works. The spike shows
the framework change is safe and the projection is reachable; whether `DataTransferManager`
behaves for an unpackaged WPF app with new Outlook as a target is Prompt 2's problem. The
installer's file-association, StillImage and AutoPlay registrations were written by this install
but not exercised.

**Version note.** This installed build carries version 0.5.1, the same number as what it replaced,
and it includes the whole of phase 25. It is not the 0.5.2 release; `<Version>` was not bumped.
