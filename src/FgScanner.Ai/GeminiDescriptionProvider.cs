using Google.GenAI;
using Google.GenAI.Types;

namespace FgScanner.Ai;

/// <summary>
/// Gemini over the official Google.GenAI SDK (PLAN §5.6): BYO key, temperature 0.2,
/// maxOutputTokens 400, thinking pinned to minimal on Gemini-3 models (their default "medium"
/// silently multiplies cost). SDK-internal retries are disabled — the worker owns backoff so the
/// first 429 can also halve global concurrency. The API key is never logged anywhere.
/// </summary>
public sealed class GeminiDescriptionProvider : IDescriptionProvider, IDisposable
{
    public const string DefaultModel = "gemini-2.5-flash-lite";

    private readonly Client _client;
    private readonly string _model;

    public GeminiDescriptionProvider(
        string apiKey, string model = DefaultModel, Func<HttpClient>? httpClientFactory = null)
    {
        _model = model;
        _client = new Client(
            apiKey: apiKey,
            httpOptions: new HttpOptions
            {
                RetryOptions = new HttpRetryOptions { Attempts = 1 },
            },
            clientOptions: httpClientFactory is null
                ? null
                : new ClientOptions { HttpClientFactory = httpClientFactory });
    }

    public async Task<DescriptionResult> DescribeAsync(
        string imagePath, CancellationToken cancellationToken = default)
    {
        byte[] imageBytes;
        try
        {
            imageBytes = await System.IO.File.ReadAllBytesAsync(imagePath, cancellationToken).ConfigureAwait(false);
        }
        catch (IOException ex)
        {
            return DescriptionResult.Fail($"Could not read image: {ex.Message}", retryable: false);
        }

        var content = new Content
        {
            Role = "user",
            Parts =
            [
                Part.FromBytes(imageBytes, MimeType(imagePath)),
                Part.FromText(DescriptionPrompt.Text),
            ],
        };
        return await GenerateAsync(content, maxOutputTokens: 400, cancellationToken).ConfigureAwait(false);
    }

    public Task<DescriptionResult> ValidateKeyAsync(CancellationToken cancellationToken = default) =>
        GenerateAsync(
            new Content { Role = "user", Parts = [Part.FromText("Reply with the single word OK.")] },
            maxOutputTokens: 1,
            cancellationToken);

    private async Task<DescriptionResult> GenerateAsync(
        Content content, int maxOutputTokens, CancellationToken cancellationToken)
    {
        var config = new GenerateContentConfig
        {
            Temperature = 0.2f,
            MaxOutputTokens = maxOutputTokens,
        };
        if (_model.StartsWith("gemini-3", StringComparison.OrdinalIgnoreCase))
        {
            config.ThinkingConfig = new ThinkingConfig { ThinkingLevel = ThinkingLevel.Minimal };
        }

        try
        {
            var response = await _client.Models
                .GenerateContentAsync(_model, [content], config, cancellationToken)
                .ConfigureAwait(false);
            return Map(response);
        }
        catch (ClientError ex)
        {
            var status = (int?)ex.StatusCode ?? 0;
            // 429 (rate limit) and 408 (timeout) are transient; other 4xx (400 bad request,
            // 403 bad key) will fail identically on retry.
            return DescriptionResult.Fail(
                $"HTTP {status}: {ex.Message}", retryable: status is 429 or 408);
        }
        catch (ServerError ex)
        {
            return DescriptionResult.Fail(
                $"HTTP {(int?)ex.StatusCode ?? 500}: {ex.Message}", retryable: true);
        }
        catch (HttpRequestException ex)
        {
            return DescriptionResult.Fail($"Network error: {ex.Message}", retryable: true);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return DescriptionResult.Fail("Request timed out.", retryable: true);
        }
    }

    private static DescriptionResult Map(GenerateContentResponse response)
    {
        var usage = response.UsageMetadata is { } u
            ? new AiUsage(u.PromptTokenCount ?? 0, u.CandidatesTokenCount ?? 0, u.ThoughtsTokenCount ?? 0)
            : null;
        var candidate = response.Candidates?.FirstOrDefault();
        var finishReason = candidate?.FinishReason;
        var text = response.Text;

        if (finishReason == FinishReason.Safety || finishReason == FinishReason.ProhibitedContent)
        {
            return DescriptionResult.Fail("Blocked by safety filter.", retryable: false, usage);
        }

        if (finishReason == FinishReason.Recitation)
        {
            return DescriptionResult.Fail("Blocked as recitation.", retryable: false, usage);
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            // MAX_TOKENS with zero text (all budget went to thinking) or an empty candidate.
            return DescriptionResult.Fail(
                $"Empty response (finishReason: {finishReason?.Value ?? "none"}).", retryable: false, usage);
        }

        // MAX_TOKENS with usable text is fine — the sentence-boundary truncation cleans the cut.
        return DescriptionResult.Ok(DescriptionPostProcessor.Normalize(text), usage);
    }

    private static string MimeType(string path) => Path.GetExtension(path).ToUpperInvariant() switch
    {
        ".JPG" or ".JPEG" => "image/jpeg",
        ".TIF" or ".TIFF" => "image/tiff",
        ".BMP" => "image/bmp",
        _ => "image/png",
    };

    public void Dispose() => _client.Dispose();
}
