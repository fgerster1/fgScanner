# ADR-0001 — UI strings stay in English, no `.resx`

**Status:** accepted, 2026-08-25
**Supersedes:** the CLAUDE.md rule "User-visible strings go in .resx from phase 3 onward"

## Context

The original hard rule required all user-visible strings to live in `.resx` files from phase 3
onward. It was never followed. As of 2026-08-25 `find src -name "*.resx"` returns nothing, and
`src/FgScanner.App` holds ~9,650 lines with every string hardcoded in XAML and view-models. PLAN §9
also specified a first-run wizard step for language, which does not exist because there is nothing
to switch between.

So the rule had been silently violated for seven phases, and the v0.2 milestone was about to add
several hundred more strings. Three options were on the table: extract to `.resx` first (1–2 days
before any feature work), keep hardcoding and decide later, or drop the requirement.

## Decision

Drop it. FG Scanner ships an English-only UI. The CLAUDE.md rule is amended to say so.

## Consequences

- The v0.2 work proceeds without a two-day detour, which matters because the owner's stated priority
  is making the app usable for their own workflow before releasing.
- The rule stops being a lie. A rule that is violated everywhere teaches everyone to ignore the
  rules that matter — the licensing guards in the same section are not optional, and they are
  devalued by sitting next to one nobody follows.
- Adding a second language later is a real project, not a config change: an extraction across a
  codebase larger than today's. That cost is accepted knowingly.
- The first-run wizard has no language step, and the PLAN reference to one is obsolete.

## Alternatives rejected

**Extract to `.resx` first.** Correct if localization is ever likely. Rejected because no second
language is planned, and it would delay every requested feature by two days to serve a hypothetical.

**Keep hardcoding, decide later.** Rejected outright: this is the option that created the problem.
Deferring again would grow the debt by an entire milestone while leaving the rule formally in force
and formally ignored.
