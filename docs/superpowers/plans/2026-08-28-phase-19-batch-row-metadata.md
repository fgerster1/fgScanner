# Batch and Row Metadata (Phase 19) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give an index field a *scope*, so `Box` and `Operator` hold one value per box instead of being retyped on every page, and record who captured each page as a fact the operator cannot edit.

**Architecture:** A new `FieldScope` enum marks a field `Row` (today's behaviour) or `Batch`. A batch field's value lives only on the `Group`, and one pure Core helper — `BatchFieldMerge` — decides what any given row shows. Four readers call it (export, validation, the entry grid, search), so there is exactly one place where "the group answers for this field" is expressed. Separately, `Page.CapturedBy` is stamped at capture time and emitted as a new JSON-only `index.json` key.

**Tech Stack:** .NET 10 · WPF · EF Core 10 + SQLite · xunit.v3 (MTP mode) · Verify snapshots

**Spec:** `docs/spec-batch-row-metadata.md` — read it before starting. Every task below argues from it.

**Branch:** `phase-19-batch-row-metadata` (already created, holds the spec commit `937a5b4`).

## Global Constraints

- **Build:** `dotnet build -c Release` — **warnings are errors** in Release.
- **Test:** `dotnet test -c Release` — MTP mode; test projects are `Exe`, no `Microsoft.NET.Test.Sdk`.
- **Format:** `dotnet format --verify-no-changes` is a CI gate. Run it before each commit.
- **Frozen external contracts — renaming any of these silently breaks a legal pipeline:** the `index.json` row keys (`sequence`, `pageId`, `checksum`, `isBlank`, `originalChecksum`, plus the original six), `manifest.json`'s `evidenceExport`, and the thirteen Evidence field names (`DocNo`, `DocDate`, `DocType`, `Title`, `Parties`, `Operator`, `Redact`, `Box`, `Notes`, `NoteState`, `NoteAuthor`, `NoteBasis`, `NoteWhen`). This phase is additive only.
- **Comments explain *why*, never what.** The codebase is consistent about this; match it.
- **Validate at boundaries only** (user input, files, external APIs). No defensive checks between internal callers.
- Dates ISO-8601; numbers invariant culture. UI strings are written inline in English, no `.resx` (ADR-0001).
- **No Bates support**, in this phase or any other.
- `NoteState` must never be made sticky or batch.
- Business logic must run without a scanner. Never mock the Tesseract engine; never use a live AI key.

---

### Task 1: `FieldScope` and the merge helper

Pure Core logic with no database involved. Everything later builds on this.

**Files:**
- Modify: `src/FgScanner.Core/Index/IndexModels.cs`
- Create: `src/FgScanner.Core/Index/BatchFieldMerge.cs`
- Test: `tests/FgScanner.Core.Tests/BatchFieldMergeTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `FgScanner.Core.Index.FieldScope { Row = 0, Batch = 1 }`; `IndexFieldDef(string Name, IndexFieldType Type, bool Required, FieldScope Scope = FieldScope.Row)`; `BatchFieldMerge.Effective(IReadOnlyList<IndexFieldDef> fields, IReadOnlyDictionary<string, string?> batchValues, IReadOnlyDictionary<string, string?> documentValues) → IReadOnlyDictionary<string, string?>`.

- [ ] **Step 1: Write the failing tests**

Create `tests/FgScanner.Core.Tests/BatchFieldMergeTests.cs`:

```csharp
using FgScanner.Core.Index;
using Xunit;

namespace FgScanner.Core.Tests;

public class BatchFieldMergeTests
{
    private static readonly IReadOnlyList<IndexFieldDef> Schema =
    [
        new("Box", IndexFieldType.Text, Required: true, Scope: FieldScope.Batch),
        new("Title", IndexFieldType.Text, Required: false),
    ];

    [Fact]
    public void Batch_field_is_answered_by_the_group()
    {
        var merged = BatchFieldMerge.Effective(
            Schema,
            batchValues: new Dictionary<string, string?> { ["Box"] = "12" },
            documentValues: new Dictionary<string, string?> { ["Title"] = "Deed" });

        Assert.Equal("12", merged["Box"]);
        Assert.Equal("Deed", merged["Title"]);
    }

    /// <summary>
    /// A field that was row-scoped before leaves its old value behind in every document's JSON.
    /// If that copy could resurface, "one source of truth" would be a convention rather than a
    /// property, and rows would silently disagree with the group after a correction.
    /// </summary>
    [Fact]
    public void Stale_document_copy_of_a_batch_field_never_resurfaces()
    {
        var merged = BatchFieldMerge.Effective(
            Schema,
            batchValues: new Dictionary<string, string?> { ["Box"] = "13" },
            documentValues: new Dictionary<string, string?> { ["Box"] = "12", ["Title"] = "Deed" });

        Assert.Equal("13", merged["Box"]);
    }

    [Fact]
    public void Group_value_for_a_row_scoped_field_is_ignored()
    {
        var merged = BatchFieldMerge.Effective(
            Schema,
            batchValues: new Dictionary<string, string?> { ["Title"] = "wrong" },
            documentValues: new Dictionary<string, string?> { ["Title"] = "Deed" });

        Assert.Equal("Deed", merged["Title"]);
    }

    [Fact]
    public void A_batch_field_with_no_group_value_yields_no_entry()
    {
        var merged = BatchFieldMerge.Effective(
            Schema,
            batchValues: new Dictionary<string, string?>(),
            documentValues: new Dictionary<string, string?> { ["Title"] = "Deed" });

        Assert.False(merged.ContainsKey("Box"));
    }

    /// <summary>Only schema fields survive; a value left over from a deleted field is dropped.</summary>
    [Fact]
    public void Values_outside_the_schema_are_dropped()
    {
        var merged = BatchFieldMerge.Effective(
            Schema,
            batchValues: new Dictionary<string, string?> { ["Retired"] = "x" },
            documentValues: new Dictionary<string, string?> { ["AlsoRetired"] = "y" });

        Assert.Empty(merged);
    }

    [Fact]
    public void Field_names_match_case_insensitively()
    {
        var merged = BatchFieldMerge.Effective(
            Schema,
            batchValues: new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase) { ["box"] = "12" },
            documentValues: new Dictionary<string, string?>());

        Assert.Equal("12", merged["Box"]);
    }
}
```

- [ ] **Step 2: Run the tests and confirm they fail**

Run: `dotnet test tests/FgScanner.Core.Tests -c Release`
Expected: FAIL — `FieldScope` does not exist, `BatchFieldMerge` does not exist.

- [ ] **Step 3: Add `FieldScope` and widen `IndexFieldDef`**

In `src/FgScanner.Core/Index/IndexModels.cs`, add the enum next to `IndexFieldType`:

```csharp
/// <summary>
/// Whether a field is answered once per row or once per group. Batch fields exist because the
/// evidence station retyped Box and Operator on every page of a box; sticky only chained a value
/// onto new rows, so the first page still had to be typed and a correction had to be repeated.
/// </summary>
public enum FieldScope
{
    Row,
    Batch,
}
```

Replace the `IndexFieldDef` declaration. The default keeps every existing construction site compiling unchanged:

```csharp
public sealed record IndexFieldDef(
    string Name, IndexFieldType Type, bool Required, FieldScope Scope = FieldScope.Row);
```

- [ ] **Step 4: Write `BatchFieldMerge`**

Create `src/FgScanner.Core/Index/BatchFieldMerge.cs`:

```csharp
namespace FgScanner.Core.Index;

