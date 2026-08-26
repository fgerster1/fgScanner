# ADR-0002 — Auto-orientation rotates to the detected angle, not 180° only

**Status:** accepted, 2026-08-25
**Supersedes:** decision #4 of `docs/scope-auto-orientation.md` ("rotate on 180 only, surface
90/270 as a review flag")

## Context

A sheet fed into the ADF the wrong way round OCRs to reversed gibberish that nothing downstream can
distinguish from a clean read. `docs/scope-auto-orientation.md` scoped the fix on 2026-08-24 and
settled four decisions. Decision #4 restricted correction to 180°, on the belief that Tesseract's
`--psm 3` layout analysis already reads sideways pages correctly, so touching them risked making
things worse for no gain.

Measurement on the owner's real scans refutes that belief. Mean word confidence at 300 DPI, `--psm
3`, on lossless rotations of seven pages:

| page | 0° | 90° | 180° | 270° |
|---|---|---|---|---|
| test/scan_00001 | 90.78 | 42.73 | 42.69 | 91.20 |
| test/scan_00002 | 91.63 | 35.13 | 35.10 | 91.86 |
| test/scan_00003 | 86.53 | 35.11 | 35.56 | 88.11 |
| test/scan_00004 | 88.04 | 32.64 | 31.26 | 82.45 |
| twain-feeder/scan_00001 | 95.68 | 42.51 | 42.23 | 95.70 |
| twain-feeder/scan_00002 | 79.65 | 23.68 | 23.41 | 79.39 |
| twain-feeder/scan_00003 | 90.14 | 28.27 | 28.70 | 91.02 |

Exactly **one** of the two sideways directions reads correctly. The other scores in the same band as
a 180° page and is indistinguishable from it without OSD. A 180-only rule would therefore rotate a
broken sideways page by 180° — landing on the *other* sideways direction, which reads fine — and
leave a landscape page on disk that no longer looks wrong to any confidence check. That is worse
than not correcting it, because it removes the only signal that something is off.

Two further measurements bear on this. OSD called the angle correctly on 10 of 10 pages across all
four orientations, at ~0.8s per page, with no false positives on upright pages. And orientation
varies **within** one feed — the `empty-feeder` batch has two pages inverted and one upright — so
correction has to be per page rather than per batch.

## Decision

Detect orientation per page with an OSD pass and rotate the stored image by whatever angle OSD
reports: 90°, 180° or 270°. Do not special-case which angles are eligible.

An undetectable orientation (a blank sheet, too little text, a missing model) leaves the page
untouched. "Cannot say" must never be read as "upright", and a page that needs no rotation is left
byte-identical rather than re-encoded.

## Consequences

- Sideways pages are corrected rather than flagged, so the "review flag" for 90/270 that decision #4
  called for is not built. Confidence now travels into the index (ADR context: GAP-2), which covers
  the residual case where detection fails.
- Every OCR job pays ~0.8s for the OSD pass, roughly doubling per-page OCR time. `Feature.AutoOrient`
  turns it off for users who never misfeed; it defaults **on**, unlike the phase-10 flags, because
  the failure it prevents is silent.
- Correction rewrites the image, so the stored checksum and perceptual hash describe the previous
  picture. `ReorderService.RefreshChecksumAsync` now clears `Page.ImageHash` — which also fixes the
  same staleness for every manual edit, where it was already wrong.

## Alternatives rejected

**Single-pass `--psm 1`.** With `osd.traineddata` present this repaired all 28 rotated cases in one
pass and produced byte-identical text on all seven already-upright pages — cheaper than OSD plus a
rotate, and it catches 90/270 for free. Rejected as the *whole* answer because it fixes only the
text: the stored image stays upside down in the viewer, in exports, and for duplicate matching. The
owner's instruction was to correct the page, not just what is read from it.

**Gate the OSD pass on low confidence.** The originally scoped design, and cheaper — most pages
would skip it. Rejected because a sideways page in the "good" direction scores 91%, sails past any
confidence gate, and stays sideways on disk forever.
