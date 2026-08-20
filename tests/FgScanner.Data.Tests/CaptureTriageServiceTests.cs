using FgScanner.Core.Capture;
using FgScanner.Data;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FgScanner.Data.Tests;

public sealed class CaptureTriageServiceTests : IDisposable
{
    private readonly TestDb _db = new();
    private readonly GroupService _groups;
    private readonly ProfileService _profiles;
    private readonly AppSettingsService _settings;
    private readonly string _groupsRoot;

    public CaptureTriageServiceTests()
    {
        _groups = new GroupService(_db.Factory);
        _profiles = new ProfileService(_db.Factory);
        _settings = new AppSettingsService(_db.Factory);
        _groupsRoot = Path.Combine(_db.Root, "groups");
        Directory.CreateDirectory(_groupsRoot);
    }

    public void Dispose() => _db.Dispose();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>Classifies by file-name prefix, so tests need no real imaging.</summary>
    private sealed class FakeClassifier : IPageClassifier
    {
        public PageKind Classify(string imagePath, CapturePolicy policy)
        {
            var name = Path.GetFileName(imagePath);
            if (name.StartsWith("sep_", StringComparison.Ordinal) && policy.DetectSeparators)
            {
                return PageKind.Separator;
            }

            if (name.StartsWith("blank_", StringComparison.Ordinal) && policy.BlankPolicy != BlankPagePolicy.Keep)
            {
                return policy.BlankPolicy == BlankPagePolicy.Separator ? PageKind.Separator : PageKind.Blank;
            }

            return PageKind.Content;
        }
    }

    private CaptureTriageService CreateService() => new(_db.Factory, _settings, new FakeClassifier());

    private async Task<Group> CreateGroupAsync(
        bool separators = false, bool keepSeparators = false, BlankPagePolicy blanks = BlankPagePolicy.Keep)
    {
        var profile = await _profiles.CreateAsync("P-" + Guid.NewGuid().ToString("N")[..8], Ct);
        await _profiles.UpdateCapturePolicyAsync(profile.Id, separators, keepSeparators, blanks, Ct);
        return await _groups.CreateGroupAsync(
            _groupsRoot, "G-" + Guid.NewGuid().ToString("N")[..8], (profile.Id, 1), Ct);
    }

    private async Task<List<string>> CreateFilesAsync(params string[] names)
    {
        var incoming = Path.Combine(_db.Root, "in-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(incoming);
        var files = new List<string>();
        foreach (var name in names)
        {
            var path = Path.Combine(incoming, name);
            await File.WriteAllTextAsync(path, name, Ct);
            files.Add(path);
        }

        return files;
    }

    private async Task EnableFlagsAsync()
    {
        await _settings.SetAsync(FeatureFlags.PatchT, "true", Ct);
        await _settings.SetAsync(FeatureFlags.BlankPolicy, "true", Ct);
    }

    [Fact]
    public async Task Feature_flags_off_pass_everything_through()
    {
        var group = await CreateGroupAsync(separators: true, blanks: BlankPagePolicy.Drop);
        var files = await CreateFilesAsync("sep_1.png", "blank_1.png", "page_1.png");

        var result = await CreateService().TriageAsync(group, files, Ct);

        Assert.Equal(files, result.FilesToAdopt);
        Assert.Equal(0, result.DroppedCount);
        Assert.False(File.Exists(Path.Combine(group.DirectoryPath, GroupJournal.FileName)));
    }

    [Fact]
    public async Task Separator_pages_are_dropped_journaled_and_deleted()
    {
        await EnableFlagsAsync();
        var group = await CreateGroupAsync(separators: true);
        var files = await CreateFilesAsync("page_1.png", "sep_1.png", "page_2.png");

        var result = await CreateService().TriageAsync(group, files, Ct);

        Assert.Equal([files[0], files[2]], result.FilesToAdopt);
        Assert.Single(result.DroppedSeparators);
        Assert.False(File.Exists(files[1]));
        var journal = await File.ReadAllTextAsync(
            Path.Combine(group.DirectoryPath, GroupJournal.FileName), Ct);
        Assert.Contains("separator page (Patch T) dropped: sep_1.png", journal);
    }

    [Fact]
    public async Task Keep_policy_adopts_separator_pages_and_journals_the_detection()
    {
        await EnableFlagsAsync();
        var group = await CreateGroupAsync(separators: true, keepSeparators: true);
        var files = await CreateFilesAsync("sep_1.png", "page_1.png");

        var result = await CreateService().TriageAsync(group, files, Ct);

        Assert.Equal(files, result.FilesToAdopt);
        Assert.Equal(0, result.DroppedCount);
        var journal = await File.ReadAllTextAsync(
            Path.Combine(group.DirectoryPath, GroupJournal.FileName), Ct);
        Assert.Contains("detected and kept: sep_1.png", journal);
    }

    [Fact]
    public async Task Blank_drop_policy_journals_and_deletes()
    {
        await EnableFlagsAsync();
        var group = await CreateGroupAsync(blanks: BlankPagePolicy.Drop);
        var files = await CreateFilesAsync("blank_1.png", "page_1.png");

        var result = await CreateService().TriageAsync(group, files, Ct);

        Assert.Equal([files[1]], result.FilesToAdopt);
        Assert.Single(result.DroppedBlanks);
        Assert.False(File.Exists(files[0]));
        var journal = await File.ReadAllTextAsync(
            Path.Combine(group.DirectoryPath, GroupJournal.FileName), Ct);
        Assert.Contains("blank page dropped: blank_1.png", journal);
    }

    [Fact]
    public async Task Blank_separator_policy_treats_blanks_like_separator_sheets()
    {
        await EnableFlagsAsync();
        var group = await CreateGroupAsync(blanks: BlankPagePolicy.Separator);
        var files = await CreateFilesAsync("blank_1.png", "page_1.png");

        var result = await CreateService().TriageAsync(group, files, Ct);

        Assert.Equal([files[1]], result.FilesToAdopt);
        Assert.Single(result.DroppedSeparators);
    }

    [Fact]
    public async Task Flagged_blanks_are_adopted_marked_and_excluded_from_ocr_and_index()
    {
        await EnableFlagsAsync();
        var group = await CreateGroupAsync(blanks: BlankPagePolicy.Flag);
        var files = await CreateFilesAsync("blank_1.png", "page_1.png");

        var result = await CreateService().TriageAsync(group, files, Ct);
        Assert.Equal(files, result.FilesToAdopt);
        Assert.Single(result.FlaggedBlanks);

        var adopted = await _groups.AdoptPagesAsync(group.Id, result.FilesToAdopt, result.IsBlankFlagged, Ct);
        Assert.Equal(2, adopted.Adopted.Count);
        Assert.True(adopted.Adopted[0].IsBlank);
        Assert.False(adopted.Adopted[1].IsBlank);

        // Excluded from the OCR queue…
        var queued = await new OcrQueueService(_db.Factory).EnqueueGroupAsync(group.Id, cancellationToken: Ct);
        Assert.Equal(1, queued);

        // …and from the index rows.
        var indexing = new IndexingService(_db.Factory, _profiles, new Core.Index.IndexExporter());
        var data = await indexing.BuildExportDataAsync(group.Id, Ct);
        Assert.Single(data.Rows);
        Assert.Equal(adopted.Adopted[1].FileName, data.Rows[0].ImageName);
    }
}
