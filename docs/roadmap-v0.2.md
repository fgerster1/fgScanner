# Gap analysis & proposed slices — the v0.2 request

**Written:** 2026-08-25 · **Status:** analysis complete, nothing designed or built yet
**Method:** seven parallel code probes over the existing source, then synthesis. Every claim below
carries a `file:line` citation from the audit; nothing here is inferred from the architecture.

Source request: 14 changes across Scan, Groups, Search and Profile.

---

## 1. The headline: less is missing than it looks, but three live bugs turned up

Of the 14 requests, **four already work today** and several more are a missing UI binding over logic
that is already built and tested. The audit also found **three defects unrelated to the request**,
two of which lose user data silently. Those matter more than any feature here.

## 2. Bugs found (not requested — fix regardless)

### BUG-2 — pre-scan field entry is silently discarded 🔴

The Expander UI for entering field values *before* scanning exists (`GroupsView.xaml:49-91`,
`GroupDetailViewModel.cs:445-455`) and the consumer exists (`ScanViewModel.cs:274-275` →
`IndexingService.cs:77-101`). But `PushPendingValues()` is called exactly once, *before* the editors
are populated (`GroupDetailViewModel.cs:91,349-352`), and nothing subscribes to
`PendingFieldEditor.Value`. **Everything the user types before scanning is dropped at scan time,
with no error.** One missing subscription. Effort **S**.

### BUG-3 — editing a blank-flagged row wipes its field values 🔴

Blank pages are shown and editable, but they are filtered out of the export projection that
`ReloadRowsAsync` loads values from (`IndexingService.cs:337-340`,
`GroupDetailViewModel.cs:105-115`). Editing one overwrites `CustomFieldsJson` wholesale from a
near-empty snapshot (`IndexingService.cs:38-39`), destroying previously applied Sticky/Default
values, and the values never reach an index file. **Live data loss on existing installs.**
Effort **M**.

### BUG-4 — two groups can own one folder 🟠

`Group.DirectoryPath` is compared with BINARY-collated SQLite equality (`GroupService.cs:35`,
migration `:37`). On case-insensitive Windows, `C:\Docs\Invoices` and `c:\docs\invoices` both pass
the uniqueness check, producing two Group rows over one physical folder whose index files overwrite
each other. Compounding it, `Create…` on an existing folder silently routes to *adopt* with no
message (`GroupService.cs:20` → `:34-39`). Effort **M**, and fixing it is a data migration because
affected rows may already exist.

## 3. Already works — do not rebuild

| You asked for | Reality |
|---|---|
| "a process to add new groups" | **Exists.** Name box + `Create…` in the Groups left pane (`GroupsView.xaml:22-26`), plus `Open folder…` and `Process existing folder…`. Only flaw: an empty name silently no-ops (`GroupsViewModel.cs:149-152`). |
| "a group should be everything in a single directory" | **Exists.** `Group.DirectoryPath` with a unique index; adopt normalises the path and returns the existing group, else creates one named after the folder (`GroupService.cs:31-55`). Subject to BUG-4. |
| "a place to fill out the data based on the profile" | **Exists.** The grid rebuilds one editable column per schema field, List fields become real ComboBoxes, cells validate live, Required blocks commit (`GroupsView.xaml.cs:81-141`). Values persist with no Save button and reach all four export formats. |
| "if you switch profiles the list of groups should change" | **Half exists.** The association is real and populated (`Entities.cs:143-147`). Only the *filter* is missing — see slice 1. |

## 4. Cheap — logic exists, no UI reaches it

Each of these is a binding or a predicate, not a feature build. All **S**.

- **View OCR text** — `Page.OcrText` is persisted (`OcrQueueService.cs:115`) and
  `ReloadRowsAsync` already holds the full Page entities; it just never copies it onto `DocumentRow`.
- **View AI description** — `Page.AiDescription` is written, stored and exported. Today the only way
  to read it is opening the CSV. Same one-line mapping.
- **Show the document's directory** — `TrashView.xaml:22` already proves the pattern with an
  "Origin folder" column; the Groups grid has no path binding at all.
- **Filter groups by profile** — needs `ListGroupsAsync` to `Include(g => g.Profile)` and a
  predicate; the association it filters on already exists.
- **Show duplicate file names** — `AdoptResult.DuplicateSourceFiles` is already returned and thrown
  away by the UI (`ScanViewModel.cs:285-288`).
- **Three dead Profile columns** — `OcrLanguages`, `AiDescriptionEnabled`, `ScanSettingsJson` exist
  in the DB with zero readers or writers. Wiring the first two needs no migration.

## 5. Real builds

