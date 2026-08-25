# FG Scanner — Database Schema

<!-- GENERATED FILE — do not edit by hand. -->
<!-- Regenerate: set FGSCANNER_UPDATE_SCHEMA_DOC=1 and run `dotnet test --project tests/FgScanner.Data.Tests`. -->

The database (`%APPDATA%\FGScanner\fgscanner.db`) is a first-class deliverable: every value the app
produces lives here and can be queried with any SQLite tool. **External tools should query the `v_*`
views**, which are kept stable across internal refactors; tables may change between versions (a backup
is taken automatically before every schema migration).

## Tables

### Documents

| Column | Type | Nullable | Notes |
|---|---|---|---|
| Id | TEXT | no | PK |
| CreatedUtc | TEXT | no |  |
| CustomFieldsJson | TEXT | no |  |
| GroupId | TEXT | no | FK → Groups |
| Sequence | INTEGER | no |  |

- Index: GroupId, Sequence

### FieldDefinitions

| Column | Type | Nullable | Notes |
|---|---|---|---|
| Id | TEXT | no | PK |
| DefaultValue | TEXT | yes |  |
| ListChoicesJson | TEXT | yes |  |
| Name | TEXT | no |  |
| Order | INTEGER | no |  |
| Required | INTEGER | no |  |
| SchemaId | TEXT | no | FK → IndexSchemas |
| Sticky | INTEGER | no |  |
| Type | INTEGER | no | enum: 0=Text, 1=Date, 2=Number, 3=List |

- Index: SchemaId

### Groups

| Column | Type | Nullable | Notes |
|---|---|---|---|
| Id | TEXT | no | PK |
| CreatedUtc | TEXT | no |  |
| DirectoryPath | TEXT | no |  |
| Name | TEXT | no |  |
| ProfileId | TEXT | yes | FK → Profiles |
| SchemaVersion | INTEGER | no |  |
| State | INTEGER | no | enum: 0=Scanning, 1=Indexing, 2=Committed |
| UpdatedUtc | TEXT | no |  |

- Index (unique): DirectoryPath
- Index: ProfileId

### IndexSchemas

| Column | Type | Nullable | Notes |
|---|---|---|---|
| Id | TEXT | no | PK |
| CreatedUtc | TEXT | no |  |
| ProfileId | TEXT | no | FK → Profiles |
| Version | INTEGER | no |  |

- Index (unique): ProfileId, Version

### Jobs

| Column | Type | Nullable | Notes |
|---|---|---|---|
| Id | TEXT | no | PK |
| Attempts | INTEGER | no |  |
| CreatedUtc | TEXT | no |  |
| LastError | TEXT | yes |  |
| PageId | TEXT | no | FK → Pages |
| State | INTEGER | no | enum: 0=Pending, 1=InFlight, 2=Done, 3=Failed, 4=Skipped |
| Type | INTEGER | no | enum: 0=Ocr, 1=AiDescription |
| UpdatedUtc | TEXT | no |  |

- Index: PageId
- Index: State, Type

### Pages

| Column | Type | Nullable | Notes |
|---|---|---|---|
| Id | TEXT | no | PK |
| AiDescription | TEXT | yes |  |
| AiStatus | INTEGER | no | enum: 0=Off, 1=Pending, 2=Done, 3=Failed, 4=Skipped |
| Checksum | TEXT | no |  |
| CreatedUtc | TEXT | no |  |
| DocumentId | TEXT | no | FK → Documents |
| FileName | TEXT | no |  |
| IsBlank | INTEGER | no |  |
| OcrMeanConfidence | REAL | yes |  |
| OcrStatus | INTEGER | no | enum: 0=No, 1=Pending, 2=Yes, 3=Failed |
| OcrText | TEXT | yes |  |
| Sequence | INTEGER | no |  |

- Index: Checksum
- Index: DocumentId

### Profiles

