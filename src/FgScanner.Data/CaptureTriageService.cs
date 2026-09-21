using FgScanner.Core.Capture;
using Microsoft.EntityFrameworkCore;

namespace FgScanner.Data;

/// <summary>
/// What capture-time triage decided for a batch of incoming files. <see cref="FilesToAdopt"/>
/// feeds AdoptPagesAsync (with <see cref="IsBlankFlagged"/> as its blank predicate); dropped
/// files were journaled and deleted.
/// </summary>
public sealed record TriageResult(
    IReadOnlyList<string> FilesToAdopt,
    IReadOnlyList<string> DroppedSeparators,
    IReadOnlyList<string> DroppedBlanks,
    IReadOnlyList<string> FlaggedBlanks)
{
    public static TriageResult PassThrough(IReadOnlyList<string> files) => new(files, [], [], []);

    public int DroppedCount => DroppedSeparators.Count + DroppedBlanks.Count;

    public bool IsBlankFlagged(string sourceFile) => FlaggedBlanks.Contains(sourceFile);
}

/// <summary>
/// Applies the per-profile Patch-T and blank-page policies to files about to be adopted
/// (PLAN prompt 10). Inactive — feature flags off, no profile policy, or no classifier
/// registered — it passes everything through untouched. Dropped files (session/temp copies,
/// never user originals) are journaled in the group, then deleted.
/// </summary>
public sealed class CaptureTriageService(
    IDbContextFactory<FgScannerDbContext> dbFactory,
    AppSettingsService settings,
    IPageClassifier? classifier = null)
{
    /// <param name="keepBlankPages">
    /// Set by a save that captured its pages as pairs. A two-pass duplex stack pairs by position,
    /// so a page removed here shifts every pairing after it — and the blank back of an evidence
    /// page is evidence that the back is blank (SPEC-2026-006 §05 Q2a, AC-7). The pages are still
    /// classified and still flagged, because the row saying the back is blank is the record; only
    /// the deletion is suppressed, and only for the caller that asks.
    /// </param>
    public async Task<TriageResult> TriageAsync(
        Group group,
        IReadOnlyList<string> sourceFiles,
        bool keepBlankPages = false,
        CancellationToken cancellationToken = default)
    {
        var policy = await ResolvePolicyAsync(group, cancellationToken).ConfigureAwait(false);
        if (classifier is null || !policy.IsActive)
        {
            return TriageResult.PassThrough(sourceFiles);
        }

        var adopt = new List<string>();
        var droppedSeparators = new List<string>();
        var droppedBlanks = new List<string>();
        var flaggedBlanks = new List<string>();
        foreach (var file in sourceFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            PageKind kind;
            try
            {
                kind = classifier.Classify(file, policy);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // An undecodable image is a content page; adoption surfaces any real file problem.
                kind = PageKind.Content;
            }

            switch (kind)
            {
                case PageKind.Separator when !policy.KeepSeparatorPages:
                    droppedSeparators.Add(file);
                    await DropAsync(group, file, "separator page (Patch T) dropped", cancellationToken)
                        .ConfigureAwait(false);
                    break;
                case PageKind.Separator:
                    adopt.Add(file);
                    await GroupJournal.AppendAsync(
                        group.DirectoryPath, $"separator page (Patch T) detected and kept: {Path.GetFileName(file)}",
                        cancellationToken).ConfigureAwait(false);
                    break;
                case PageKind.Blank when policy.BlankPolicy == BlankPagePolicy.Drop && !keepBlankPages:
                    droppedBlanks.Add(file);
                    await DropAsync(group, file, "blank page dropped", cancellationToken).ConfigureAwait(false);
                    break;
                case PageKind.Blank:
                    adopt.Add(file);
                    flaggedBlanks.Add(file);
                    break;
                default:
                    adopt.Add(file);
                    break;
            }
        }

        return new TriageResult(adopt, droppedSeparators, droppedBlanks, flaggedBlanks);
    }

    private async Task<CapturePolicy> ResolvePolicyAsync(Group group, CancellationToken cancellationToken)
    {
        if (group.ProfileId is not { } profileId)
        {
            return CapturePolicy.Off;
        }

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var profile = await db.Profiles
            .FirstOrDefaultAsync(p => p.Id == profileId, cancellationToken).ConfigureAwait(false);
        if (profile is null)
        {
            return CapturePolicy.Off;
        }

        var patchT = await FeatureFlags.IsEnabledAsync(settings, FeatureFlags.PatchT, cancellationToken)
            .ConfigureAwait(false);
        var blanks = await FeatureFlags.IsEnabledAsync(settings, FeatureFlags.BlankPolicy, cancellationToken)
            .ConfigureAwait(false);
        return new CapturePolicy(
            patchT && profile.SeparatorDetectionEnabled,
            profile.KeepSeparatorPages,
            blanks ? profile.BlankPolicy : BlankPagePolicy.Keep);
    }

    private static async Task DropAsync(
        Group group, string file, string reason, CancellationToken cancellationToken)
    {
        await GroupJournal.AppendAsync(
            group.DirectoryPath, $"{reason}: {Path.GetFileName(file)}", cancellationToken).ConfigureAwait(false);
        try
        {
            File.Delete(file);
        }
        catch (IOException)
        {
            // A stuck temp copy is litter, not a failure — the page was still excluded.
        }
    }
}
