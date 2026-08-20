using System.Net;
using FgScanner.Core.Capture;
using FgScanner.Core.Hooks;
using FgScanner.Core.Index;
using FgScanner.Data;
using Xunit;

namespace FgScanner.Data.Tests;

public sealed class CommitHookRunnerTests : IDisposable
{
    private readonly TestDb _db = new();
    private readonly GroupService _groups;
    private readonly ProfileService _profiles;
    private readonly AppSettingsService _settings;
    private readonly string _groupsRoot;

    public CommitHookRunnerTests()
    {
        _groups = new GroupService(_db.Factory);
        _profiles = new ProfileService(_db.Factory);
        _settings = new AppSettingsService(_db.Factory);
        _groupsRoot = Path.Combine(_db.Root, "groups");
        Directory.CreateDirectory(_groupsRoot);
    }

    public void Dispose() => _db.Dispose();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static IndexExportData MinimalData(string directory) => new(
        "G", directory, "(none)", 0, "0.0.0", DateTime.UtcNow, [], [IndexFormat.Csv], []);

    private sealed class StubHandler : HttpMessageHandler
    {
        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }

    private async Task<Group> CreateCommittableGroupAsync()
    {
        var group = await _groups.CreateGroupAsync(_groupsRoot, "Hooked", null, Ct);
        var incoming = Path.Combine(_db.Root, "in");
        Directory.CreateDirectory(incoming);
        var file = Path.Combine(incoming, "p1.png");
        await File.WriteAllBytesAsync(file, [1], Ct);
        await _groups.AdoptPagesAsync(group.Id, [file], Ct);
        return group;
    }

    [Fact]
    public async Task Flag_off_runs_nothing()
    {
        var handler = new StubHandler();
        var runner = new CommitHookRunner(_settings, new CommitHookService(handler));
        await _settings.SetAsync(CommitHookRunner.WebhookUrlKey, "https://example.test/hook", Ct);

        var result = await runner.RunAsync(MinimalData(_db.Root), Ct);

        Assert.Null(result);
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task Group_commit_fires_the_webhook_and_journals_the_outcome()
    {
        var handler = new StubHandler();
        var runner = new CommitHookRunner(_settings, new CommitHookService(handler));
        await _settings.SetAsync(FeatureFlags.CommitHook, "true", Ct);
        await _settings.SetAsync(CommitHookRunner.WebhookUrlKey, "https://example.test/hook", Ct);

        var group = await CreateCommittableGroupAsync();
        var indexing = new IndexingService(_db.Factory, _profiles, new IndexExporter(), runner);
        var (validation, export) = await indexing.CommitGroupAsync(group.Id, Ct);

        Assert.False(validation.HasErrors);
        Assert.NotNull(export);
        Assert.Equal(1, handler.Calls);
        var journal = await File.ReadAllTextAsync(
            Path.Combine(group.DirectoryPath, GroupJournal.FileName), Ct);
        Assert.Contains("commit hook: webhook HTTP 200", journal);
    }

    [Fact]
    public async Task Hook_failure_never_fails_the_commit()
    {
        var runner = new CommitHookRunner(_settings, new CommitHookService());
        await _settings.SetAsync(FeatureFlags.CommitHook, "true", Ct);
        await _settings.SetAsync(CommitHookRunner.WebhookUrlKey, "http://127.0.0.1:1/unreachable", Ct);

        var group = await CreateCommittableGroupAsync();
        var indexing = new IndexingService(_db.Factory, _profiles, new IndexExporter(), runner);
        var (validation, export) = await indexing.CommitGroupAsync(group.Id, Ct);

        Assert.False(validation.HasErrors);
        Assert.True(export!.AllSucceeded);
        var journal = await File.ReadAllTextAsync(
            Path.Combine(group.DirectoryPath, GroupJournal.FileName), Ct);
        Assert.Contains("webhook failed:", journal);
    }
}