/// <summary>
/// Resolves the values one row shows. A batch-scoped field is answered by the group and never by
/// the row: a value the group owns must not be able to differ per row, and a copy left in a
/// document's JSON by an earlier row-scoped life must not resurface after a correction.
/// </summary>
public static class BatchFieldMerge
{
    public static IReadOnlyDictionary<string, string?> Effective(
        IReadOnlyList<IndexFieldDef> fields,
        IReadOnlyDictionary<string, string?> batchValues,
        IReadOnlyDictionary<string, string?> documentValues)
    {
        var merged = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var field in fields)
        {
            var source = field.Scope == FieldScope.Batch ? batchValues : documentValues;
            if (source.TryGetValue(field.Name, out var value))
            {
                merged[field.Name] = value;
            }
        }

        return merged;
    }
}
```

- [ ] **Step 5: Run the tests and confirm they pass**

Run: `dotnet test tests/FgScanner.Core.Tests -c Release`
Expected: PASS, all six.

- [ ] **Step 6: Format and commit**

```bash
dotnet format --verify-no-changes
git add src/FgScanner.Core/Index/IndexModels.cs src/FgScanner.Core/Index/BatchFieldMerge.cs tests/FgScanner.Core.Tests/BatchFieldMergeTests.cs
git commit -m "Let a field be answered by the group instead of the row"
```

---

### Task 2: Database columns and migration

Three columns in one migration. Nothing reads them yet.

**Files:**
- Modify: `src/FgScanner.Data/Entities.cs`
- Modify: `src/FgScanner.Data/RawSchemaSql.cs` (the `v_index` view)
- Create: `src/FgScanner.Data/Migrations/<timestamp>_AddFieldScopeAndGroupBatchFields.cs` (generated)
- Modify: `docs/db-schema.md` (regenerated, not hand-edited)
- Test: `tests/FgScanner.Data.Tests/BatchFieldColumnsTests.cs`

**Interfaces:**
- Consumes: `FieldScope` from Task 1.
- Produces: `FieldDefinition.Scope` (`FieldScope`, default `Row`); `Group.BatchFieldsJson` (`string`, default `"{}"`); `Page.CapturedBy` (`string?`, default null).

- [ ] **Step 1: Write the failing test**

Create `tests/FgScanner.Data.Tests/BatchFieldColumnsTests.cs`. Follow the existing fixture pattern in `tests/FgScanner.Data.Tests/IndexingServiceTests.cs` for building a temp database — read that file first and mirror how it constructs its `IDbContextFactory`.

```csharp
using FgScanner.Core.Index;
using FgScanner.Data;
using Xunit;

namespace FgScanner.Data.Tests;

public class BatchFieldColumnsTests
{
    /// <summary>
    /// Every field that existed before this phase must migrate as Row, or a schema the operator
    /// never touched would start answering from the group.
    /// </summary>
    [Fact]
    public void FieldDefinition_defaults_to_row_scope()
    {
        var field = new FieldDefinition { Name = "Title" };

        Assert.Equal(FieldScope.Row, field.Scope);
    }

    [Fact]
    public void Group_starts_with_an_empty_batch_bag()
    {
        var group = new Group { Name = "g", DirectoryPath = "d" };

        Assert.Equal("{}", group.BatchFieldsJson);
    }

    /// <summary>Null is "unknown provenance" and must stay distinguishable from an empty string.</summary>
    [Fact]
    public void Page_captured_by_starts_null()
    {
        var page = new Page { FileName = "a.jpg", Checksum = "abc" };

        Assert.Null(page.CapturedBy);
    }
}
```

- [ ] **Step 2: Run the test and confirm it fails**

Run: `dotnet test tests/FgScanner.Data.Tests -c Release`
Expected: FAIL — `Scope`, `BatchFieldsJson`, `CapturedBy` do not exist.

- [ ] **Step 3: Add the three properties**

In `src/FgScanner.Data/Entities.cs`, add to `FieldDefinition` (after `Sticky`):

```csharp
    /// <summary>
    /// Row (the value is this document's) or Batch (the value is the group's, stamped onto every
    /// row). Entities already reach into Core for BlankPagePolicy, so the enum is not duplicated.
    /// </summary>
    public FgScanner.Core.Index.FieldScope Scope { get; set; }
```

Add to `Group` (after `SchemaVersion`):

```csharp
    /// <summary>
    /// Values for this group's batch-scoped fields, keyed by field name — deliberately the same
    /// shape as Document.CustomFieldsJson so one helper reads both. This is the only place a
    /// batch value lives; rows hold no copy that could drift from it.
    /// </summary>
    public string BatchFieldsJson { get; set; } = "{}";
```

Add to `Page` (after `OriginalChecksum`):

```csharp
    /// <summary>
    /// The Windows account whose session captured this page, recorded at capture and never
    /// editable. Null means unknown provenance — retro-processed files were scanned elsewhere,
    /// and naming the current user as their captor would be a fabrication.
    /// </summary>
    public string? CapturedBy { get; set; }
```

- [ ] **Step 4: Generate the migration**

```bash
dotnet ef migrations add AddFieldScopeAndGroupBatchFields --project src/FgScanner.Data
```

If `dotnet ef` is not installed: `dotnet tool install --global dotnet-ef`.

Open the generated migration and confirm it contains exactly three `AddColumn` calls — `FieldDefinitions.Scope` (INTEGER, default 0), `Groups.BatchFieldsJson` (TEXT, default `"{}"`), `Pages.CapturedBy` (TEXT, nullable). If it contains anything else, the model has drifted; stop and investigate rather than editing the migration by hand.

- [ ] **Step 5: Extend the `v_index` view**

`docs/db-schema.md` tells external tools to query the `v_*` views. After this phase `d.CustomFieldsJson` alone no longer holds `Box` or `Operator`, so the view would silently mislead. In `src/FgScanner.Data/RawSchemaSql.cs`, add one line to `CreateViewIndex`, immediately after the `CustomFields` line:

```sql
          g.BatchFieldsJson   AS BatchFields,
```

Leave `v_pages` alone for `CapturedBy` — phase 17 added `OriginalChecksum` without extending it, and this follows that precedent.

- [ ] **Step 6: Run tests and regenerate the schema doc**

```bash
dotnet test tests/FgScanner.Data.Tests -c Release
```
Expected: PASS.

Then regenerate the committed schema doc (a test compares it, so it will fail until you do):
```bash
FGSCANNER_UPDATE_SCHEMA_DOC=1 dotnet test tests/FgScanner.Data.Tests -c Release
```
On PowerShell: `$env:FGSCANNER_UPDATE_SCHEMA_DOC=1; dotnet test tests/FgScanner.Data.Tests -c Release`

Re-run without the variable and confirm green.

- [ ] **Step 7: Format and commit**

```bash
dotnet format --verify-no-changes
git add src/FgScanner.Data tests/FgScanner.Data.Tests/BatchFieldColumnsTests.cs docs/db-schema.md
git commit -m "Store a field's scope, a group's batch values, and a page's captor"
```

---

### Task 3: Seed a group's batch values, and stamp them onto every exported row

Two halves of one deliverable: a batch field's default seeds the group's bag when the group is created, and export answers every row from that bag.

**Files:**
- Modify: `src/FgScanner.Data/GroupService.cs` (`AdoptDirectoryAsync` — where a group is created with a profile)
- Modify: `src/FgScanner.Data/IndexingService.cs` (`BuildExportDataAsync`, ~line 262)
- Modify: `src/FgScanner.Core/Index/Writers.cs` (`ManifestBuilder.Build`)
- Test: `tests/FgScanner.Data.Tests/BatchFieldExportTests.cs`

**Interfaces:**
- Consumes: `BatchFieldMerge.Effective` (Task 1); `Group.BatchFieldsJson`, `FieldDefinition.Scope` (Task 2).
- Produces: exported `IndexRow.CustomValues` carrying batch values on every row; `manifest.json` field entries carrying `scope`; `Group.BatchFieldsJson` seeded from batch defaults at creation.

- [ ] **Step 1: Write the failing tests**

Create `tests/FgScanner.Data.Tests/BatchFieldExportTests.cs`. This mirrors the fixture in `IndexingServiceTests.cs` — `TestDb`, `TestContext.Current.CancellationToken`, and services constructed from `_db.Factory`.

```csharp
using System.Text.Json;
using FgScanner.Core.Index;
using FgScanner.Data;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FgScanner.Data.Tests;

