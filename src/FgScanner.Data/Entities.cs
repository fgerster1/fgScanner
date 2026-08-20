namespace FgScanner.Data;

public enum FieldType
{
    Text,
    Date,
    Number,
    List,
}

public enum GroupState
{
    Scanning,
    Indexing,
    Committed,
}

public enum OcrStatus
{
    No,
    Pending,
    Yes,
    Failed,
}

public enum AiStatus
{
    Off,
    Pending,
    Done,
    Failed,
    Skipped,
}

public enum JobType
{
    Ocr,
    AiDescription,
}

public enum JobState
{
    Pending,
    InFlight,
    Done,
    Failed,
    Skipped,
}

/// <summary>A scan profile: settings + the versioned index schema its groups use.</summary>
public class Profile
{
    public Guid Id { get; set; }

    public required string Name { get; set; }

    /// <summary>Serialized scan settings (device/dpi/source…); structured in phase 4.</summary>
    public string ScanSettingsJson { get; set; } = "{}";

    public bool OcrEnabled { get; set; }

    public string OcrLanguages { get; set; } = "eng";

    public bool AiDescriptionEnabled { get; set; }

    // Index export formats (PLAN §5.2): any combination, CSV on by default.
    public bool ExportCsv { get; set; } = true;

    public bool ExportXlsx { get; set; }

    public bool ExportXml { get; set; }

    public bool ExportJson { get; set; }

    /// <summary>"," default; ";" for European Excel locales.</summary>
    public string CsvDelimiter { get; set; } = ",";

    public DateTime CreatedUtc { get; set; }

    public List<IndexSchema> Schemas { get; set; } = [];
}

/// <summary>One immutable version of a profile's index-field layout. Editing fields creates a new version.</summary>
public class IndexSchema
{
    public Guid Id { get; set; }

    public Guid ProfileId { get; set; }

    public Profile? Profile { get; set; }

    public int Version { get; set; }

    public DateTime CreatedUtc { get; set; }

    public List<FieldDefinition> Fields { get; set; } = [];
}

public class FieldDefinition
{
    public Guid Id { get; set; }

    public Guid SchemaId { get; set; }

    public IndexSchema? Schema { get; set; }

    public int Order { get; set; }

    public required string Name { get; set; }

    public FieldType Type { get; set; }

    public bool Required { get; set; }

    public bool Sticky { get; set; }

    /// <summary>Default value; supports tokens like $(today), $(group), $(counter), $(user).</summary>
    public string? DefaultValue { get; set; }

    /// <summary>JSON array of choices; only for <see cref="FieldType.List"/>.</summary>
    public string? ListChoicesJson { get; set; }
}

/// <summary>A batch tied to a directory; the directory name is the group name (PLAN §5.1).</summary>
public class Group
{
    public Guid Id { get; set; }

    public required string Name { get; set; }

    public required string DirectoryPath { get; set; }

    public GroupState State { get; set; }

    public Guid? ProfileId { get; set; }

    public Profile? Profile { get; set; }

    public int SchemaVersion { get; set; }

    public DateTime CreatedUtc { get; set; }

    public DateTime UpdatedUtc { get; set; }

    public List<Document> Documents { get; set; } = [];
}

/// <summary>The unit an index row describes. v1: one page per document (PLAN decision #2).</summary>
public class Document
{
    public Guid Id { get; set; }

    public Guid GroupId { get; set; }

    public Group? Group { get; set; }

    public int Sequence { get; set; }

    /// <summary>User-defined field values as a JSON object keyed by field name (PLAN §5.1 JSON pattern).</summary>
    public string CustomFieldsJson { get; set; } = "{}";

    public DateTime CreatedUtc { get; set; }

    public List<Page> Pages { get; set; } = [];
}

public class Page
{
    public Guid Id { get; set; }

    public Guid DocumentId { get; set; }

    public Document? Document { get; set; }

    /// <summary>File name relative to the group directory; the checksum, not the name, is identity.</summary>
    public required string FileName { get; set; }

    /// <summary>SHA-256 of file content, lower-case hex. Survives renames; drives duplicate detection.</summary>
    public required string Checksum { get; set; }

    public int Sequence { get; set; }

    public bool IsBlank { get; set; }

    public OcrStatus OcrStatus { get; set; }

    public AiStatus AiStatus { get; set; }

    public double? OcrMeanConfidence { get; set; }

    /// <summary>Plain OCR text; indexed by the PagesFts FTS5 table (phase 5 fills it).</summary>
    public string? OcrText { get; set; }

    /// <summary>AI description (≤1000 chars); phase 6 fills it.</summary>
    public string? AiDescription { get; set; }

    public DateTime CreatedUtc { get; set; }
}

/// <summary>Durable work queue: OCR/AI jobs survive restarts (PLAN §8).</summary>
public class QueuedJob
{
    public Guid Id { get; set; }

    public JobType Type { get; set; }

    public Guid PageId { get; set; }

    public Page? Page { get; set; }

    public JobState State { get; set; }

    public int Attempts { get; set; }

    public string? LastError { get; set; }

    public DateTime CreatedUtc { get; set; }

    public DateTime UpdatedUtc { get; set; }
}

/// <summary>A deleted page (image + sidecars) held restorable for the retention period (PLAN §5.2).</summary>
public class TrashItem
{
    public Guid Id { get; set; }

    public Guid OriginalGroupId { get; set; }

    public required string GroupName { get; set; }

    public required string GroupDirectoryPath { get; set; }

    public int DocumentSequence { get; set; }

    /// <summary>Serialized document + page rows, sufficient to restore them exactly.</summary>
    public required string PayloadJson { get; set; }

    /// <summary>JSON array of file names moved into the trash folder (image first, then sidecars).</summary>
    public required string FilesJson { get; set; }

    public required string TrashFolderPath { get; set; }

    public DateTime DeletedUtc { get; set; }
}

/// <summary>App settings as key/value (retention days, etc.).</summary>
public class Setting
{
    public required string Key { get; set; }

    public required string Value { get; set; }
}
