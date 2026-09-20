# SPEC-2026-005 — Prompt pack

Prompts for [SPEC-2026-005-memo-and-field-widths.md](./SPEC-2026-005-memo-and-field-widths.md).
Work through them in order. Each stops at a checkpoint; paste the results back before
starting the next.

Branch: `phase-24-memo-and-widths`. Approved 2026-09-20; build second, after SPEC-2026-004.

---

```
PROMPT 1 of 5 — A memo opens big enough to read
Spec: SPEC-2026-005 §04, §08, §10 (AC-1..AC-3), §16 R2, R3    Phase: 1    Depends on: nothing

Read SPEC-2026-005 §04 and §08 before starting. Create the branch
phase-24-memo-and-widths from main if it does not exist.

Context: the memo box already wraps and scrolls (RecordEditorWindow.xaml:45-69). It opens
at MinWidth=120 / MinHeight=40 (:57) — about two lines — and only grows when a stored
size is restored (xaml.cs:190-202) or the operator drags the grip. §05 Q1 was answered
(a): a FIXED default of about four rows at the full form width. Do NOT derive it from the
character limit — ADR-0009:46-48 keeps width and length independent, and that stands.

Two constraints that bite here:
- RecordEditorLayoutStore.MinMemoWidth (=120) must keep agreeing with the XAML MinWidth;
  RecordEditorLayoutStoreTests.cs:221-226 asserts it, with a comment saying so.
- Stored sizes are clamped to 2..20 lines (MinMemoLines/MaxMemoLines, :31-33). The new
  default must sit inside that range; MinMemoLines stays 2 — it is the floor for a
  DRAGGED size, not for the default.

Do only this:
1. Failing tests FIRST in tests/FgScanner.App.Tests/RecordEditorLayoutStoreTests.cs:
   - the default memo size, passed through ClampMemo, comes back unchanged;
   - a stored 6-line size still wins over the default after a load.
2. Apply the default in the XAML, converting rows to pixels with the existing
   line↔pixel helpers (xaml.cs:253-260) so one definition of a "line" is used.
3. Keep the restore path ordered so a stored size overrides the default, never the
   reverse (§16 R3).
4. Add a Debug log line naming the field when a stored size is clamped (§14).

Do NOT in this prompt: touch the ComboBox, Settings, the Groups side panels, or the
grid columns. Those are Prompts 2 and 3, and the panels are out of scope entirely.

Stop when the checkpoint below is satisfied and report the results.

CHECKPOINT 1 — paste back:
  · dotnet test -c Release → count and "passed"; no lower than 692
  · the two new test names, failing first
  · confirm MinMemoWidth and the XAML MinWidth still agree (paste both lines)
  · open a record with a filled Notes field and say how many lines are visible
```

```
PROMPT 2 of 5 — Dropdowns as wide as their content
Spec: SPEC-2026-005 §08, §10 (AC-4, AC-5), §16 R5    Phase: 1    Depends on: Prompt 1 (green)

Read SPEC-2026-005 §08 (Dropdown width) before starting.

Context: RecordEditorWindow.xaml:71-75 sets no Width, no MaxWidth and no
HorizontalAlignment, so the ComboBox fills the * column. The form StackPanel is bound to
at least the viewport width (:131-134) — that stays, so the COLUMN keeps its width and
every other input still fills the form. §05 N1 was answered (a): the record editor only.

Note this reverses a non-goal from SPEC-2026-002:83 ("no per-field width for non-memo
fields"). Franz approved the reversal on 2026-09-20; record it in SPEC-2026-005 §04 if it
is not already clear there.

Do only this:
1. Add HorizontalAlignment="Left" to the list ComboBox, and cap it at the form pane width
   the way the memo box already is (xaml.cs:247) so one very long choice cannot force a
   horizontal scrollbar (§16 R5).
2. Check by eye: a list with no choices, a normal list, and a list with one very long
   choice.

Do NOT in this prompt: change EntryGridColumns.cs (the grid's star-sized combo column) or
GroupsView.xaml's fixed 160px panels. Both stay as they are, by decision.

Stop when the checkpoint below is satisfied and report the results.

CHECKPOINT 2 — paste back:
  · dotnet build -c Release → clean, warnings are errors
  · dotnet test -c Release → unchanged count, still green
  · git diff --stat → RecordEditorWindow.xaml(.cs) only
  · say what the DocType dropdown looks like now against the form width
```

