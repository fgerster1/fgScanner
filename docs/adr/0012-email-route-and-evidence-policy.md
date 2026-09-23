# ADR-0012 — Email opens a message in the operator's own client, by the first route that works, and never sends

**Status:** accepted, 2026-09-22 (SPEC-2026-007; route order amended the same day)

## Context

Until SPEC-2026-007, nothing in FG Scanner moved case material out of the app. Sending a scan
meant exporting it, finding it in Explorer and attaching it by hand. Franz asked for an Email
button on the Scan page and on a group.

Two questions came with it, and only one of them is technical.

**How does a desktop app hand files to "the operator's mail"?** There is no single answer on
Windows. `mailto:` cannot carry attachments (RFC 6068). Simple MAPI works only where a MAPI
client is registered — classic Outlook, Thunderbird — and new Outlook does not implement it;
spike 3 (2026-09-12) found this station has no MAPI client at all. Windows' Share sheet carries
real files, and new Outlook is a share target, but it needs the Windows SDK target framework,
which this solution had never used. SPEC-2026-003 §05 Q4 (2026-09-13) settled the order as
Share sheet, then MAPI where a client is detected, then Explorer.

**Should case material be able to leave the capture station this way at all?** A page sent by
email leaves the group folder whose `index.json` checksums and `originals\` archive are its
evidentiary integrity (ADR-0003), and lands in a mail store nothing here can account for. That is
not something a button should decide by existing.

## Decision

### The routes

`IShareService` (Core) has one method, **Open**, and deliberately no way to send. Every route ends
with the operator looking at a message, or a Share sheet, and pressing Send themselves in their own
client, under their own identity. There is no SMTP, no stored credential, no background send, and
a test fails if the interface grows a method named Send, Transmit or Deliver.

`WindowsShareService` (App) tries, in order:

1. **Simple MAPI** (`MAPISendMailW` with `MAPI_DIALOG`) — only when the registry probe finds a
   MAPI client: `HKLM\SOFTWARE\Clients\Mail` has a default, that client's `DLLPathEx`/`DLLPath`
   exists, and `Windows Messaging Subsystem` has a `MAPI` value. The probe reads the registry;
   MAPI is never called to find out. The draft is modal, so when the call returns it has already
   been sent or thrown away — and a thrown-away draft is reported as such and ends the send.
2. **The Windows Share sheet**, through the SDK projection's own `DataTransferManagerInterop`,
   parented to the main window and run on the UI thread.
3. **Explorer with the file selected**, and a sentence saying where the file is and that it stays
   there until FG Scanner closes.

**Why MAPI is first, when §08 put it second.** The order decided on 2026-09-13 assumed the Share
sheet might be unavailable and MAPI would catch what it missed. Once the Share sheet worked, it
opened on every Windows 10 and 11 machine, so MAPI was unreachable — and classic Outlook is not a
share target, so a classic-Outlook station got a sheet with no Outlook in it and no route to a
real draft. Franz moved MAPI first on 2026-09-22, gated on the probe. A station with no MAPI
client — new Outlook, and this station — never calls MAPI and still gets the Share sheet first, so
for it nothing changed.

### Webmail (added 2026-09-22)

Franz sends from Gmail and Jim from Yahoo, both in a browser. **No Windows mechanism can hand a
file to a webmail service**: it is not a MAPI client, it is not a share target, and `mailto:`
cannot carry an attachment. As built, the Share sheet opened on both stations with neither
service in it, and closing it left the operator with no idea where the file was.

So the station says how it sends mail — **Settings → "Send email with"** (`Email.SendWith`): a
mail program on this PC (the three routes above, and the default), Gmail in the browser, or
Yahoo Mail in the browser. It is chosen, not detected, because nothing on Windows records where
someone reads their mail. For the webmail choices a send opens the service's compose page with the
subject filled in, and Explorer beside it with the file selected, and the operator drags the file
in. That drag is the whole of what cannot be automated; everything around it is.

Explorer opens **before** the compose page, because the window opened last takes the foreground:
the other way round Explorer covered the message and the send looked as though nothing had
happened (Franz, first real send, 2026-09-22). That send also went to the wrong Google account —
the bare link opens whichever the browser holds first — so Settings takes an optional
`Email.WebmailAccount`, an address or its browser number, which goes into the compose link.

The mail-app routes are never tried on a webmail station: the Share sheet would always "work" and
never contain the service. Neither compose link is an official API — Gmail's
(`mail.google.com/mail/?view=cm&fs=1&su=`) is long-standing and widely used; Yahoo documents none,
and `compose.mail.yahoo.com/?subject=` is the form in common use. If either stops honouring the
subject the operator still gets a blank message. The subject now also reaches the browser's
address bar and history — the operator's own browser, but a place it did not reach before.

**Why not the Share sheet only.** It cannot report what happened: it returns as soon as it is
shown, and the operator may close it without choosing anything. The status line says so —
*"The Windows Share sheet is open — choose your mail app there"* — rather than claiming a message
was opened.

### What is attached

- **A PDF** is built by the Export PDF button's own exporter, so an emailed PDF is the artefact an
  exported one would have been.
- **Images are the page files themselves, copied byte for byte**, keeping their extension. Each
  attachment's checksum is the one `index.json` holds, so a recipient can check it against the
  record. The image exporter was the first design; its default re-encoded the scanner's JPEGs as
  PNG at two and a half to four times the size of the PDF of the same pages (Franz's decision,
  2026-09-22, amending SPEC-2026-007 §07).
- **Never** `index.json`, `manifest.json` or any other index file (§05 N1): the importer reads
  the folder, and a manifest in an inbox invites someone to treat the email as the record.
- Attachments are written under `%TEMP%\FGScanner\email\<guid>`, never into the group folder.
  Every folder there is removed at startup and at exit. That is safe only because FG Scanner is
  single-instance and holds its mutex before the sweep runs.

### The evidence policy (§05 Q2b)

**Sending is allowed, and the operator is told once what it means.** The first send from a
committed evidence group shows a panel inside the attachment dialog: what goes is a copy, it
leaves the folder whose index, checksums and `originals\` archive make it evidence, and the
folder itself is not changed. "I understand — don't show this again" dismisses it for good
(`Email.EvidenceWarningSeen`), and the tick counts even if that send is cancelled.

It is a warning and not a gate, on purpose. A second confirmation on every send is a dialog
nobody reads; refusing outright would push the operator back to exporting and attaching by hand,
which leaves the same copy in the same mail store with no log line at all.

**An evidence record is recognised by its fields, never by its profile's name**: committed, and
carrying the contract's required fields (`DocNo`, `Box`, read off `EvidenceProfile`). The importer
reads field names; matching the name "Evidence" missed a hand-built "JimsStuff Evidence", a
renamed profile and an imported copy.

**Every send is logged** — surface, page count, format and route — and **never a recipient or the
subject**. The app does not know who a message goes to, and it must not start recording who case
material was sent to as a side effect of logging that it was sent. The subject is free text, and
naming the recipient in it is an ordinary thing to type. **No exception object reaches the log on
this path**, only its type name: an IOException names the file it could not delete, a failed
browser launch quotes the whole compose URL, and Serilog's file template ends with `{Exception}` —
so a locked attachment, the ordinary case at exit, wrote the subject (and any address in it) to
disk for 14 days. The security review of 2026-09-22 found it; the operator still gets the full
reason on screen.

## Consequences

- Case material can now leave the station. What leaves is a copy, the folder is untouched, and
  the log records that it happened; nothing records where it went.
- The App targets `net10.0-windows10.0.26100.0` for the WinRT projection. The installer grew from
  94.8 MB to 104.1 MB. `SupportedOSPlatformVersion` pins the analyzer to the installer's
  Windows 10 1607 floor, so a newer API cannot compile clean by accident.
- The route a station gets depends on what is installed there, so a manual send has to be
  repeated on each kind of station: new Outlook (Share sheet), classic Outlook (MAPI), and none
  (Explorer). The dev station has no MAPI client; the MAPI path has not been seen on hardware.
- When Quick Scan is built, its Phase 10 uses `EmailSender` rather than writing its own.