| Work | Effort | Note |
|---|---|---|
| Delete a group | M | Zero code exists. Needs a file policy decision first (§7). |
| Rename a profile | S | No update-name method anywhere; Export→Import produces a "(2)" copy. |
| Delete a profile | M | Needs a referential policy for `Group.ProfileId` and a guard so the last profile survives. |
| Per-profile base directory | M | New column + migration; three folder dialogs must consult it. |
| Profile-owned scan settings | M | `ScanSettingsJson` is dead; real settings are ephemeral view-model properties that reset every launch. |
| Scope search to one group | M | `SearchAsync` has no group parameter; Group is output-only today. |
| Scan button inside Groups | M | No missing logic — `SaveToGroupAsync` and `ScanCommand` already exist. M only because scan settings must be extracted first. |
| Duplicate review with a choice | M | `AdoptPagesAsync` has no `allowDuplicates`, and the skipped file is destroyed immediately after (`ScanSessionService.cs:35-38`). |
| Text-similarity dedup (≥90%) | M | No similarity implementation exists. Token-set Jaccard or cosine beats edit distance on OCR output. |
| Perceptual image dedup (≥80%) | **L** | Nothing perceptual exists; the only hashing is cryptographic. Needs a new `Page.ImageHash` column + migration. **Must be hand-rolled** — `NAPS2.Images.ImageSharp` and `Emgu.CV` are both on the CLAUDE.md forbidden list. |
| Move pages between groups | **L** | The hardest item by a distance — 11 touchpoints (§6). |

## 6. Why cross-group move is L, not M

`Document.GroupId` is only ever assigned at insert; `ReorderService` reorders *within* one group.
Today's only workaround is export-then-import, which mints a new GUID and loses field values, OCR
status and AI description. A correct move must handle: the image **and its sidecars**; `GroupId`;
sequence renumbering in **both** groups; a checksum re-check against the target (dedup is scoped per
group, `GroupService.cs:89-91`); `CustomFieldsJson` when the two groups sit on different schema
versions; re-export of both groups; the commit hook firing twice; trash origin capture; the FTS row
being **UPDATEd, never delete+inserted**, or search silently drifts; and in-flight OCR/AI jobs that
resolve the group directory at run time.

## 7. Decisions needed before design

1. **Group delete — what happens to the files?** Delete the directory, move it to Trash, or
   unregister and leave the files. Trash today is document/page-scoped only (`TrashService.cs:24`),
   so "send a group to Trash" is itself a new capability.
2. **Profile delete — what happens to its groups?** Block while referenced, reassign to Default, or
   null out. `Group.ProfileId` has no declared FK delete behaviour (`FgScannerDbContext.cs:28-31`),
   so today it could cascade groups away or throw at runtime.
3. **Duplicate action default.** You said "let the user choose delete or leave" — does the near-match
   case ever auto-skip, or always ask? Exact matches auto-skip today.
4. **Localization (D1).** Still unmade, and this work adds hundreds of user-visible strings. Deciding
   now is minutes; deciding later means touching every new file twice.

## 8. Proposed slices

Ordered by dependency and by value-per-day. Each is independently shippable.

**Slice 0 — the three bugs.** BUG-2, BUG-3, BUG-4. Not requested, but two lose data silently.

**Slice 1 — visibility (all S, no migrations).** View OCR text · view AI description · show the
document directory · filter groups by profile · show duplicate names. *This answers four of the 14
requests for roughly a day of work* and is the highest value-per-hour in the whole list.

**Slice 2 — group management.** Delete a group (after decision 1) · Scan button in Groups. Requires
extracting scan settings out of `ScanViewModel` first.

**Slice 3 — profiles.** Rename · delete (after decision 2) · base directory · wire the three dead
columns · bump the `.fgprofile` FormatVersion so shared profiles don't go silently incomplete.

**Slice 4 — search.** Group scoping · prefix matching · truncation indicator (the 50-hit cap is
invisible today and the status line reports the capped count as if it were the total).

**Slice 5 — duplicates.** Review dialog first (nothing else has anywhere to present a candidate),
then text similarity, then perceptual image hashing.

**Slice 6 — cross-group move.** Last, because it depends on decisions settled in 2 and 5.

## 9. Constraints that shape all of it

- **Commit is one-way.** Nothing sets `GroupState` back to Open. Any review/undo step collides with
  that, and a user who commits early has no path back.
- **Re-export fires on every field edit** of a committed group, rewriting `manifest.json` and running
  the commit hook. A cross-group move rewrites two groups and fires the hook twice.
- **Trash restores to paths captured at delete time.** Deleting a group or moving a page strands
  already-trashed items.
- **FTS5 rows must be UPDATEd, not delete+inserted**, or search results drift silently.
- **Schema versions are immutable and groups pin one.** Changing a Default or Sticky flag has no
  retroactive effect — any UI implying otherwise will mislead.
