using System.Globalization;
using System.Security.Cryptography;
using FgScanner.Core;
using Microsoft.EntityFrameworkCore;

namespace FgScanner.Data;

/// <summary>Result of adopting scanned files into a group: what landed, what was a duplicate.</summary>
public sealed record AdoptResult(IReadOnlyList<Page> Adopted, IReadOnlyList<string> DuplicateSourceFiles);

public sealed class GroupService(IDbContextFactory<FgScannerDbContext> dbFactory)
{
    /// <summary>Creates a group as a new subdirectory of <paramref name="parentDirectory"/> (name sanitized).</summary>
    public async Task<Group> CreateGroupAsync(
        string parentDirectory, string name, CancellationToken cancellationToken = default)
    {
        var safeName = GroupNameSanitizer.Sanitize(name);
        var directory = Path.Combine(parentDirectory, safeName);
        return await AdoptDirectoryAsync(directory, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Opens the group for an existing directory, or registers the directory as a new group.
    /// The directory name IS the group name (PLAN §5.1).
    /// </summary>
    public async Task<Group> AdoptDirectoryAsync(string directory, CancellationToken cancellationToken = default)
    {
        var fullPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var existing = await db.Groups
            .FirstOrDefaultAsync(g => g.DirectoryPath == fullPath, cancellationToken).ConfigureAwait(false);
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
            CreatedUtc = DateTime.UtcNow,
            UpdatedUtc = DateTime.UtcNow,
        };
        db.Groups.Add(group);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return group;
    }

    public async Task<IReadOnlyList<Group>> ListGroupsAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        return await db.Groups.OrderByDescending(g => g.UpdatedUtc).ToListAsync(cancellationToken).ConfigureAwait(false);
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
    public async Task<AdoptResult> AdoptPagesAsync(
        Guid groupId, IEnumerable<string> sourceFiles, CancellationToken cancellationToken = default)
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
