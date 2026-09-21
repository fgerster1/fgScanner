# ADR-0011 — A two-pass stack is put in order before it is adopted, and keeps every back

**Status:** accepted, 2026-09-21 (SPEC-2026-006)

## Context

Most feeder scanners on this station scan one side. Capturing both sides of a stack therefore
means two passes: the fronts, then the whole stack turned over, then the backs. The pages come
off the scanner as `F1 F2 F3 … B3 B2 B1` — the backs reversed, because turning a stack over
end-for-end reverses it — and have to end up as `F1 B1 F2 B2 F3 B3`.

FG Scanner already had a tool for that shape. The Groups page has **Reverse**, **Interleave** and
**Deinterleave** buttons backed by `ReorderService`, which rewrites `Document.Sequence` for a
group that has already been adopted. The obvious move was to reuse it: adopt the two passes, then
press Interleave.

Two things make that the wrong place.

**Adoption is where the order becomes the record.** `GroupService.AdoptPagesAsync` numbers
documents in the order it is handed them and renames every file to `scan_NNNNN` to match. Those
names and numbers are what `index.json` carries and what
`JimsStuff/pipeline/import_fgscanner.py` reads. Reordering afterwards rewrites rows that are
already written, and on a committed group it also re-exports — so the pairing arrives as a second
edit to an existing exhibit rather than as the shape it was captured in.

**A repair the operator has to remember is a repair the operator will forget.** Interleave is a
button on a different page, pressed after the stack is already in the group and looks finished.
A stack saved and not interleaved reads as a normal document set: twenty pages, sensible names,
nothing visibly wrong. The failure surfaces when an exhibit is read out.

There is a second, separate trap on the same path. Adoption skips a page whose SHA-256 is already
in the group — the protection against a folder adopted twice or a batch replayed after a partial
run. The blank backs of a stack scanned at one setting are **byte-identical**, so ten sheets
would save one blank back and silently drop nine, shifting every pairing after the first loss.
Capture triage, which runs before adoption, has its own way of removing the same pages: a profile
whose blank-page policy is **Drop** deletes them outright, not to the Recycle Bin.

## Decision

**The pairing happens on the Scan page, before anything is saved.** `DuplexPassSequence` in
`FgScanner.Core.Capture` records the two passes and works out the order; `ScanViewModel` applies
it to the page list. The list is then the order the save reads — `SaveToGroupAsync` hands adoption
the pages in list order, so the `scan_NNNNN` numbering is the pairing, first time, with nothing to
undo.

**The Groups-page Interleave button is untouched.** It remains the repair tool for stacks captured
before this feature, and for a stack whose pairing was refused.

**A stack is measured against itself.** The pages staged before it started — an earlier scan, or a
session restored from crash recovery — keep their place ahead of it. The reorder moves records
rather than replacing them, so the thumbnail selection survives it; that also means the pages keep
the sequence numbers they were *captured* under, and the list, not those numbers, is the order.

**On a count mismatch, nothing is paired** (§05 Q1a). Both counts are reported and the pages are
left exactly as captured. An odd stack — a last sheet with no back — is a mismatch and is refused
like any other. This costs a rescan; the alternative is a confident wrong pairing, which is worse
than an obvious mess because nobody looks for it.

**Every back is kept** (§05 Q2a). A save that knows its pages were captured as pairs suppresses
**both** removal mechanisms for those pages: adoption's checksum skip, and triage's blank-page
Drop. Neither is changed for any other path — the skip is what protects every ordinary save, and
the policy is a batch-scanning convenience the operator set for good reasons. The pages are still
classified and still flagged blank, because the row recording that the back is blank *is* the
evidence that it is blank.

The suppression is granted to **the pages**, not to the save. It survives a save that could not
take every page, because the retry is still that stack's save; it does not survive the pages
themselves, because a flag left standing turns de-duplication off for the next, unrelated save.

**Which back belongs to which front is the operator's answer, not a guess** (§05 Q3a). "The backs
come off the stack in reverse order" is on by default, because turning the whole stack over is the
usual gesture. Getting it wrong reverses every pairing while leaving the page count correct, which
is exactly the failure no count can catch — so the app asks rather than inferring it from the
images.

## Consequences

- The `scan_NNNNN` numbering in a group is the sheet order as captured. Nothing re-numbers an
  adopted stack, and a committed group is not re-exported to fix a pairing.
- A two-pass stack always doubles the page count. Ten sheets are twenty pages, including ten
  blank ones. Storage grows; that is the price of the record showing that a back was blank.
- A mismatch means a rescan. The operator can also save the pages unpaired and use the Groups-page
  Interleave button, which is why it stays.
- **The pairing does not survive a crash.** It lives in the Scan page's list, and the recovery
  session knows only the order the pages came off the scanner (§16 R6). A recovered session says
  so on screen rather than letting a mis-ordered stack look normal.
- Two-pass is refused unless the source is the feeder. On the flatbed a "stack" is one sheet
  scanned twice; on a one-pass duplex scanner each pass already returns both sides, so two passes
  return four images per sheet and mispair every one of them with the counts still matching.

## Alternatives rejected

- **Adopt, then Interleave on the Groups page.** Renumbers written rows, re-exports a committed
  group, and depends on the operator remembering a second step on a second page after the work
  looks done.
- **Detect fronts and backs from the images.** Pairing is a counting problem, not a perception
  problem, and a model that is wrong produces a mis-paired exhibit nobody notices (§08a).
- **Let the blank-page policy decide the backs** (§05 Q2's option c). It silently breaks pairing,
  and it makes an evidence question depend on a setting chosen for batch scanning.
- **Write the paired order back into the recovery index** so it survives a crash. It would change
  the recovery format and break the filename-to-sequence correspondence that
  `ReserveNextPagePath` and `PageSequence.FromPath` both rely on. The spec took the message
  instead; revisit it if crashes mid-stack turn out to be real.
