using System.Globalization;
using System.Security.Cryptography;
using FgScanner.Core;
using Microsoft.EntityFrameworkCore;

namespace FgScanner.Data;

/// <summary>Result of adopting scanned files into a group: what landed, what was a duplicate.</summary>
public sealed record AdoptResult(IReadOnlyList<Page> Adopted, IReadOnlyList<string> DuplicateSourceFiles);

/// <summary>What happens to the scanned files when a group is deleted — the user's choice.</summary>
public enum GroupFilePolicy
{
    /// <summary>Unregister the group; every file stays exactly where it is.</summary>
    KeepFiles,

    /// <summary>Move the group's folder somewhere else, then unregister it.</summary>
    MoveFiles,

    /// <summary>Move the folder into the trash root, then unregister. Recoverable from disk.</summary>
    DeleteFiles,
}

/// <summary>
/// Outcome of a cross-group move. <paramref name="SkippedAsDuplicate"/> lists file names whose
/// content already exists in the target — dedup is scoped per group, so a move can collide.
/// </summary>
public sealed record MoveResult(
    int MovedCount,
    IReadOnlyList<string> SkippedAsDuplicate,
    bool TargetSchemaDiffers);

public sealed class GroupService(IDbContextFactory<FgScannerDbContext> dbFactory)
{
    /// <summary>Creates a group as a new subdirectory of <paramref name="parentDirectory"/> (name sanitized).</summary>
    public async Task<Group> CreateGroupAsync(
        string parentDirectory, string name, (Guid ProfileId, int SchemaVersion)? profile = null,
        CancellationToken cancellationToken = default)
    {
        var safeName = GroupNameSanitizer.Sanitize(name);
        var directory = Path.Combine(parentDirectory, safeName);
        return await AdoptDirectoryAsync(directory, profile, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Opens the group for an existing directory, or registers the directory as a new group.
    /// The directory name IS the group name (PLAN §5.1).
    /// </summary>
    public async Task<Group> AdoptDirectoryAsync(
        string directory, (Guid ProfileId, int SchemaVersion)? profile = null,
        CancellationToken cancellationToken = default)
    {
        var fullPath = NormalizeDirectory(directory);
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var existing = await FindByDirectoryAsync(db, fullPath, cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            return existing;
        }

        Directory.CreateDirectory(fullPath);
        var group = new Group
        {
            Id = Guid.NewGuid(),
            Name = Path.GetFileName(fullPath),
            DirectoryPath = fullPath,
            State = GroupState.Scanning,
            ProfileId = profile?.ProfileId,
            SchemaVersion = profile?.SchemaVersion ?? 0,
            CreatedUtc = DateTime.UtcNow,
            UpdatedUtc = DateTime.UtcNow,
        };
        db.Groups.Add(group);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return group;
    }

    /// <summary>True when a group already owns this directory, ignoring case (see BUG-4).</summary>
    public async Task<bool> GroupExistsForDirectoryAsync(
        string directory, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        return await FindByDirectoryAsync(db, NormalizeDirectory(directory), cancellationToken)
            .ConfigureAwait(false) is not null;
    }

    private static string NormalizeDirectory(string directory) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));

    /// <summary>
    /// Windows paths are case-insensitive but SQLite's default collation is BINARY, so a plain
    /// equality match let "C:\Docs" and "c:\docs" each mint a Group row over one physical folder,
    /// whose index files then overwrote each other (BUG-4, docs/roadmap-v0.2.md). NOCASE keeps the
    /// comparison in SQL; the OrderBy makes the winner deterministic when a database already
    /// contains such a pair from before this fix.
    /// </summary>
    private static Task<Group?> FindByDirectoryAsync(
        FgScannerDbContext db, string fullPath, CancellationToken cancellationToken) =>
        db.Groups
            .Where(g => EF.Functions.Collate(g.DirectoryPath, "NOCASE") == fullPath)
            .OrderBy(g => g.CreatedUtc).ThenBy(g => g.Id)
            .FirstOrDefaultAsync(cancellationToken);

