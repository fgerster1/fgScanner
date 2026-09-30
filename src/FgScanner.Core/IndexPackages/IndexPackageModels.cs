namespace FgScanner.Core.IndexPackages;

/// <summary>
/// An opened index package (SPEC-2026-005; the contract lives in JimsStuff
/// docs/contract/, vendored here under docs/contract-vendored/). This is the
/// portal→scanner half of the index batch loop and shares nothing with the
/// capture evidence contract. Plain models, paths passed in, no EF — the
/// Core wall stands.
/// </summary>
public sealed record IndexPackage(
    string PackageDirectory,
    string PackageId,
    int FormatVersion,
    int VocabularyVersion,
    string PackageChecksum,
    string AppVersionAtOpen,
    IReadOnlyList<SeedDocument> Documents,
    IReadOnlyList<PackagePerson> People,
    IReadOnlyList<PackageSubject> Subjects,
    IReadOnlyList<PackageDocType> DocTypes);

public sealed record SeedDocument(
    string AnchorPageId,
    long? DocumentId,
    string? Title,
    IReadOnlyList<SeedPage> Pages,
    IReadOnlyList<SeedDecision> Decisions,
    IReadOnlyList<SeedSuggestion> Suggestions);

public sealed record SeedPage(string PageId, string Image);

public sealed record SeedDecision(string Field, string? Qualifier, string Value);

public sealed record SeedSuggestion(
    long SuggestionId, string Field, string? Qualifier, string Value, string? ReasonQuote);

public sealed record PackagePerson(
    string Id,
    string DisplayName,
    string Kind,
    IReadOnlyList<string> Roles,
    IReadOnlyList<string> Aliases);

public sealed record PackageSubject(string Id, string Label, string? ParentId, bool Active);

public sealed record PackageDocType(string Id, string Label, string Family, bool Active);

/// <summary>
/// One doc-type verdict for one document — the single field type this slice
/// carries (SPEC-2026-005 §03.4); more field types are later phases.
/// </summary>
public sealed record DocTypeAnswer(
    string AnchorPageId, string DocTypeId, string DecidedBy, DateTimeOffset DecidedAt);
