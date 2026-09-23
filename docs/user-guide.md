# FG Scanner — User Guide

## The workflow in one paragraph

Create or open a **Group** (a folder on disk = a group), scan pages into it,
fill in your index fields in the grid (before or after scanning — "values for
the next scan" pre-fill incoming pages), then **Review & Commit**. Committing
validates required fields and writes `index.csv` (and, if enabled in the
profile, `.xlsx` / `.xml` / `.json`) plus `manifest.json` into the group
folder. Everything else — OCR, AI descriptions, PDF export — feeds that index.

## Scanning

- Pick a driver (WIA is the default; TWAIN for older drivers; eSCL for network
  scanners), a device, source (flatbed/feeder/duplex), DPI, bit depth, page size.
- **Batch scan…** runs several passes with a prompt or delay between them and
  saves to the active group at the end.
- If the app is killed mid-scan, the next start offers to recover the pages.
- **Check a page before saving:** double-click a thumbnail, or select it and press Enter, to open it
  full size. The arrow keys page through the other scans. When you close the viewer, the page you
  were on stays selected.
- **Delete a failed scan before saving:**
  - Select one or more thumbnails (Ctrl/Shift+click, or Ctrl+A for all).
  - Press Delete or **Delete selected…**, then confirm.
  - The files go to the **Windows Recycle Bin**, and the page labels renumber.
  - Delete is unavailable while a scan or a save is running.

  Pages already saved to a group are deleted from Groups instead, into FG Scanner's Trash.
- **Restoring a deleted scan:** the Recycle Bin puts the file back in FG Scanner's hidden scan-session
  folder (`%APPDATA%\FGScanner\recovery\…`). That folder is cleared after Save to group, so copy the
  restored file somewhere else straight away — or simply scan the sheet again.

## Both sides of the paper

**Which option to use.** The **Source** list offers three:

| Source | Use it when |
|---|---|
| **Flatbed** | One sheet at a time on the glass. |
| **Feeder (one side)** | A stack, fronts only — or a stack whose backs you will take in a second pass. |
| **Feeder (both sides, one pass)** | Your scanner captures both sides itself. One pass, done. |

If your scanner cannot do one of these, it stays in the list but greyed out, with the reason
written underneath the box. A greyed-out **Feeder (both sides, one pass)** is what the two-pass
flow below is for.

**"Turn the backs the right way up"** is for a one-pass duplex scanner that hands the reverse
sides back upside down. It is off unless you turn it on, and it only applies to that source.

### Two passes, for a scanner that scans one side

Press **Both sides (two passes)** with the source set to **Feeder (one side)**.

1. Put the stack in the feeder, fronts up, and press the button. FG Scanner scans the fronts.
2. It then says, for example: *"6 front(s) scanned. Turn the whole stack over, put it back in the
   feeder, and press "Scan the backs"."* The button itself changes to **Scan the backs**.
3. Turn the **whole stack** over as one block — do not reverse it, do not flip sheet by sheet —
   put it back, and press **Scan the backs**.
4. The pages are put into sheet order for you: front 1, back 1, front 2, back 2, and so on. The
   status line confirms it: *"Both sides scanned — 6 front(s) and 6 back(s), paired into 12
   page(s) in sheet order."*

Save to a group as usual. The order you see is the order that is saved.

**"The backs come off the stack in reverse order"** is ticked, because turning a whole stack over
end-for-end reverses it — the last sheet comes off the feeder first. Untick it only if you flip
the sheets one at a time, keeping the original order. Getting this wrong pairs every sheet with
the wrong back while the page count still looks right, which is why FG Scanner asks instead of
guessing.

**While a stack is half captured**, the prompt and a **Cancel stack** button stay on screen, and
the ordinary **Scan** and **Batch scan…** buttons are unavailable. That is deliberate: a page
scanned in the middle of a stack is not part of it, and would throw the pairing out.

**Cancel stack** abandons both passes and moves their pages to the Recycle Bin. Use it if the
stack jams or you lose your place. A front with no back is not half a record — saved, it becomes a
whole one-sided document and is read as one.

### If the counts disagree

If the two passes do not produce the same number of pages, **nothing is paired**:

> The two passes do not match: 10 front(s) and 9 back(s). Nothing has been paired. Scan the backs
> again, or save the pages as they are and put them in order in Groups.

Your pages are all still there, in the order they were scanned — fronts first, then backs. You
have two ways on:

- **Scan the backs again.** Press **Cancel stack**, then start over. This is usually right: a
  missing back normally means a double feed, and you want to know which sheet lost it.
- **Save them as they are** and fix the order in Groups with **Interleave**.

FG Scanner will not guess a pairing. A stack that is obviously a mess gets rescanned; a stack that
was paired wrongly looks perfectly normal until somebody reads it out in a deposition.

**A stack of five sheets where the last one is single-sided** counts as a disagreement — five
fronts, four backs — and is refused the same way.

### Blank backs

Every back is kept, including the blank ones. Ten double-sided sheets give **twenty** pages, not
eleven, even though every blank back is an identical image and even if your profile is set to drop
blank pages. That the back of a page is blank is part of the record.

### If the app closes mid-stack

The pairing is not saved until you save to a group. If FG Scanner is closed or crashes between
pairing and saving, the recovered pages come back **in the order they were scanned**, not in sheet
order, and the Scan page says so. Pair them again, or put them in order in Groups.

## Profiles and index fields (Settings)

A profile holds up to 16 typed fields — Text, Date (ISO), Number, List — each
optionally Required (blocks commit), Sticky (carries to the next page), or
defaulted (tokens: `$(today)`, `$(group)`, `$(counter)`, `$(user)`). Saving
field changes creates a new schema version; existing groups keep theirs.

**Settings apply when you save them — you do not need to restart.** A profile
you create, rename, delete or import is in the Groups list straight away, and a
base folder you set is used by the next group you create. If you change a
profile's fields while a group is open, that group keeps its own layout and
shows a banner offering **"Use latest field layout"**; values you have typed for
the next scan survive, except for a field that no longer exists — the status
line says how many were dropped. A change made while a scan is running, or while
a sheet with notes is part-captured, waits until the scan finishes or the sheet
is completed or abandoned, so a half-captured sheet is never disturbed.

**Appearance.** Settings → Appearance picks the theme — system, light or dark —
and it applies when you save.

**Trash.** "Keep deleted pages for N days" shows what is actually stored. Change
it and save, and the purge runs there and then, reporting how many items it
removed.

**Length and memo (Text fields).** A Text field can carry a **Length** — 1 to
100 characters. Choose **Memo** in the **Type** list for a long field: it opens
at three lines instead of one and raises the limit to 2000. Leave Length blank
for no limit. Every text box wraps and grows with what you type, memo or not,
so a long value is readable either way; Memo decides how much may be typed and
how tall the box starts. Switching a Memo back to Text clears a length plain
text cannot hold — 2000 does not fit in 100. Both settings
are about typing and checking values on screen: neither is written to the index
files, and shortening a length never shortens values already stored — they show
as invalid until someone corrects them. Typing stops at the limit, and a paste
that would go over is refused whole, with a message, rather than being silently
cut short.
Profiles also choose the export formats and the CSV delimiter, and can be
shared as `.fgprofile` files (Export/Import buttons).

## Editing pages (Groups section)

Select rows in the grid (Ctrl/Shift for several) and use the toolbar:
rotate ⟲/⟳/flip/custom angle, Deskew, Adjust… (brightness, contrast, hue,
saturation, sharpen, black & white, crop), Split, Combine, reorder
(▲ ▼ / Reverse / Interleave for manual duplex), Undo/Redo (Ctrl+Z / Ctrl+Y).
Deleting a page moves it to **Trash**, restorable for 30 days (configurable).

**Record editor.** "Record editor…" on the toolbar opens the selected page in a window of its own:
its fields on the left, the page on the right, and the rest of the group in a list below. Drag the
dividers to give a pane more room — that is the width control for the fields, and the boxes reflow
to match; the sizes come back the next time you open that group. Text boxes wrap and grow as you
type, to twenty lines, then scroll. Ctrl+PageDown and Ctrl+PageUp move to the next
and previous page, and the fields, the image and the list stay on the same page. The toolbar also
has Undo, Redo, Add missed page…, Import PDF/images… and Delete page (to Trash). What you type here
goes exactly where the grid's cells go — it is the same record, in a bigger window. Closing it
leaves the Groups list on the page you were editing.

**Preview and page viewer.** Fit shows the whole page — in the preview beside the grid and in the
full-size viewer (double-click the preview). 100% is the page at its actual paper size. The page
keeps fitting as you resize the panel until you zoom with + or −; press Fit to go back. On a small
screen every section and dialog scrolls, and dialog buttons always stay visible.

## Emailing pages

**FG Scanner never sends anything itself.** It puts the pages into a message in your own mail
app, and you press Send there, from your own account. Nothing is sent until you do.

**From the Scan page.** **Email…** sends every page on the page, in the order they are shown. The
button turns on once there is a page to send. While a message is being prepared, **Save to group**
and **Delete** wait, because they would move or remove the files it is reading.

**From a group.** **Email…** on the toolbar sends the rows you have selected (Ctrl/Shift for
several), in page order. **With nothing selected it sends the whole group.** One selected row
sends that one page. (Export works differently: it treats a single selected row as the whole
group. Email does not, so choosing one page never sends sixty.)

**The dialog.** It says how many pages will go and asks two things:

- **Subject** — starts as the group's name. It also names the attachment file.
- **Attach as** — **One PDF** (one file, opens anywhere) or **Separate images** (the page files
  exactly as scanned, one attachment per page). Your choice is remembered for next time.

Press **Continue** to go on, or **Cancel** to stop, and nothing leaves the app.

**The first time you email from a committed evidence group**, the dialog also says *"This group is
a committed evidence record"*: what you send is a copy, it leaves the folder whose index, checksums
and originals are what make it evidence, and the folder itself is not changed. Tick **"I understand
— don't show this again"** and it will not come back. It never stops you sending.

**First, tell FG Scanner how you send mail** — **Settings → Email → "Send email with"**, then
**Save**:

- **A mail program on this PC** (Outlook, Thunderbird…) — the pages are attached for you.
- **Gmail in the browser** or **Yahoo Mail in the browser** — a new message opens in your browser
  with the subject filled in, and an Explorer window opens beside it with the file selected.
  **Drag the file from Explorer into the message.** That one drag is the only thing FG Scanner
  cannot do for you: no program can attach a file to a web page. Several images: select them all
  in the Explorer window and drag them together.
- **Gmail account** (optional, beside the list) — with several Google accounts signed in, the
  browser opens whichever it holds first, which may not be the one you want to write from. Put the
  address here (or its number in the browser: 0, 1, 2…) and the message opens in that account.
  Leave it blank for whichever the browser picks.

**What happens next depends on that setting and on what is installed**, and the line under the
buttons says which:

| You see | What it means |
|---|---|
| *"3 pages attached. Your mail app reports the message as sent."* | Your mail app (classic Outlook, for example) opened a new message with the pages attached, and you sent it. |
| *"You closed the message without sending it — nothing left the app."* | The same, but you closed the message instead of sending it. |
| *"3 pages were made into one PDF. A new Gmail message is open in your browser, and the file is selected in the Explorer window beside it — drag it into the message. It stays there until FG Scanner closes."* | Gmail or Yahoo Mail is set. Drag the file into the message, then send it. **Attach it before closing FG Scanner.** |
| *"Gmail could not be opened in your browser. The attachment is in … — start a new Gmail message and drag it in."* | The browser did not open. Open Gmail yourself; the Explorer window with the file is already there. |
| *"3 pages attached. The Windows Share sheet is open — choose your mail app there. The attachment is in … as well, if your mail is in a browser."* | Windows' Share panel is open with the pages in it. Pick your mail app — new Outlook, for example — and send from there. Closing the panel sends nothing. If your mail is Gmail or Yahoo, close it, choose that in Settings, and the file is in the folder named. |
| *"3 pages were made into one PDF. No mail app was found. The attachment is in C:\…\ — attach it to your message yourself. It stays there until FG Scanner closes, and is removed the next time it starts."* | No mail app could be reached. An Explorer window opens with the file selected: start a message yourself and attach it. **Attach it before closing FG Scanner** — closing the app removes the copy, and so does the next start if the app was killed. |
| *"Attached to a message these are about 28 MB. Mail servers often refuse anything over 20 MB, so this may bounce — send fewer pages at a time."* | Shown after the message opens. It may still go through; the limit is the recipient's server. |
| *"The page scan_00007.jpg is no longer on disk, so nothing was attached."* | A page's file is missing. Nothing is sent rather than a message one page short. Use **Reconcile** on the group to find out what happened. |
| *"The attachment could not be built, so nothing left the app. (…)"* | Something stopped the attachment being made — a damaged page file, or a full disk. The reason is in brackets. |

**What is kept.** Each send is written to the log (`%LOCALAPPDATA%\FGScanner\logs`) with where it
came from, how many pages, the format and the route. The log never records who you sent it to, or
the subject. The attachment copies are temporary: they are deleted when FG Scanner closes, or the
next time it starts if it closed unexpectedly.

## OCR

"OCR pages" recognizes text (English out of the box; add languages in
Settings). Each page gets a `<image>.md` Markdown sidecar beside it, the OCRed
column updates in the index, and the text becomes searchable in the database.
Pages under 65% confidence show "⚠ review". "Re-OCR all" redoes everything —
old `.md` files go to the Trash. "Export PDF…" can embed a selectable text layer.

## AI descriptions (optional)

Settings → AI: paste your own Google AI Studio key (paid tier recommended —
see PRIVACY.md), validate, and an "AI describe" button appears in Groups. Every
run shows a page count and cost estimate first; results land in the
AIDescription index column. Blank pages are skipped without an API call.

## Existing folders

"Process existing folder…" registers a folder full of images and PDFs as a
group — file names are kept, PDFs become `<name>_page_NNN.png` pages, and
running it twice changes nothing. "Reconcile" re-matches files you renamed in
Explorer (by content checksum) and reports files that vanished.

## Command line

```
fgscanner scan --group C:\Scans\Inbox --source feeder -n 2
fgscanner process C:\Scans\Inbox --ocr --write-index
fgscanner export --group C:\Scans\Inbox -o inbox.pdf --pdfcompat A2-b --ocr
fgscanner list-devices
```

Exit code 0 = success. Ideal for Task Scheduler.

`--verbose` and `--fake` are **global** options and must come *before* the
command — `fgscanner --verbose scan --group ...`. Placed after the command they
are rejected with "Unrecognized command or argument".

> **`fgscanner.exe` requires the .NET 10 Desktop Runtime.** Unlike `FgScanner.exe`,
> which is self-contained, the command line tool is published framework-dependent.
> On a machine without .NET 10 it exits with "You must install .NET". Install the
> runtime from <https://dotnet.microsoft.com/download/dotnet/10.0> — the GUI needs
> nothing. This matters most for headless or server machines running scheduled
> tasks, which are exactly where the runtime is least likely to already be present.

## Keyboard shortcuts

Rebindable in Settings. Defaults: Ctrl+Enter scan, Ctrl+S save to group,
Ctrl+Shift+Enter commit, Ctrl+Z/Y undo/redo, Ctrl+Shift+←/→ rotate,
Delete → Trash, F2–F12 select profile 1–11.

The page keys — Delete, undo/redo and rotate — act only on the screen that is showing:
- **Groups:** they act on the selected page, and Delete moves it to the Trash.
- **Scan:** Delete removes the selected unsaved scans, to the Recycle Bin.
- **Search, Trash and Settings:** they do nothing.

The scan, save, commit and profile keys work from any screen.

## Your data

The SQLite database (`%APPDATA%\FGScanner\fgscanner.db`) is yours to query —
the views `v_index`, `v_pages`, and `v_ocr_text` are the stable public
surface; see docs/db-schema.md. The database is backed up automatically
before every schema migration.