    /// <summary>
    /// Groups, newest activity first. Pass <paramref name="profileId"/> to list only the groups
    /// belonging to one profile; Profile is included so the list can show which one owns a group.
    /// </summary>
    public async Task<IReadOnlyList<Group>> ListGroupsAsync(
        Guid? profileId = null, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var query = db.Groups.Include(g => g.Profile).AsQueryable();
        if (profileId is { } id)
        {
            query = query.Where(g => g.ProfileId == id);
        }

        return await query.OrderByDescending(g => g.UpdatedUtc).ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>The group's document ids in order — the unit a cross-group move operates on.</summary>
    public async Task<IReadOnlyList<Guid>> GetDocumentIdsAsync(
        Guid groupId, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        return await db.Documents
            .Where(d => d.GroupId == groupId)
            .OrderBy(d => d.Sequence)
            .Select(d => d.Id)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>The group, or null when it has been deleted.</summary>
    public async Task<Group?> FindAsync(Guid groupId, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        return await db.Groups.FirstOrDefaultAsync(g => g.Id == groupId, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<Page>> GetPagesAsync(Guid groupId, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        return await db.Pages
            .Where(p => p.Document!.GroupId == groupId)
            .OrderBy(p => p.Document!.Sequence).ThenBy(p => p.Sequence)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Moves scanned files into the group directory and records them: one page = one document (v1,
    /// PLAN decision #2). Files whose content is already in the group are skipped as duplicates.
    /// </summary>
    public Task<AdoptResult> AdoptPagesAsync(
        Guid groupId, IEnumerable<string> sourceFiles, CancellationToken cancellationToken = default) =>
        AdoptPagesAsync(groupId, sourceFiles, isBlank: null, cancellationToken);

    /// <summary>Capture triage passes <paramref name="isBlank"/> so flag-policy blanks are marked on adoption.</summary>
    public async Task<AdoptResult> AdoptPagesAsync(
        Guid groupId, IEnumerable<string> sourceFiles, Func<string, bool>? isBlank,
        CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var group = await db.Groups.FirstAsync(g => g.Id == groupId, cancellationToken).ConfigureAwait(false);

        var knownChecksums = (await db.Pages
                .Where(p => p.Document!.GroupId == groupId)
                .Select(p => p.Checksum)
                .ToListAsync(cancellationToken).ConfigureAwait(false))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var nextSequence = await db.Documents
            .Where(d => d.GroupId == groupId)
            .Select(d => (int?)d.Sequence)
            .MaxAsync(cancellationToken).ConfigureAwait(false) ?? 0;

        var adopted = new List<Page>();
        var duplicates = new List<string>();
        foreach (var sourceFile in sourceFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var checksum = await ComputeSha256Async(sourceFile, cancellationToken).ConfigureAwait(false);
            if (!knownChecksums.Add(checksum))
            {
                duplicates.Add(sourceFile);
                continue;
            }

            nextSequence++;
            var fileName = MoveIntoGroup(sourceFile, group.DirectoryPath, nextSequence);
            var document = new Document
            {
                Id = Guid.NewGuid(),
                GroupId = groupId,
                Sequence = nextSequence,
                CreatedUtc = DateTime.UtcNow,
            };
            var page = new Page
            {
                Id = Guid.NewGuid(),
                DocumentId = document.Id,
                FileName = fileName,
                Checksum = checksum,
                Sequence = 1,
                IsBlank = isBlank?.Invoke(sourceFile) ?? false,
                CreatedUtc = DateTime.UtcNow,
            };
            db.Documents.Add(document);
            db.Pages.Add(page);
            adopted.Add(page);
        }

        group.UpdatedUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return new AdoptResult(adopted, duplicates);
    }

    public static async Task<string> ComputeSha256Async(string filePath, CancellationToken cancellationToken = default)
    {
        await using var stream = File.OpenRead(filePath);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        return Convert.ToHexStringLower(hash);
    }

    /// <summary>
    /// Moves documents from one group to another: the image and its sidecars follow, the row keeps
    /// its identity, and both groups are renumbered contiguously. Preserving the document id is the
    /// whole point — the previous workaround (export, then import into the other group) minted a new
    /// id and lost field values, OCR status and the AI description.
    ///
    /// Page rows are UPDATEd rather than deleted and re-inserted, because the FTS5 index is external
    /// content over Pages.OcrText: a delete+insert would leave search pointing at stale rowids.
    /// Callers are responsible for re-exporting both groups afterwards, since only they know whether
    /// a group is committed.
    /// </summary>
    public async Task<MoveResult> MoveDocumentsAsync(
        Guid sourceGroupId, Guid targetGroupId, IReadOnlyList<Guid> documentIds,
        CancellationToken cancellationToken = default)
    {
        if (sourceGroupId == targetGroupId)
        {
            throw new InvalidOperationException("Source and target groups are the same.");
        }

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var source = await db.Groups.FirstAsync(g => g.Id == sourceGroupId, cancellationToken).ConfigureAwait(false);
        var target = await db.Groups.FirstAsync(g => g.Id == targetGroupId, cancellationToken).ConfigureAwait(false);

        // Load the whole source group once. Re-querying after the move would read the database,
        // which still holds the old GroupId until SaveChanges — so the moved rows would come back
        // and get renumbered over their new sequences.
        var sourceDocuments = await db.Documents
            .Include(d => d.Pages)
            .Where(d => d.GroupId == sourceGroupId)
            .OrderBy(d => d.Sequence)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var documents = sourceDocuments.Where(d => documentIds.Contains(d.Id)).ToList();

        var targetChecksums = (await db.Pages
                .Where(p => p.Document!.GroupId == targetGroupId)
                .Select(p => p.Checksum)
                .ToListAsync(cancellationToken).ConfigureAwait(false))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var nextSequence = await db.Documents
            .Where(d => d.GroupId == targetGroupId)
            .Select(d => (int?)d.Sequence)
            .MaxAsync(cancellationToken).ConfigureAwait(false) ?? 0;

        var moved = 0;
        var skipped = new List<string>();
        foreach (var document in documents)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Dedup is per group, so the same content can legitimately exist in both. Moving it in
            // would break the one-checksum-per-group invariant everything downstream assumes.
            var clash = document.Pages.FirstOrDefault(p => targetChecksums.Contains(p.Checksum));
            if (clash is not null)
            {
                skipped.Add(clash.FileName);
                continue;
            }

            nextSequence++;
            foreach (var page in document.Pages.OrderBy(p => p.Sequence))
            {
                page.FileName = MoveFileAndSidecars(
                    source.DirectoryPath, target.DirectoryPath, page.FileName, nextSequence);
                targetChecksums.Add(page.Checksum);
            }

            document.GroupId = targetGroupId;
            document.Sequence = nextSequence;
            moved++;
        }

        if (moved > 0)
        {
            Renumber([.. sourceDocuments
                .Where(d => d.GroupId == sourceGroupId)
                .OrderBy(d => d.Sequence)]);
            source.UpdatedUtc = DateTime.UtcNow;
            target.UpdatedUtc = DateTime.UtcNow;
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return new MoveResult(moved, skipped, source.SchemaVersion != target.SchemaVersion
            || source.ProfileId != target.ProfileId);
    }

    /// <summary>
    /// Removes a group and, per <paramref name="policy"/>, decides what becomes of its files.
    /// Documents and pages cascade, which is what keeps the FTS index consistent — its delete
    /// trigger fires per page row.
    ///
    /// DeleteFiles relocates the folder into <paramref name="trashRoot"/> rather than erasing it:
    /// a wrong click should not be the end of a batch of scans. It is recoverable from disk, not
    /// through the Trash view, which archives individual pages rather than whole groups.
    /// </summary>
    public async Task DeleteGroupAsync(
        Guid groupId, GroupFilePolicy policy, string trashRoot, string? moveTo = null,
        CancellationToken cancellationToken = default)
    {
        if (policy == GroupFilePolicy.MoveFiles && string.IsNullOrWhiteSpace(moveTo))
        {
            throw new InvalidOperationException("Choose where to move the group's files.");
        }

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var group = await db.Groups.FirstAsync(g => g.Id == groupId, cancellationToken).ConfigureAwait(false);
        var directory = group.DirectoryPath;

        // Rows first: if the file move fails the group is still registered, which is recoverable.
        // The reverse — files gone, rows intact — would leave a group pointing at nothing.
        db.Groups.Remove(group);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        if (policy == GroupFilePolicy.KeepFiles || !Directory.Exists(directory))
        {
            return;
        }

        var destination = policy == GroupFilePolicy.MoveFiles
            ? Path.Combine(moveTo!, Path.GetFileName(directory))
            : Path.Combine(trashRoot, $"group-{groupId:N}", Path.GetFileName(directory));

        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        for (var suffix = 1; Directory.Exists(destination); suffix++)
        {
            destination = $"{destination}_{suffix.ToString(CultureInfo.InvariantCulture)}";
        }

        Directory.Move(directory, destination);
    }

    private static void Renumber(List<Document> documents)
    {
        var sequence = 0;
        foreach (var document in documents)
        {
            document.Sequence = ++sequence;
        }
    }

    /// <summary>Moves an image plus any .md/.txt sidecars, collision-suffixing in the target.</summary>
    private static string MoveFileAndSidecars(
        string sourceDirectory, string targetDirectory, string fileName, int sequence)
    {
        var sourceFile = Path.Combine(sourceDirectory, fileName);
        var extension = Path.GetExtension(fileName);
        var baseName = $"scan_{sequence.ToString("00000", CultureInfo.InvariantCulture)}";
        var newFileName = baseName + extension;
        for (var suffix = 1; File.Exists(Path.Combine(targetDirectory, newFileName)); suffix++)
        {
            newFileName = $"{baseName}_{suffix.ToString(CultureInfo.InvariantCulture)}{extension}";
        }

        if (File.Exists(sourceFile))
        {
            File.Move(sourceFile, Path.Combine(targetDirectory, newFileName));
        }

        foreach (var sidecarExtension in SidecarExtensions)
        {
            var sidecar = Path.ChangeExtension(sourceFile, sidecarExtension);
            if (File.Exists(sidecar))
            {
                File.Move(
                    sidecar,
                    Path.ChangeExtension(Path.Combine(targetDirectory, newFileName), sidecarExtension),
                    overwrite: true);
            }
        }

        return newFileName;
    }

    private static readonly string[] SidecarExtensions = [".md", ".txt"];

    private static string MoveIntoGroup(string sourceFile, string groupDirectory, int sequence)
    {
        var extension = Path.GetExtension(sourceFile);
        var baseName = $"scan_{sequence.ToString("00000", CultureInfo.InvariantCulture)}";
        var fileName = baseName + extension;
        var target = Path.Combine(groupDirectory, fileName);
        for (var suffix = 1; File.Exists(target); suffix++)
        {
            fileName = $"{baseName}_{suffix.ToString(CultureInfo.InvariantCulture)}{extension}";
            target = Path.Combine(groupDirectory, fileName);
        }

        File.Move(sourceFile, target);
        return fileName;
    }
}