public sealed class BatchFieldExportTests : IDisposable
{
    private readonly TestDb _db = new();
    private readonly GroupService _groups;
    private readonly ProfileService _profiles;
    private readonly IndexingService _indexing;
    private readonly string _groupsRoot;

    public BatchFieldExportTests()
    {
        _groups = new GroupService(_db.Factory);
        _profiles = new ProfileService(_db.Factory);
        _indexing = new IndexingService(_db.Factory, _profiles, new IndexExporter());
        _groupsRoot = Path.Combine(_db.Root, "groups");
        Directory.CreateDirectory(_groupsRoot);
    }

    public void Dispose() => _db.Dispose();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<Group> ArrangeAsync(int pages = 3)
    {
        var profile = await _profiles.CreateAsync("Evidence", Ct);
        var schema = await _profiles.SaveSchemaAsync(profile.Id,
        [
            new FieldDefinition { Name = "Box", Type = FieldType.Text, Required = true, Scope = FieldScope.Batch },
            new FieldDefinition { Name = "Title", Type = FieldType.Text },
        ], Ct);
        await _profiles.UpdateExportSettingsAsync(profile.Id, csv: true, xlsx: false, xml: false, json: true, ",", Ct);

        var group = await _groups.CreateGroupAsync(_groupsRoot, "Box12", (profile.Id, schema.Version), Ct);
        var incoming = Path.Combine(_db.Root, "incoming-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(incoming);
        var files = new List<string>();
        for (var i = 1; i <= pages; i++)
        {
            var f = Path.Combine(incoming, $"p{i}.png");
            await File.WriteAllBytesAsync(f, [(byte)i, 0xFF], Ct);
            files.Add(f);
        }

        await _groups.AdoptFilesAsync(group.Id, files, Ct);
        return group;
    }

    private async Task SetBatchValueAsync(Guid groupId, string box)
    {
        await using var db = await _db.Factory.CreateDbContextAsync(Ct);
        var group = await db.Groups.FirstAsync(g => g.Id == groupId, Ct);
        group.BatchFieldsJson = JsonSerializer.Serialize(new Dictionary<string, string?> { ["Box"] = box });
        await db.SaveChangesAsync(Ct);
    }

    [Fact]
    public async Task A_batch_value_appears_on_every_row()
    {
        var group = await ArrangeAsync();
        await SetBatchValueAsync(group.Id, "12");

        var data = await _indexing.BuildExportDataAsync(group.Id, Ct);

        Assert.NotEmpty(data.Rows);
        Assert.All(data.Rows, r => Assert.Equal("12", r.CustomValues["Box"]));
    }

    /// <summary>
    /// The correction the whole design exists for: one edit, every row, and no per-row write —
    /// so no row can be left behind holding the old number.
    /// </summary>
    [Fact]
    public async Task Correcting_the_group_value_changes_every_row()
    {
        var group = await ArrangeAsync();
        await SetBatchValueAsync(group.Id, "12");
        await SetBatchValueAsync(group.Id, "13");

        var data = await _indexing.BuildExportDataAsync(group.Id, Ct);

        Assert.All(data.Rows, r => Assert.Equal("13", r.CustomValues["Box"]));

        await using var db = await _db.Factory.CreateDbContextAsync(Ct);
        var stored = await db.Documents.Where(d => d.GroupId == group.Id)
            .Select(d => d.CustomFieldsJson).ToListAsync(Ct);
        Assert.All(stored, json => Assert.DoesNotContain("Box", json, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Manifest_records_each_fields_scope()
    {
        var group = await ArrangeAsync();
        await SetBatchValueAsync(group.Id, "12");

        var data = await _indexing.BuildExportDataAsync(group.Id, Ct);
        var json = IndexPayload.ToJson(data);

        Assert.Contains("\"scope\": \"batch\"", json, StringComparison.Ordinal);
        Assert.Contains("\"scope\": \"row\"", json, StringComparison.Ordinal);
    }

    /// <summary>
    /// Operator's $(user) default has to land somewhere, and a batch field is answered once per
    /// group — so the default seeds the group's bag at creation rather than each row.
    /// </summary>
    [Fact]
    public async Task A_batch_defaults_value_seeds_the_group_at_creation()
    {
        var profile = await _profiles.CreateAsync("Evidence", Ct);
        var schema = await _profiles.SaveSchemaAsync(profile.Id,
        [
            new FieldDefinition
            {
                Name = "Operator", Type = FieldType.Text,
                Scope = FieldScope.Batch, DefaultValue = "$(user)",
            },
        ], Ct);

        var group = await _groups.CreateGroupAsync(_groupsRoot, "Seeded", (profile.Id, schema.Version), Ct);

        await using var db = await _db.Factory.CreateDbContextAsync(Ct);
        var stored = await db.Groups.Where(g => g.Id == group.Id)
            .Select(g => g.BatchFieldsJson).FirstAsync(Ct);
        var values = JsonSerializer.Deserialize<Dictionary<string, string?>>(stored)!;
        Assert.Equal(Environment.UserName, values["Operator"]);
    }
}
```

Check `AdoptFilesAsync`'s exact name and signature in `GroupService.cs` before running — mirror whatever `IndexingServiceTests.CreateGroupWithPagesAsync` calls.

- [ ] **Step 2: Run the tests and confirm they fail**

Run: `dotnet test tests/FgScanner.Data.Tests -c Release`
Expected: FAIL — batch values are absent from rows; the manifest has no `scope`; the group's bag is empty.

- [ ] **Step 2b: Seed batch defaults when a group is created**

In `src/FgScanner.Data/GroupService.cs`, in `AdoptDirectoryAsync`, after the group is created with a profile and schema version, seed the bag from the schema's batch fields:

```csharp
        // A batch field is answered once per group, so its default belongs to the group and is
        // expanded here — not per row, where $(user) would be re-evaluated on every page.
        var batchDefaults = new Dictionary<string, string?>();
        foreach (var field in schema.Fields.Where(f =>
            f.Scope == FgScanner.Core.Index.FieldScope.Batch && !string.IsNullOrEmpty(f.DefaultValue)))
        {
            batchDefaults[field.Name] = TokenExpander.Expand(field.DefaultValue!, group.Name, counter: 1);
        }

        if (batchDefaults.Count > 0)
        {
            group.BatchFieldsJson = JsonSerializer.Serialize(batchDefaults);
        }
```

`AdoptDirectoryAsync` does not load the schema today. Load it through the injected `ProfileService` if one is available; if `GroupService` has no `ProfileService` dependency, read the schema's fields directly from the `DbContext` rather than adding a constructor parameter — `GroupService` is constructed in several places and widening it is a larger change than this phase needs.

- [ ] **Step 2c: Stop `ApplyDefaults` writing batch fields into documents**

`ApplyDefaultsAsync` (`IndexingService.cs`, ~line 196) loops **every** schema field and writes the resolved value into each document's `CustomFieldsJson`. A batch field must not travel that path: its value is the group's, seeded once in Step 2b, and a per-document copy is exactly what the spec's "rows store no copy that can drift" forbids.

Skip batch fields in that loop:

```csharp
            foreach (var field in schema.Fields.Where(f => f.Scope != FgScanner.Core.Index.FieldScope.Batch))
```

This is the second of two write paths that would otherwise duplicate a batch value into rows; Task 9 Step 5 closes the other (`PersistRowAsync`). The `Correcting_the_group_value_changes_every_row` test above asserts documents never carry a `Box` key, and pins this.

- [ ] **Step 3: Carry scope into `IndexFieldDef`**

In `BuildExportDataAsync`, the fields projection currently drops scope. Change it:

```csharp
            fields = [.. schema.Fields.Select(f => new IndexFieldDef(f.Name, (IndexFieldType)f.Type, f.Required, f.Scope))];
```

- [ ] **Step 4: Merge the group's batch values into each row**

Still in `BuildExportDataAsync`, before the `foreach` over documents, deserialize the group's bag once:

```csharp
        var batchValues = JsonSerializer.Deserialize<Dictionary<string, string?>>(group.BatchFieldsJson) ?? [];
```

Then replace the `CustomValues` argument in the `new IndexRow(...)` call. It currently reads:

```csharp
                JsonSerializer.Deserialize<Dictionary<string, string?>>(doc.CustomFieldsJson) ?? [],
```

Replace with:

```csharp
                BatchFieldMerge.Effective(
                    fields,
                    batchValues,
                    JsonSerializer.Deserialize<Dictionary<string, string?>>(doc.CustomFieldsJson) ?? []),
```

- [ ] **Step 5: Emit `scope` in the manifest**

In `src/FgScanner.Core/Index/Writers.cs`, `ManifestBuilder.Build`, the `Fields` projection becomes:

```csharp
        Fields = data.Fields.Select(f => new
        {
            f.Name,
            Type = f.Type.ToString().ToLowerInvariant(),
            f.Required,
            Scope = f.Scope.ToString().ToLowerInvariant(),
        }),
```

- [ ] **Step 6: Run the tests and confirm they pass**

Run: `dotnet test tests/FgScanner.Data.Tests -c Release`
Expected: PASS.

- [ ] **Step 7: Re-approve the affected Verify snapshots**

The manifest gained a key, so snapshot tests that cover `index.json` or `manifest.json` now fail with a `.received.` file beside each `.verified.`. Inspect **every** diff before accepting — confirm the only change is the added `scope`, then rename `.received.` over `.verified.` (or use your Verify diff tool's accept action).

Run the full suite and confirm green: `dotnet test -c Release`

- [ ] **Step 8: Format and commit**

```bash
dotnet format --verify-no-changes
git add -A
git commit -m "Stamp a group's batch values onto every exported row"
```

---

### Task 4: Record who captured each page

**Files:**
- Modify: `src/FgScanner.Data/GroupService.cs:319`
- Modify: `src/FgScanner.Data/IndexingService.cs:427`
- Modify: `src/FgScanner.Core/Index/IndexModels.cs` (`IndexRow`)
- Modify: `src/FgScanner.Core/Index/IndexPayload.cs`
- Modify: `src/FgScanner.Data/IndexingService.cs` (`BuildExportDataAsync` row construction)
- Test: `tests/FgScanner.Data.Tests/CapturedByTests.cs`

**Interfaces:**
- Consumes: `Page.CapturedBy` (Task 2).
- Produces: `IndexRow.CapturedBy` (`string?`, optional trailing parameter); `index.json` row key `capturedBy`.

- [ ] **Step 1: Write the failing tests**

Create `tests/FgScanner.Data.Tests/CapturedByTests.cs`:

```csharp
using FgScanner.Core.Index;
using FgScanner.Data;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FgScanner.Data.Tests;

public sealed class CapturedByTests : IDisposable
{
    private readonly TestDb _db = new();
    private readonly GroupService _groups;
    private readonly ProfileService _profiles;
    private readonly IndexingService _indexing;
    private readonly string _groupsRoot;

    public CapturedByTests()
    {
        _groups = new GroupService(_db.Factory);
        _profiles = new ProfileService(_db.Factory);
        _indexing = new IndexingService(_db.Factory, _profiles, new IndexExporter());
        _groupsRoot = Path.Combine(_db.Root, "groups");
        Directory.CreateDirectory(_groupsRoot);
    }

    public void Dispose() => _db.Dispose();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<string> MakeImageAsync(string name)
    {
        var incoming = Path.Combine(_db.Root, "incoming-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(incoming);
        var path = Path.Combine(incoming, name);
        await File.WriteAllBytesAsync(path, [0x01, 0xFF], Ct);
        return path;
    }

    [Fact]
    public async Task An_adopted_page_records_the_current_user()
    {
        var group = await _groups.CreateGroupAsync(_groupsRoot, "Captured", null, Ct);
        await _groups.AdoptFilesAsync(group.Id, [await MakeImageAsync("p1.png")], Ct);

        await using var db = await _db.Factory.CreateDbContextAsync(Ct);
        var captured = await db.Pages
            .Where(p => p.Document!.GroupId == group.Id)
            .Select(p => p.CapturedBy)
            .ToListAsync(Ct);

        Assert.All(captured, c => Assert.Equal(Environment.UserName, c));
    }

    /// <summary>
    /// Retro-processing adopts files scanned elsewhere, possibly years ago on another machine.
    /// Naming whoever ran the import as their captor would be a fabrication, and on an evidence
    /// station a fabricated provenance is worse than an absent one.
    /// </summary>
    [Fact]
    public async Task A_retro_processed_page_records_no_captor()
    {
        // Arrange through RetroProcessService exactly as the existing retro tests in this project
        // do — copy their setup rather than inventing one — then assert:
        await using var db = await _db.Factory.CreateDbContextAsync(Ct);
        var captured = await db.Pages.Select(p => p.CapturedBy).ToListAsync(Ct);

        Assert.All(captured, Assert.Null);
    }

    [Fact]
    public async Task The_json_row_carries_captured_by()
    {
        var group = await _groups.CreateGroupAsync(_groupsRoot, "Captured", null, Ct);
        await _groups.AdoptFilesAsync(group.Id, [await MakeImageAsync("p1.png")], Ct);

        var json = IndexPayload.ToJson(await _indexing.BuildExportDataAsync(group.Id, Ct));

        Assert.Contains("\"capturedBy\"", json, StringComparison.Ordinal);
        Assert.Contains(Environment.UserName, json, StringComparison.Ordinal);
    }
}
```

For the retro-process case, copy the arrange block from the existing retro tests in `tests/FgScanner.Data.Tests` rather than writing a new one — that service has a directory-scanning setup worth reusing verbatim.

- [ ] **Step 2: Run the tests and confirm they fail**

Run: `dotnet test tests/FgScanner.Data.Tests -c Release`
Expected: FAIL — `CapturedBy` is never set; `IndexRow` has no such member.

- [ ] **Step 3: Stamp at the two genuine capture sites**

In `src/FgScanner.Data/GroupService.cs`, the `new Page { ... }` at line 319 gains one initializer line:

```csharp
                CapturedBy = Environment.UserName,
```

In `src/FgScanner.Data/IndexingService.cs`, the `db.Pages.Add(new Page { ... })` at line 427 gains the same line.

**Do not touch `RetroProcessService.cs:231`.** Its page must keep `CapturedBy` null, and the test above pins that.

- [ ] **Step 4: Widen `IndexRow` and the JSON payload**

In `src/FgScanner.Core/Index/IndexModels.cs`, add a trailing optional parameter to `IndexRow`, after `OriginalChecksum`:

```csharp
    string? CapturedBy = null);
```

In `src/FgScanner.Core/Index/IndexPayload.cs`, add one line to the row projection, immediately after `r.OriginalChecksum`:

```csharp
            r.CapturedBy,
```

JSON only — do not add it to the CSV, XLSX or XML writers. It joins `sequence`/`pageId`/`checksum`/`isBlank`/`originalChecksum` as a machine fact the human-facing formats deliberately omit.

- [ ] **Step 5: Pass it through the export projection**

In `BuildExportDataAsync`, add `page.CapturedBy` as the final argument of the `new IndexRow(...)` call, after `page.OriginalChecksum`.

- [ ] **Step 6: Run the tests and confirm they pass**

Run: `dotnet test tests/FgScanner.Data.Tests -c Release`
Expected: PASS.

- [ ] **Step 7: Update the webhook payload test and re-approve snapshots**

`tests/FgScanner.Core.Tests/CommitHookServiceTests.cs` asserts the webhook body's shape — the same test that needed updating when phase 16 added a row. The payload now carries `capturedBy`; update the expectation.

Re-approve any Verify snapshots covering `index.json`, inspecting each diff to confirm `capturedBy` is the only change.

Run: `dotnet test -c Release` — expect green.

- [ ] **Step 8: Format and commit**

```bash
dotnet format --verify-no-changes
git add -A
git commit -m "Record who captured each page, and no one for pages we did not capture"
```

---

### Task 5: Validate a required batch field once per group

**Files:**
- Modify: `src/FgScanner.Data/IndexingService.cs` (`GroupValidation` record at line 9, `ValidateAsync` at ~line 224)
- Modify: `src/FgScanner.App/Views/GroupDetailViewModel.cs` (wherever `GroupValidation` is consumed — grep for `ValidateAsync`)
- Test: `tests/FgScanner.Data.Tests/BatchValidationTests.cs`

**Interfaces:**
- Consumes: `BatchFieldMerge` (Task 1), `Group.BatchFieldsJson` and `FieldDefinition.Scope` (Task 2).
- Produces: `GroupValidation(IReadOnlyList<DocumentValidation> Documents, IReadOnlyList<string> GroupErrors)` — `HasErrors` and `ErrorCount` account for both lists.

- [ ] **Step 1: Write the failing test**

Create `tests/FgScanner.Data.Tests/BatchValidationTests.cs`:

Reuse the `ArrangeAsync` / `SetBatchValueAsync` fixture written in Task 3 — copy it into this class rather than sharing it, following how the Data test classes each own their setup.

```csharp
using System.Text.Json;
using FgScanner.Core.Index;
using FgScanner.Data;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FgScanner.Data.Tests;

public sealed class BatchValidationTests : IDisposable
{
    // Fixture identical to BatchFieldExportTests: TestDb, GroupService, ProfileService,
    // IndexingService, _groupsRoot, Ct, ArrangeAsync(pages), SetBatchValueAsync(groupId, box).

    /// <summary>
    /// A missing Box used to produce one identical error per row — two hundred of them for a
    /// two-hundred-page box, none of which told the operator anything the first did not.
    /// </summary>
    [Fact]
    public async Task A_missing_required_batch_value_is_one_error_not_one_per_row()
    {
        var group = await ArrangeAsync(pages: 3);

        var validation = await _indexing.ValidateAsync(group.Id, Ct);

        Assert.Single(validation.GroupErrors);
        Assert.Contains("Box", validation.GroupErrors[0], StringComparison.Ordinal);
        Assert.All(validation.Documents, d => Assert.Empty(d.Errors));
    }

    [Fact]
    public async Task A_present_batch_value_satisfies_every_row()
    {
        var group = await ArrangeAsync(pages: 3);
        await SetBatchValueAsync(group.Id, "12");

        var validation = await _indexing.ValidateAsync(group.Id, Ct);

        Assert.False(validation.HasErrors);
    }

    /// <summary>Row-scoped fields keep reporting per row; only batch scope moves to the group.</summary>
    [Fact]
    public async Task Row_scoped_required_fields_still_report_per_row()
    {
        var profile = await _profiles.CreateAsync("Evidence", Ct);
        var schema = await _profiles.SaveSchemaAsync(profile.Id,
        [
            new FieldDefinition { Name = "Title", Type = FieldType.Text, Required = true },
        ], Ct);
        var group = await _groups.CreateGroupAsync(_groupsRoot, "Rows", (profile.Id, schema.Version), Ct);

        var incoming = Path.Combine(_db.Root, "incoming-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(incoming);
        var files = new List<string>();
        for (var i = 1; i <= 2; i++)
        {
            var f = Path.Combine(incoming, $"p{i}.png");
            await File.WriteAllBytesAsync(f, [(byte)i, 0xFF], Ct);
            files.Add(f);
        }

        await _groups.AdoptFilesAsync(group.Id, files, Ct);

        var validation = await _indexing.ValidateAsync(group.Id, Ct);

        Assert.Empty(validation.GroupErrors);
        Assert.Equal(2, validation.Documents.Count(d => d.Errors.Count > 0));
    }
}
```

- [ ] **Step 2: Run the test and confirm it fails**

Run: `dotnet test tests/FgScanner.Data.Tests -c Release`
Expected: FAIL — `GroupValidation` has no `GroupErrors`.

- [ ] **Step 3: Widen `GroupValidation`**

In `src/FgScanner.Data/IndexingService.cs`, replace the record at line 9:

```csharp
public sealed record GroupValidation(
    IReadOnlyList<DocumentValidation> Documents,
    IReadOnlyList<string> GroupErrors)
{
    public bool HasErrors => GroupErrors.Count > 0 || Documents.Any(d => d.Errors.Count > 0);

    public int ErrorCount => GroupErrors.Count + Documents.Sum(d => d.Errors.Count);
}
```

- [ ] **Step 4: Split validation by scope**

In `ValidateAsync`, the early return for a profile-less group becomes:

```csharp
            return new GroupValidation([.. documents.Select(d =>
                new DocumentValidation(d.Doc.Id, d.ImageName, []))], []);
```

After the schema is loaded, validate batch fields once against the group's bag, and skip them in the per-document loop:

```csharp
        var batchValues = JsonSerializer.Deserialize<Dictionary<string, string?>>(group.BatchFieldsJson) ?? [];
        var groupErrors = new List<string>();
        foreach (var field in schema.Fields.Where(f => f.Scope == FgScanner.Core.Index.FieldScope.Batch))
        {
            var error = FieldValidator.Validate(
                new IndexFieldDef(field.Name, (IndexFieldType)field.Type, field.Required, field.Scope),
                batchValues.GetValueOrDefault(field.Name),
                ParseChoices(field.ListChoicesJson));
            if (error is not null)
            {
                groupErrors.Add(error);
            }
        }
```

In the existing per-document loop, change the field enumeration so batch fields are not re-checked:

```csharp
            foreach (var field in schema.Fields.Where(f => f.Scope == FgScanner.Core.Index.FieldScope.Row))
```

And return both lists:

```csharp
        return new GroupValidation(results, groupErrors);
```

- [ ] **Step 5: Follow the change into the App**

Grep for `ValidateAsync` and `GroupValidation` under `src/FgScanner.App`. Every construction site needs the second argument, and the surface that lists validation problems to the operator must show `GroupErrors` alongside the per-row ones — a group-level problem that appears nowhere is worse than the two hundred duplicates it replaced.

- [ ] **Step 6: Run the tests and confirm they pass**

Run: `dotnet test -c Release`
Expected: PASS.

- [ ] **Step 7: Format and commit**

```bash
dotnet format --verify-no-changes
git add -A
git commit -m "Report a missing batch value once, not once per page"
```

---

### Task 6: Make batch values findable

**Files:**
- Modify: `src/FgScanner.Data/SearchService.cs` (`FieldAndAiSearchAsync` at ~line 104, `FieldSnippet` at ~line 164)
- Test: `tests/FgScanner.Data.Tests/BatchSearchTests.cs`

**Interfaces:**
- Consumes: `Group.BatchFieldsJson` (Task 2).
- Produces: nothing later tasks depend on.

- [ ] **Step 1: Write the failing test**

Create `tests/FgScanner.Data.Tests/BatchSearchTests.cs`. Mirror the existing search-test fixture in that project.

Reuse the Task 3 fixture, plus a `SearchService` built from `_db.Factory`. Check the existing search tests in this project for how `SearchService` is constructed and what its search method is named — mirror it exactly.

```csharp
using FgScanner.Core.Index;
using FgScanner.Data;
using Xunit;

namespace FgScanner.Data.Tests;

public sealed class BatchSearchTests : IDisposable
{
    // Fixture as in BatchFieldExportTests, plus:
    //   private readonly SearchService _search;   // constructed from _db.Factory

    [Fact]
    public async Task A_batch_value_is_findable()
    {
        var group = await ArrangeAsync(pages: 1);
        await SetBatchValueAsync(group.Id, "12");

        var hits = await _search.SearchAsync("12", limit: 50, groupId: null, Ct);

        Assert.Contains(hits, h => h.GroupId == group.Id && h.Source == "Fields");
    }

    /// <summary>
    /// Field search is a LIKE over live rows, not an index, so a correction is searchable at
    /// once. This pins that there is no re-index step to forget.
    /// </summary>
    [Fact]
    public async Task A_corrected_batch_value_is_findable_immediately()
    {
        var group = await ArrangeAsync(pages: 1);
        await SetBatchValueAsync(group.Id, "12");
        await SetBatchValueAsync(group.Id, "13");

        Assert.Contains(await _search.SearchAsync("13", 50, null, Ct), h => h.GroupId == group.Id);
        Assert.DoesNotContain(await _search.SearchAsync("12", 50, null, Ct), h => h.GroupId == group.Id);
    }
}
```

Adjust `SearchAsync`'s name and parameter order to the real signature before running.

- [ ] **Step 2: Run the test and confirm it fails**

Run: `dotnet test tests/FgScanner.Data.Tests -c Release`
Expected: FAIL — nothing matches the group's batch bag.

- [ ] **Step 3: Widen the query**

In `FieldAndAiSearchAsync`, the `Where` clause at line 111 currently matches the document JSON and the AI description. Add the group's bag:

```csharp
            .Where(p => EF.Functions.Like(p.Document!.CustomFieldsJson, pattern, "\\")
                || EF.Functions.Like(p.Document!.Group!.BatchFieldsJson, pattern, "\\")
                || (p.AiDescription != null && EF.Functions.Like(p.AiDescription, pattern, "\\")))
```

Add the bag to the projection so the snippet can use it, alongside `p.Document!.CustomFieldsJson`:

```csharp
                BatchFieldsJson = p.Document!.Group!.BatchFieldsJson,
```

- [ ] **Step 4: Build the snippet from both bags**

`FieldSnippet` takes one JSON string and returns `(string Snippet, string Source)?`. Call it for the document's bag, then the group's, preferring a document hit. Replace the `if (FieldSnippet(page.CustomFieldsJson, query) is { } fieldHit)` block with:

```csharp
            var fieldHit = FieldSnippet(page.CustomFieldsJson, query)
                ?? FieldSnippet(page.BatchFieldsJson, query);
            if (fieldHit is { } hit)
            {
                (snippet, source) = hit;
            }
            else if (page.AiDescription is { } ai && ai.Contains(query, StringComparison.OrdinalIgnoreCase))
            {
                snippet = MakeSnippet(ai, query);
                source = "AI";
            }
```

The `?? ` chain works because `FieldSnippet` already returns a nullable tuple, so a miss on the document's bag falls through to the group's.

- [ ] **Step 5: Run the tests and confirm they pass**

Run: `dotnet test tests/FgScanner.Data.Tests -c Release`
Expected: PASS.

- [ ] **Step 6: Format and commit**

```bash
dotnet format --verify-no-changes
git add -A
git commit -m "Find a page by the value its group holds"
```

---

### Task 7: Mark Box and Operator as batch in the Evidence profile

**Files:**
- Modify: `src/FgScanner.Core/Evidence/EvidenceProfile.cs`
- Modify: `src/FgScanner.Data/ProfileService.cs` (`EnsureEvidenceProfileAsync`, `Unchanged`)
- Modify: `tests/FgScanner.Core.Tests/EvidenceProfileTests.cs`
- Modify: `tests/FgScanner.Data.Tests/EvidenceProfileSeedTests.cs`

**Interfaces:**
- Consumes: `FieldScope` (Task 1), `FieldDefinition.Scope` (Task 2).
- Produces: `EvidenceFieldSpec(string Name, IndexFieldType Type, bool Required, bool Sticky, string? DefaultValue = null, IReadOnlyList<string>? ListChoices = null, FieldScope Scope = FieldScope.Row)`.

- [ ] **Step 1: Write the failing tests**

In `tests/FgScanner.Core.Tests/EvidenceProfileTests.cs`, add:

```csharp
    /// <summary>
    /// Box and Operator are constant for a whole box. They were sticky, which still made the
    /// operator type the first page and retype a correction onto every row it had reached.
    /// </summary>
    [Fact]
    public void Box_and_operator_are_the_batch_fields()
    {
        var batch = EvidenceProfile.Fields
            .Where(f => f.Scope == FieldScope.Batch)
            .Select(f => f.Name);

        Assert.Equal(["Operator", "Box"], batch);
    }
```

Note the order: `Operator` precedes `Box` in the field list, and `Where` preserves it.

Update the existing `Authorship_fields_are_sticky_because_a_box_is_one_answer_repeated` — `Operator` and `Box` leave the sticky list:

```csharp
        Assert.Equal(
            ["DocNo", "NoteAuthor", "NoteBasis", "NoteWhen"],
            sticky);
```

In `tests/FgScanner.Data.Tests/EvidenceProfileSeedTests.cs`, extend `Seeding_preserves_the_sticky_and_required_flags` with one assertion inside its existing loop:

```csharp
            Assert.Equal(spec.Scope, seeded[spec.Name].Scope);
```

- [ ] **Step 2: Run the tests and confirm they fail**

Run: `dotnet test -c Release`
Expected: FAIL — `EvidenceFieldSpec` has no `Scope`.

- [ ] **Step 3: Add `Scope` to the spec record**

In `src/FgScanner.Core/Evidence/EvidenceProfile.cs`:

```csharp
public sealed record EvidenceFieldSpec(
    string Name,
    IndexFieldType Type,
    bool Required,
    bool Sticky,
    string? DefaultValue = null,
    IReadOnlyList<string>? ListChoices = null,
    FieldScope Scope = FieldScope.Row);
```

- [ ] **Step 4: Change the two fields**

Replace the `Operator` entry:

```csharp
        // Batch: one operator runs a box. Sticky only chained the value onto new rows, so the
        // first page was still typed by hand and a correction had to be repeated down the box.
        new("Operator", IndexFieldType.Text, Required: false, Sticky: false,
            DefaultValue: "$(user)", Scope: FieldScope.Batch),
```

Replace the `Box` entry:

```csharp
        // Batch: one group is one box, so this is a group-level fact by definition.
        new("Box", IndexFieldType.Text, Required: true, Sticky: false, Scope: FieldScope.Batch),
```

Leave `NoteAuthor`, `NoteBasis`, `NoteWhen` and `DocNo` sticky and row-scoped — they can legitimately differ sheet to sheet, and batch would make them uneditable per row.

- [ ] **Step 5: Carry scope through seeding and change-detection**

In `src/FgScanner.Data/ProfileService.cs`, `EnsureEvidenceProfileAsync`'s `FieldDefinition` projection gains:

```csharp
                Scope = spec.Scope,
```

And `Unchanged` gains one clause — without it, "Build the Evidence profile" would silently decline to apply a scope change when repairing an existing profile:

```csharp
            || field.Scope != submitted[i].Scope
```

- [ ] **Step 6: Run the tests and confirm they pass**

Run: `dotnet test -c Release`
Expected: PASS.

`EvidenceProfileSeedTests.Seeding_twice_does_not_mint_a_second_schema_version` must still pass: pressing the button twice after this change is still a no-op. What *does* change is that the first press after this phase mints one new version, because `Box` and `Operator` genuinely changed. That is correct and intended.

- [ ] **Step 7: Format and commit**

```bash
dotnet format --verify-no-changes
git add -A
git commit -m "Ask for the box number once per box, not once per page"
```

---

### Task 8: `.fgprofile` format version 2

**Files:**
- Modify: `src/FgScanner.Data/ProfileService.cs` (`FgProfileField`, `ExportProfileJsonAsync`, `ImportProfileJsonAsync`)
- Test: `tests/FgScanner.Data.Tests/ProfileImportExportTests.cs`

**Interfaces:**
- Consumes: `FieldDefinition.Scope` (Task 2).
- Produces: `.fgprofile` files at `FormatVersion` 2 carrying `Scope`; version 1 files still readable.

- [ ] **Step 1: Write the failing tests**

Add to `tests/FgScanner.Data.Tests/ProfileImportExportTests.cs`:

```csharp
    [Fact]
    public async Task A_batch_field_round_trips()
    {
        var profile = await _profiles.CreateAsync("Evidence", Ct);
        await _profiles.SaveSchemaAsync(profile.Id,
        [
            new FieldDefinition { Name = "Box", Type = FieldType.Text, Scope = FieldScope.Batch },
        ], Ct);

        var json = await _profiles.ExportProfileJsonAsync(profile.Id, Ct);
        Assert.Contains("\"FormatVersion\": 2", json, StringComparison.Ordinal);

        var imported = await _profiles.ImportProfileJsonAsync(json, Ct);
        var schema = await _profiles.GetLatestSchemaAsync(imported.Id, Ct);

        Assert.Equal(FieldScope.Batch, schema.Fields.Single(f => f.Name == "Box").Scope);
    }

    /// <summary>
    /// Profiles already exported onto the hand-off USB stick are version 1. Refusing them would
    /// strand the operator mid-box with a file that worked yesterday.
    /// </summary>
    [Fact]
    public async Task A_version_1_file_still_imports_with_row_scope()
    {
        const string v1 = """
            {
              "FormatVersion": 1,
              "Name": "Legacy",
              "OcrEnabled": false,
              "ExportCsv": true,
              "ExportXlsx": false,
              "ExportXml": false,
              "ExportJson": false,
              "CsvDelimiter": ",",
              "Fields": [
                { "Name": "Box", "Type": "Text", "Required": true, "Sticky": true,
                  "DefaultValue": null, "ListChoicesJson": null }
              ]
            }
            """;

        var imported = await _profiles.ImportProfileJsonAsync(v1, Ct);
        var schema = await _profiles.GetLatestSchemaAsync(imported.Id, Ct);

        Assert.Equal(FieldScope.Row, schema.Fields.Single().Scope);
    }
```

- [ ] **Step 2: Run the tests and confirm they fail**

Run: `dotnet test tests/FgScanner.Data.Tests -c Release`
Expected: FAIL — export writes version 1; `Scope` is not carried.

- [ ] **Step 3: Add `Scope` to the file record**

An init-property with a default, so version-1 files lacking the member still deserialize — the same pattern the capture-triage properties already use in this record:

```csharp
    private sealed record FgProfileField(
        string Name, string Type, bool Required, bool Sticky, string? DefaultValue, string? ListChoicesJson)
    {
        /// <summary>Init-prop with a default so version-1 files without it still load as row-scoped.</summary>
        public string Scope { get; init; } = nameof(FieldScope.Row);
    }
```

- [ ] **Step 4: Write version 2**

In `ExportProfileJsonAsync`, change the version argument from `1` to `2`, and carry scope in the field projection:

```csharp
            [.. schema.Fields.Select(f => new FgProfileField(
                f.Name, f.Type.ToString(), f.Required, f.Sticky, f.DefaultValue, f.ListChoicesJson)
                { Scope = f.Scope.ToString() })])
```

- [ ] **Step 5: Accept both versions on import**

In `ImportProfileJsonAsync`, replace the version guard:

```csharp
        if (file.FormatVersion is not (1 or 2))
        {
            throw new InvalidOperationException(
                $"Unsupported .fgprofile format version {file.FormatVersion} (this build reads versions 1 and 2).");
        }
```

Then, where the imported fields are mapped into `FieldDefinition`s, parse the scope:

```csharp
                Scope = Enum.TryParse<FieldScope>(f.Scope, ignoreCase: true, out var scope) ? scope : FieldScope.Row,
```

Falling back to `Row` rather than throwing is deliberate: this is a file boundary, and an unrecognised scope from a newer build should degrade to today's behaviour, not refuse the operator's profile.

- [ ] **Step 6: Run the tests and confirm they pass**

Run: `dotnet test tests/FgScanner.Data.Tests -c Release`
Expected: PASS.

- [ ] **Step 7: Format and commit**

```bash
dotnet format --verify-no-changes
git add -A
git commit -m "Carry a field's scope through the profile file"
```

---

### Task 9: Let the operator see and set batch fields

**Files:**
- Modify: `src/FgScanner.App/Views/SettingsViewModel.cs` (`FieldRow`, ~lines 715-745)
- Modify: `src/FgScanner.App/Views/SettingsView.xaml` (the field editor grid)
- Modify: `src/FgScanner.App/Views/GroupDetailViewModel.cs`
- Modify: `src/FgScanner.App/Views/GroupsView.xaml` and `GroupsView.xaml.cs` (`RebuildColumns`, line 274)
- Test: `tests/FgScanner.App.Tests/BatchFieldUiTests.cs`

**Interfaces:**
- Consumes: everything from Tasks 1-3.
- Produces: nothing later tasks depend on.

- [ ] **Step 1: Write the failing tests**

Create `tests/FgScanner.App.Tests/BatchFieldUiTests.cs`. Mirror the view-model test style already in that project (they exercise view models directly, not the WPF visual tree).

`FieldRow` is a top-level public class in the `FgScanner.App.Views` namespace (`SettingsViewModel.cs:728`), not nested inside `SettingsViewModel` — reference it directly.

```csharp
using FgScanner.App.Views;
using FgScanner.Core.Index;
using FgScanner.Data;
using Xunit;

namespace FgScanner.App.Tests;

public class BatchFieldUiTests
{
    /// <summary>
    /// Sticky means "chain this row's value to the next row", which is meaningless for a value
    /// the group owns. Allowing both would let the schema express a contradiction.
    /// </summary>
    [Fact]
    public void Marking_a_field_batch_clears_sticky()
    {
        var row = new FieldRow { Name = "Box", Sticky = true };

        row.Scope = FieldScope.Batch;

        Assert.False(row.Sticky);
    }

    [Fact]
    public void Scope_round_trips_through_the_field_editor()
    {
        var definition = new FieldDefinition { Name = "Box", Scope = FieldScope.Batch };

        var restored = FieldRow.From(definition).ToDefinition();

        Assert.Equal(FieldScope.Batch, restored.Scope);
    }
}
```

- [ ] **Step 2: Run the tests and confirm they fail**

Run: `dotnet test tests/FgScanner.App.Tests -c Release`
Expected: FAIL — `FieldRow` has no `Scope`.

- [ ] **Step 3: Add `Scope` to `FieldRow`**

In `SettingsViewModel.cs`, add an observable property beside `Sticky`, and clear `Sticky` when it turns batch:

```csharp
    [ObservableProperty]
    private FieldScope _scope;

    partial void OnScopeChanged(FieldScope value)
    {
        if (value == FieldScope.Batch)
        {
            Sticky = false;
        }
    }
```

Carry it through both converters:

```csharp
        Scope = field.Scope,
```
in `From`, and:
```csharp
        Scope = Scope,
```
in `ToDefinition`.

- [ ] **Step 4: Add the checkbox**

In `SettingsView.xaml`, add a Batch column to the field editor beside the existing Sticky checkbox, bound to `Scope` through a converter between `FieldScope` and `bool` (`FieldScope.Batch` ⇄ true). Disable the Sticky checkbox when Batch is ticked, so the exclusivity is visible rather than only enforced.

- [ ] **Step 5: Show batch fields once, and lock their columns**

In `GroupDetailViewModel.cs`, expose the group's batch values as an editable collection — one entry per `FieldScope.Batch` field — and persist changes to `Group.BatchFieldsJson`. Follow the existing `PendingFields` / `PendingFieldEditor` pattern in that file; it already does the "one editor per field" job for pre-scan values.

**The grid must show the stamped value, not an empty cell.** Rows are populated from `IndexingService.GetStoredFieldValuesAsync`, which reads `Document.CustomFieldsJson` only — a batch field has no entry there, so without a change every batch cell renders blank. Merge the group's bag in when building each row's `RowValues` (around `GroupDetailViewModel.cs:136-147`), using `BatchFieldMerge.Effective` so the grid and the export agree by construction rather than by coincidence.

**And it must not write them back.** `PersistRowAsync` (~line 557) writes `RowValues` into the document's JSON. Exclude batch-scoped fields from what it persists — otherwise every row accumulates a private copy of the group's value. `BatchFieldMerge` would ignore those copies on read, so this is not a correctness bug, but it contradicts the "rows hold no copy" property the design rests on, and the stale values would be visible to anyone reading the database directly.

In `GroupsView.xaml.cs`, `RebuildColumns` (line 274), the loop over `_detail.Fields` at line 306 builds a `DataGridComboBoxColumn` or `DataGridTextColumn` per field. Set both kinds read-only for batch fields so the operator can see the stamped value but cannot diverge one row from its group:

```csharp
            var isBatch = field.Scope == FieldScope.Batch;
```
then `IsReadOnly = isBatch` on the column before adding it.

- [ ] **Step 6: Add the panel to the view**

In `GroupsView.xaml`, add a Batch fields panel above the entry grid, bound to the collection from Step 5, visible only when the group's schema has at least one batch field.

- [ ] **Step 7: Run the tests and confirm they pass**

Run: `dotnet test -c Release`
Expected: PASS.

- [ ] **Step 8: Run the app and confirm the flow by hand**

```bash
dotnet run --project src/FgScanner.App
```

Press Settings → "Build the Evidence profile", create a group on that profile, and confirm: the Batch panel offers `Box` and `Operator`; `Operator` is pre-filled with your Windows username; typing a `Box` value shows it on every row; the `Box` and `Operator` grid cells cannot be edited.

- [ ] **Step 9: Format and commit**

```bash
dotnet format --verify-no-changes
git add -A
git commit -m "Ask for batch fields once and show them on every row"
```

---

### Task 10: Record the decisions

**Files:**
- Modify: `docs/FEATURE-PARITY.md`
- Create: `docs/adr/0004-field-scope.md`
- Create: `docs/adr/0005-captured-by-is-null-for-adopted-files.md`
- Modify: `CLAUDE.md` (the evidence contract bullet)

**Interfaces:**
- Consumes: the finished behaviour of Tasks 1-9.
- Produces: nothing.

- [ ] **Step 1: Add the parity rows**

In `docs/FEATURE-PARITY.md`, add rows for batch-level fields and per-row captor, marked shipped in this release. Match the existing row format exactly — read a few neighbouring rows first.

- [ ] **Step 2: Write ADR-0004**

Create `docs/adr/0004-field-scope.md` following the shape of `docs/adr/0003-preserve-originals.md` (Status / Context / Decision / Consequences). The context is that `Box` and `Operator` were retyped on every page and sticky could not fix it; the decision is field scope with the value on the group; the consequences include the one-time schema version bump and that pre-existing evidence groups keep row-scoped fields and are deliberately not migrated.

- [ ] **Step 3: Write ADR-0005**

Create `docs/adr/0005-captured-by-is-null-for-adopted-files.md`. The decision is that `CapturedBy` is stamped only where this machine did the capturing, and left null for retro-processed files, because a fabricated provenance is worse than an absent one on an evidence station.

- [ ] **Step 4: Update the contract note**

In `CLAUDE.md`, add `capturedBy` to the listed `index.json` row keys, and note that `manifest.json` field entries now carry `scope`. Do not restate the whole design — link the spec.

- [ ] **Step 5: Final verification**

```bash
dotnet build -c Release
dotnet test -c Release
dotnet format --verify-no-changes
```

All three must be clean. Confirm the test count has grown from the 473 baseline recorded at the start of this phase.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "Record the field-scope and captured-by decisions"
```

---

## Notes for the executor

- **The `IndexRow` positional-record trap.** `IndexRow` has many optional trailing parameters. When adding `CapturedBy` in Task 4, every existing call site passes arguments positionally — confirm you appended and did not insert, or checksums will quietly land in the wrong column.
- **Verify snapshots twice.** Tasks 3 and 4 both change `index.json`. Inspect each `.received.` diff rather than bulk-accepting; the whole point of the snapshots is that a contract change is deliberate.
- **The one-time version bump is expected.** After Task 7, the first "Build the Evidence profile" mints a new schema version. Existing evidence groups keep row-scoped `Box`/`Operator` and nothing migrates them.
- **If the station has already captured a real box**, stop before Task 7 and raise it — the spec assumes no production evidence groups exist yet.
