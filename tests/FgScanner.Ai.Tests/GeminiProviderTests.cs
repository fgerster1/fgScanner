using System.Net;
using RichardSzalay.MockHttp;
using Xunit;

namespace FgScanner.Ai.Tests;

/// <summary>All HTTP behavior via MockHttp — no live keys, no network, ever (CLAUDE.md rule).</summary>
public sealed class GeminiProviderTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("fgs-ai").FullName;
    private readonly MockHttpMessageHandler _mockHttp = new();
    private readonly string _imagePath;

    public GeminiProviderTests()
    {
        _imagePath = Path.Combine(_dir, "page.png");
        File.WriteAllBytes(_imagePath, [0x89, 0x50, 0x4E, 0x47, 1, 2, 3]);
    }

    public void Dispose()
    {
        _mockHttp.Dispose();
        Directory.Delete(_dir, recursive: true);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private GeminiDescriptionProvider CreateProvider(string model = "gemini-2.5-flash-lite") =>
        new("test-api-key", model, () => _mockHttp.ToHttpClient());

    private static string SuccessJson(
        string text, string finishReason = "STOP", int prompt = 1142, int output = 210, int thoughts = 0) =>
        $$"""
        {
          "candidates": [
            {
              "content": { "parts": [ { "text": "{{text}}" } ], "role": "model" },
              "finishReason": "{{finishReason}}"
            }
          ],
          "usageMetadata": {
            "promptTokenCount": {{prompt}},
            "candidatesTokenCount": {{output}},
            "thoughtsTokenCount": {{thoughts}},
            "totalTokenCount": {{prompt + output + thoughts}}
          }
        }
        """;

    [Fact]
    public async Task Success_returns_normalized_description_and_usage()
    {
        _mockHttp.When("*generateContent*")
            .Respond("application/json", SuccessJson("A signed 1987 letter from Acme Corp.  Two   spaces."));
        using var provider = CreateProvider();

        var result = await provider.DescribeAsync(_imagePath, Ct);

        Assert.True(result.Success, result.FailureReason);
        Assert.Equal("A signed 1987 letter from Acme Corp. Two spaces.", result.Description);
        Assert.Equal(1142, result.Usage!.PromptTokens);
        Assert.Equal(210, result.Usage.OutputTokens);
    }

    [Fact]
    public async Task Request_authenticates_with_header_and_sends_inline_image()
    {
        string? capturedBody = null;
        _mockHttp.When("*generateContent*")
            .With(request =>
            {
                capturedBody = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
                return request.Headers.TryGetValues("x-goog-api-key", out var values)
                    && values.Contains("test-api-key");
            })
            .Respond("application/json", SuccessJson("ok"));
        using var provider = CreateProvider();

        var result = await provider.DescribeAsync(_imagePath, Ct);

        Assert.True(result.Success, result.FailureReason);
        Assert.Contains("inlineData", capturedBody, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("image/png", capturedBody, StringComparison.Ordinal);
        Assert.Contains("Do not transcribe", capturedBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Gemini3_models_pin_thinking_to_minimal()
    {
        string? capturedBody = null;
        _mockHttp.When("*generateContent*")
            .With(request =>
            {
                capturedBody = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
                return true;
            })
            .Respond("application/json", SuccessJson("ok"));
        using var provider = CreateProvider("gemini-3.5-flash-lite");

        await provider.DescribeAsync(_imagePath, Ct);

        Assert.Contains("thinking", capturedBody, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("MINIMAL", capturedBody, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests, true)]
    [InlineData(HttpStatusCode.RequestTimeout, true)]
    [InlineData(HttpStatusCode.BadRequest, false)]
    [InlineData(HttpStatusCode.Forbidden, false)]
    [InlineData(HttpStatusCode.InternalServerError, true)]
    [InlineData(HttpStatusCode.ServiceUnavailable, true)]
    public async Task Http_errors_classify_retryability(HttpStatusCode status, bool expectRetryable)
    {
        _mockHttp.When("*generateContent*").Respond(status);
        using var provider = CreateProvider();

        var result = await provider.DescribeAsync(_imagePath, Ct);

        Assert.False(result.Success);
        Assert.Equal(expectRetryable, result.Retryable);
        Assert.Contains(((int)status).ToString(System.Globalization.CultureInfo.InvariantCulture), result.FailureReason);
    }

    [Fact]
    public async Task Safety_block_fails_permanently()
    {
        _mockHttp.When("*generateContent*")
            .Respond("application/json",
                """{"candidates":[{"finishReason":"SAFETY"}],"usageMetadata":{"promptTokenCount":1142,"totalTokenCount":1142}}""");
        using var provider = CreateProvider();

        var result = await provider.DescribeAsync(_imagePath, Ct);

        Assert.False(result.Success);
        Assert.False(result.Retryable);
        Assert.Contains("safety", result.FailureReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Recitation_block_fails_permanently()
    {
        _mockHttp.When("*generateContent*")
            .Respond("application/json", """{"candidates":[{"finishReason":"RECITATION"}]}""");
        using var provider = CreateProvider();

        var result = await provider.DescribeAsync(_imagePath, Ct);

        Assert.False(result.Success);
        Assert.False(result.Retryable);
        Assert.Contains("recitation", result.FailureReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Max_tokens_with_text_succeeds_via_sentence_truncation()
    {
        _mockHttp.When("*generateContent*")
            .Respond("application/json",
                SuccessJson("A memo about quarterly results. It was cut mid-sente", "MAX_TOKENS"));
        using var provider = CreateProvider();

        var result = await provider.DescribeAsync(_imagePath, Ct);

        Assert.True(result.Success);
        Assert.Contains("quarterly results", result.Description);
    }

    [Fact]
    public async Task Max_tokens_with_no_text_fails_permanently()
    {
        _mockHttp.When("*generateContent*")
            .Respond("application/json", """{"candidates":[{"finishReason":"MAX_TOKENS"}]}""");
        using var provider = CreateProvider();

        var result = await provider.DescribeAsync(_imagePath, Ct);

        Assert.False(result.Success);
        Assert.False(result.Retryable);
        Assert.Contains("MAX_TOKENS", result.FailureReason);
    }

    [Fact]
    public async Task Network_loss_is_retryable()
    {
        _mockHttp.When("*generateContent*")
            .Throw(new HttpRequestException("Connection reset"));
        using var provider = CreateProvider();

        var result = await provider.DescribeAsync(_imagePath, Ct);

        Assert.False(result.Success);
        Assert.True(result.Retryable);
        Assert.Contains("Network", result.FailureReason);
    }

    [Fact]
    public async Task Missing_image_fails_without_calling_the_api()
    {
        using var provider = CreateProvider();

        var result = await provider.DescribeAsync(Path.Combine(_dir, "gone.png"), Ct);

        Assert.False(result.Success);
        Assert.False(result.Retryable);
        Assert.Equal(0, _mockHttp.GetMatchCount(_mockHttp.When("*")));
    }

    [Fact]
    public async Task Key_validation_uses_a_single_output_token()
    {
        string? capturedBody = null;
        _mockHttp.When("*generateContent*")
            .With(request =>
            {
                capturedBody = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
                return true;
            })
            .Respond("application/json", SuccessJson("OK", prompt: 8, output: 1));
        using var provider = CreateProvider();

        var result = await provider.ValidateKeyAsync(Ct);

        Assert.True(result.Success);
        Assert.Contains("\"maxOutputTokens\":1", capturedBody!.Replace(" ", "", StringComparison.Ordinal));
    }
}
