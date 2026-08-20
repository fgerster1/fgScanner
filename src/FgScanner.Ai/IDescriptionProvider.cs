namespace FgScanner.Ai;

/// <summary>Token usage from one call, for spend tracking (PLAN §5.6).</summary>
public sealed record AiUsage(int PromptTokens, int OutputTokens, int ThoughtTokens)
{
    public int TotalOutputTokens => OutputTokens + ThoughtTokens;
}

/// <summary>
/// One page described. Failed results carry whether a retry can help (429/5xx/network yes;
/// 400/403/safety/recitation no).
/// </summary>
public sealed record DescriptionResult(
    bool Success,
    string? Description,
    string? FailureReason,
    bool Retryable,
    AiUsage? Usage)
{
    public static DescriptionResult Ok(string description, AiUsage? usage) =>
        new(true, description, null, false, usage);

    public static DescriptionResult Fail(string reason, bool retryable, AiUsage? usage = null) =>
        new(false, null, reason, retryable, usage);
}

/// <summary>
/// Provider seam (PLAN §5.6): Gemini today; a local Ollama vision model can slot in later via the
/// same Microsoft.Extensions.AI abstractions without touching call sites.
/// </summary>
public interface IDescriptionProvider
{
    Task<DescriptionResult> DescribeAsync(string imagePath, CancellationToken cancellationToken = default);

    /// <summary>Cheapest possible round-trip to prove a pasted key works (1 output token).</summary>
    Task<DescriptionResult> ValidateKeyAsync(CancellationToken cancellationToken = default);
}
