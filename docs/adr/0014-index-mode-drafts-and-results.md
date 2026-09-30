# ADR-0014 — Index mode: drafts outside the package, staged answers, delete gating

Date: 2026-09-30 · Status: Accepted · Spec: SPEC-2026-008 (JimsStuff SPEC-2026-003 phase 4)

## Context

The Index section lets Jim answer five index questions per document against a
portal-exported package and send a `results.json` back. The package is contract
law (checksummed, refused on any byte change), the answers land in a legal
record, and the work spans days on a laptop that crashes, updates, and loses
power.

## Decisions

1. **Drafts live at `%APPDATA%\FGScanner\index-drafts\<packageId>.json`, never in
   the package folder.** The package must stay byte-identical to what the portal
   exported or its own checksums make it refuse everywhere. The draft is written
   atomically (house `AtomicFileWriter`) after every staging change and carries
   the package checksum it belongs to: a re-export of the same id is a DIFFERENT
   package, and its draft is refused with a sentence — never merged, never
   deleted. A failed save is surfaced in-section the moment it happens, because
   an answer Jim believes saved that is not is this screen's worst outcome.

2. **Suggestions never auto-apply.** An untouched AI suggestion produces no
   answer; Accept (or manual entry) is what stages one. Silence is not consent
   on a legal index — the program constitution's "automation proposes, humans
   decide" enforced at the widget.

3. **`decidedAt` is stamped when the answer is staged, not when it is
   exported.** The portal's importer dedupes on the answer event's identity,
   which includes `decidedAt`; export-time stamps would make the designed
   partial-then-full send-back duplicate every earlier row. Staging time is also
   simply the truth — it is when Jim decided.

4. **Staging slots follow SPEC-2026-003 §07 as amended 2026-09-30**: one entry
   per field for doc_type/date/key_flag (latest edit replaces, a date's
   qualifier belongs to the answer not the slot), one entry per (qualifier,
   person) and per subject for the multi-value fields. A multi-value withdrawal
   is refused at entry: the contract's empty value cannot name WHICH person or
   subject it withdraws, so removal of a decided person/subject belongs to
   phase 5's web UI, and the checkbox for a portal-decided subject springs back
   with that sentence instead of lying.

5. **Delete is gated on coverage, not on time.** "Remove package from this
   computer" arms only while the latest export covers every currently staged
   answer (and who signs them); any later change disarms it until exported
   again. Confirmation happens inside the section with the counts on screen;
   the exported results file always survives the delete — it is the work
   product.

## Consequences

- A crash or auto-update mid-batch costs nothing; reopening resumes exactly.
- Re-exports are byte-identical when nothing changed, so checksum-based
  dedupe at the portal works across partial sends.
- Un-deciding a person/subject requires the portal (phase 5) — recorded as a
  §03 non-goal in SPEC-2026-008 and in the §07 amendment.