```
PROMPT 3 of 5 — "Memo" in the Type dropdown
Spec: SPEC-2026-005 §07, §08, §10 (AC-6, AC-7), §16 R1, R4    Phase: 2    Depends on: Prompt 2 (green)

Read SPEC-2026-005 §07 and ADR-0009 before starting. This one touches the export
contract's neighbourhood, so read both fully.

The rule that must not be broken: FieldType has FOUR members and keeps them. It is cast
POSITIONALLY to IndexFieldType (Entities.cs:167-173, IndexModels.cs:11-17) and the type's
NAME is written into manifest.json, which the JimsStuff importer parses. A fifth member
would change a legal pipeline's contract. ADR-0009:57-58 says nobody should tidy Memo
into a FieldType — that still holds. What changes is ONLY what the Settings screen shows.

§05 Q2 was answered (a). The mapping, display → stored:
  Text   → (FieldType.Text,   Memo = false)
  Memo   → (FieldType.Text,   Memo = true)
  Date   → (FieldType.Date,   Memo = false)
  Number → (FieldType.Number, Memo = false)
  List   → (FieldType.List,   Memo = false)

Do only this:
1. Failing tests FIRST in tests/FgScanner.App.Tests/FieldRowTests.cs:
   - "Memo" stores Text + Memo=true; "Text" stores Memo=false; "Date" drops both;
   - a field already stored as Text + Memo shows as "Memo" when Settings reopens.
2. Implement the display list and the two-way mapping in the Settings view model only.
   Do not change Entities.cs, IndexModels.cs or ProfileService's Sizing rule.
3. Remove the now-duplicate Memo checkbox column (SettingsView.xaml:101) — two controls
   for one truth can disagree (§16 R4).
4. Run the export snapshot tests and confirm they pass WITHOUT being re-accepted. If a
   Verify snapshot needs accepting, stop: the export contract has moved and the change is
   wrong.

Do NOT in this prompt: add an enum member anywhere, change ToIndexFieldDef, or touch the
Length column.

Stop when the checkpoint below is satisfied and report the results.

CHECKPOINT 3 — paste back:
  · dotnet test -c Release → count and "passed"
  · the new test names, failing first
  · git diff src/FgScanner.Data/ src/FgScanner.Core/ → EMPTY (nothing outside the App layer)
  · confirm no *.received.* snapshot file was produced
  · grep -n "enum FieldType" src/ → still four members
```

```
PROMPT 4 of 5 — Code review  [START A NEW SESSION FOR THIS]
Spec: SPEC-2026-005    Depends on: Prompt 3 (green)

Run /code-review max over the changes made in Prompts 1..3.

Then check compliance with the spec: does the code do what SPEC-2026-005 §10 says, and
only that? Report any drift.

Pay particular attention to §16: the export contract untouched (R1), MinMemoWidth and the
XAML agreeing (R2), a dragged size still beating the default (R3), and only one control
for the memo setting (R4).

Resolve every correctness finding. For anything you decline to change, write one line in
the spec's §22 saying what and why.

CHECKPOINT 4 — paste back:
  · the findings list with each one's resolution
  · dotnet test -c Release → count and "passed"
  · dotnet format --verify-no-changes → clean
```

```
PROMPT 5 of 5 — Documentation and memory
Spec: SPEC-2026-005 §18    Depends on: Prompt 4 (green)

Do only this:
1. Amend docs/adr/0009-field-length-and-memo.md — do not rewrite it. Add a dated section:
   Memo may be PRESENTED as a type in Settings while remaining a flag on Text; the export
   is unchanged; the default memo height is fixed and is NOT derived from the character
   limit, so :46-48 stands.
2. docs/user-guide.md — update "Length and memo (Text fields)": memo boxes now open at a
   readable size, and Memo is chosen in the Type list.
3. docs/manual-tests.md — add AC-1, AC-4 and AC-5 as manual rows (they cannot be
   automated). Leave them unticked.
4. CLAUDE.md — extend the existing "memo is a flag, not a FieldType" line with "even when
   the Settings screen shows it as one".
5. Update the memory note fg-scanner-memo-is-a-flag so a later session does not read the
   Memo entry in the Type dropdown as evidence that the enum gained a member.
6. Tick §21 in the spec and fill §22's Built row.

Do NOT in this prompt: change any source file.

CHECKPOINT 5 — paste back:
  · git diff --stat → docs and memory only, no src/
  · the ADR section heading you added
  · dotnet test -c Release → unchanged count, still green
```
