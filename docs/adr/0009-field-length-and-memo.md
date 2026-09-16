# ADR-0009 — Field length and memo are settings on Text, not a new field type

**Status:** accepted, 2026-09-16

## Context

Operators asked for two things a profile could not express: a character length for a field, so a
typo of forty characters in a ten-character reference is caught while it is being typed, and a
larger, resizable box for the long fields (`Title`, `Parties`, `Notes`) that a one-line grid cell
makes unreadable.

The obvious shape — a new `FieldType.Memo` — cannot be used here. `FieldType` is cast
**positionally** to `FgScanner.Core.Index.IndexFieldType` (`DocumentRow.cs`, `ProfileService.cs`),
so a new member either shifts every existing value or has to be appended and remembered forever.
Worse, the type's **name** is written into `manifest.json` by `ManifestBuilder`
(`FgScanner.Core/Index/Writers.cs`), and `manifest.json` is an external contract the JimsStuff
importer parses (`pipeline/import_fgscanner.py`). A new type string is a contract change for a
feature that is purely about how a value is typed on screen.

## Decision

`FieldDefinition` gains two properties, both **Text only**:

- `MaxLength` (`int?`, null = no limit) — 1–100 characters, or 1–2000 when `Memo` is set.
- `Memo` (`bool`) — show this field in a larger box the operator can resize.

Both are cleared on non-Text types at the save boundary (`ProfileService.Sizing`) and again where a
stored field is converted for validation (`FieldDefinition.ToIndexFieldDef`), so one rule holds even
for a definition built in code that never passed through a save.

**Neither value is exported.** `manifest.json`, `index.json`, CSV, XLSX and XML are byte-identical
before and after a field gains a length — pinned by a test that compares the written files, not by
convention. `IndexFieldDef` carries `MaxLength` only so the validator can see it; no writer emits it.

`.fgprofile` is written as `FormatVersion` 3 **only when some field actually uses** a length or memo,
so an ordinary profile still imports on 0.4.0, which refuses anything above 2. Import accepts 1–3,
and an out-of-range length in a file degrades to null rather than failing the import.

"Build the Evidence profile" carries both values forward by field name instead of rebuilding them
from code, so repairing a profile no longer wipes what the operator set.

Enforcement never uses `TextBox.MaxLength`: it truncates a pasted value silently, and on evidence a
quietly shortened title is data loss nobody sees. A small attached behaviour refuses the paste whole
and says why.

On screen, a field's **width and its character length are independent** (Franz, 2026-09-14): an
ordinary text box fills the form's width whatever its limit, and only a memo box is resized, by
dragging its grip.

## Consequences

- Changing a length mints a new field-layout version, like any other field edit. Existing groups keep
  the version they were created with until "Use latest field layout" is pressed.
- Shortening a length never trims stored values. A value now too long shows as invalid, like any
  other invalid value, until someone corrects it — the app does not edit evidence to fit a setting.
- Length counts UTF-16 characters, so an emoji may count as two. Documented beside the rule.
- Nobody should later "tidy" `Memo` into a `FieldType`. The reason is in Context, and it is a
  contract break, not a refactor.

## Alternatives rejected

**`FieldType.Memo`.** Breaks the positional cast to `IndexFieldType` and puts a new `"type"` string
into `manifest.json`, which the JimsStuff importer reads.

**`TextBox.MaxLength`.** Silently truncates a paste. The whole point of a limit here is to tell the
operator something is wrong, not to quietly make it fit.

**Exporting the length with the field definition.** The index files are an external contract and
nothing downstream needs a length; adding one would be an importer change and a separate agreement.
