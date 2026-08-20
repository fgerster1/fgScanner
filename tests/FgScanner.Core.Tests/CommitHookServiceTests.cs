using System.Net;
using System.Text.Json;
using FgScanner.Core.Hooks;
using FgScanner.Core.Index;
using Xunit;

namespace FgScanner.Core.Tests;

public sealed class CommitHookServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "fgscanner-tests", Guid.NewGuid().ToString("N"));

    public CommitHookServiceTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private IndexExportData Data() => ExporterTestData.Build(IndexFormat.Csv) with { GroupDirectory = _root };

    private sealed class StubHandler(HttpStatusCode status) : HttpMessageHandler
    {
        public string? CapturedBody { get; private set; }

        public Uri? CapturedUri { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CapturedUri = request.RequestUri;
            CapturedBody = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(status);
        }
    }

    [Fact]
    public void Tokens_expand_to_group_directory_and_manifest()
    {
        var expanded = CommitHookService.ExpandTokens("do $(group) in $(dir) with $(manifest)", Data());

        Assert.Equal($"do Invoices 2026 in {_root} with {Path.Combine(_root, "manifest.json")}", expanded);
    }

    [Fact]
    public async Task Command_runs_in_the_group_directory_and_reports_exit_zero()
    {
        var result = await new CommitHookService().RunAsync(
            new CommitHookOptions("echo hooked> hook-out.txt", null), Data(), Ct);

        Assert.Equal(0, result.CommandExitCode);
        Assert.Null(result.CommandError);
        Assert.Null(result.WebhookStatus);
        Assert.True(File.Exists(Path.Combine(_root, "hook-out.txt")));
    }

    [Fact]
    public async Task Failing_command_reports_its_exit_code_without_throwing()
    {
        var result = await new CommitHookService().RunAsync(
            new CommitHookOptions("exit 7", null), Data(), Ct);

        Assert.Equal(7, result.CommandExitCode);
    }

    [Fact]
    public async Task Webhook_posts_the_index_json_payload()
    {
        var handler = new StubHandler(HttpStatusCode.OK);

        var result = await new CommitHookService(handler).RunAsync(
            new CommitHookOptions(null, "https://example.test/hook"), Data(), Ct);

        Assert.Equal(200, result.WebhookStatus);
        Assert.Null(result.WebhookError);
        Assert.Equal("https://example.test/hook", handler.CapturedUri!.ToString());

        using var payload = JsonDocument.Parse(handler.CapturedBody!);
        Assert.Equal("Invoices 2026", payload.RootElement.GetProperty("manifest").GetProperty("group").GetString());
        Assert.Equal(4, payload.RootElement.GetProperty("rows").GetArrayLength());
        // Same shape as index.json, byte for byte.
        Assert.Equal(IndexPayload.ToJson(Data()), handler.CapturedBody);
    }

    [Fact]
    public async Task Webhook_failure_is_reported_not_thrown()
    {
        var result = await new CommitHookService(new StubHandler(HttpStatusCode.InternalServerError)).RunAsync(
            new CommitHookOptions(null, "https://example.test/hook"), Data(), Ct);

        Assert.Equal(500, result.WebhookStatus);
        Assert.NotNull(result.WebhookError);
    }

    [Fact]
    public async Task Nothing_configured_runs_nothing()
    {
        var result = await new CommitHookService().RunAsync(
            new CommitHookOptions("", "  "), Data(), Ct);

        Assert.False(result.RanAnything);
    }
}