| Column | Type | Nullable | Notes |
|---|---|---|---|
| Id | TEXT | no | PK |
| AiDescriptionEnabled | INTEGER | no |  |
| BaseDirectory | TEXT | no |  |
| BlankPolicy | INTEGER | no | enum: 0=Keep, 1=Drop, 2=Flag, 3=Separator |
| CreatedUtc | TEXT | no |  |
| CsvDelimiter | TEXT | no |  |
| ExportCsv | INTEGER | no |  |
| ExportJson | INTEGER | no |  |
| ExportXlsx | INTEGER | no |  |
| ExportXml | INTEGER | no |  |
| KeepSeparatorPages | INTEGER | no |  |
| Name | TEXT | no |  |
| OcrEnabled | INTEGER | no |  |
| OcrLanguages | TEXT | no |  |
| ScanSettingsJson | TEXT | no |  |
| SeparatorDetectionEnabled | INTEGER | no |  |

- Index (unique): Name

### Settings

| Column | Type | Nullable | Notes |
|---|---|---|---|
| Key | TEXT | no | PK |
| Value | TEXT | no |  |

### TrashItems

| Column | Type | Nullable | Notes |
|---|---|---|---|
| Id | TEXT | no | PK |
| DeletedUtc | TEXT | no |  |
| DocumentSequence | INTEGER | no |  |
| FilesJson | TEXT | no |  |
| GroupDirectoryPath | TEXT | no |  |
| GroupName | TEXT | no |  |
| OriginalGroupId | TEXT | no |  |
| PayloadJson | TEXT | no |  |
| TrashFolderPath | TEXT | no |  |

- Index: DeletedUtc

## Raw-SQL objects (FTS + stable views)

### PagesFts (FTS5)

```sql
CREATE VIRTUAL TABLE PagesFts USING fts5(OcrText, content='Pages', content_rowid='rowid');
```

### PagesFts triggers

```sql
CREATE TRIGGER Pages_fts_insert AFTER INSERT ON Pages BEGIN
  INSERT INTO PagesFts(rowid, OcrText) VALUES (new.rowid, new.OcrText);
END;
CREATE TRIGGER Pages_fts_delete AFTER DELETE ON Pages BEGIN
  INSERT INTO PagesFts(PagesFts, rowid, OcrText) VALUES ('delete', old.rowid, old.OcrText);
END;
CREATE TRIGGER Pages_fts_update AFTER UPDATE OF OcrText ON Pages BEGIN
  INSERT INTO PagesFts(PagesFts, rowid, OcrText) VALUES ('delete', old.rowid, old.OcrText);
  INSERT INTO PagesFts(rowid, OcrText) VALUES (new.rowid, new.OcrText);
END;
```

### v_index

```sql
CREATE VIEW v_index AS
SELECT
  g.Name              AS GroupName,
  g.DirectoryPath     AS GroupDirectory,
  d.Id                AS DocumentId,
  d.Sequence          AS DocumentSequence,
  (SELECT p.FileName FROM Pages p WHERE p.DocumentId = d.Id ORDER BY p.Sequence LIMIT 1)
                      AS FirstImage,
  (SELECT COUNT(*) FROM Pages p WHERE p.DocumentId = d.Id)
                      AS PageCount,
  d.CustomFieldsJson  AS CustomFields,
  d.CreatedUtc        AS CreatedUtc
FROM Documents d
JOIN Groups g ON g.Id = d.GroupId;
```

### v_pages

```sql
CREATE VIEW v_pages AS
SELECT
  g.Name              AS GroupName,
  d.Sequence          AS DocumentSequence,
  p.Id                AS PageId,
  p.FileName          AS FileName,
  p.Sequence          AS PageSequence,
  p.Checksum          AS Checksum,
  p.IsBlank           AS IsBlank,
  p.OcrStatus         AS OcrStatus,
  p.OcrMeanConfidence AS OcrMeanConfidence,
  p.AiStatus          AS AiStatus,
  p.AiDescription     AS AiDescription,
  p.CreatedUtc        AS CreatedUtc
FROM Pages p
JOIN Documents d ON d.Id = p.DocumentId
JOIN Groups g ON g.Id = d.GroupId;
```

### v_ocr_text

```sql
CREATE VIEW v_ocr_text AS
SELECT
  g.Name     AS GroupName,
  p.FileName AS FileName,
  p.Id       AS PageId,
  p.OcrText  AS OcrText
FROM Pages p
JOIN Documents d ON d.Id = p.DocumentId
JOIN Groups g ON g.Id = d.GroupId
WHERE p.OcrText IS NOT NULL;
```
