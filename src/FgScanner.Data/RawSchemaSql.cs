namespace FgScanner.Data;

/// <summary>
/// Raw-SQL schema objects (FTS5, triggers, stable views). Single source of truth: migrations
/// execute these strings and the schema-doc generator prints them, so the two cannot drift.
/// The v_* views are the supported public query surface for external tools (PLAN §5.1).
/// </summary>
public static class RawSchemaSql
{
    /// <summary>FTS5 full-text index over Pages.OcrText (external content — no duplicated storage).</summary>
    public const string CreateFts = """
        CREATE VIRTUAL TABLE PagesFts USING fts5(OcrText, content='Pages', content_rowid='rowid');
        """;

    /// <summary>Triggers keep PagesFts transactional with Pages (standard external-content pattern).</summary>
    public const string CreateFtsTriggers = """
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
        """;

    /// <summary>One row per document: what an index-file row will contain.</summary>
    public const string CreateViewIndex = """
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
        """;

    /// <summary>One row per page with group/document context and pipeline state.</summary>
    public const string CreateViewPages = """
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
        """;

    /// <summary>OCR text per page, for external full-text tooling.</summary>
    public const string CreateViewOcrText = """
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
        """;

    public static readonly IReadOnlyList<(string Name, string Sql)> AllObjects =
    [
        ("PagesFts (FTS5)", CreateFts),
        ("PagesFts triggers", CreateFtsTriggers),
        ("v_index", CreateViewIndex),
        ("v_pages", CreateViewPages),
        ("v_ocr_text", CreateViewOcrText),
    ];
}
