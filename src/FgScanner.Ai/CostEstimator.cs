namespace FgScanner.Ai;

public sealed record ModelPricing(decimal InputPerMillionUsd, decimal OutputPerMillionUsd);

/// <summary>
/// Pre-run estimates and per-call actuals (PLAN §5.6). A Letter/A4 page at 300 DPI costs about
/// 1,032 image tokens (4 tiles × 258 — research-3 Part B); output typically ~250 tokens.
/// Prices come from a config table so new models don't need code changes.
/// </summary>
public static class CostEstimator
{
    public const int ImageTokensPerPage = 1032;
    public const int PromptTextTokens = 110; // the instruction text itself
    public const int TypicalOutputTokens = 250;

    /// <summary>Observed 2026-08 pricing; unknown models fall back to the default model's rates.</summary>
    public static readonly IReadOnlyDictionary<string, ModelPricing> Pricing =
        new Dictionary<string, ModelPricing>(StringComparer.OrdinalIgnoreCase)
        {
            ["gemini-2.5-flash-lite"] = new(0.10m, 0.40m),
            ["gemini-2.5-flash"] = new(0.30m, 2.50m),
            ["gemini-3.5-flash-lite"] = new(0.30m, 2.00m),
        };

    public static ModelPricing PricingFor(string model) =>
        Pricing.TryGetValue(model, out var pricing)
            ? pricing
            : Pricing[GeminiDescriptionProvider.DefaultModel];

    public static decimal EstimateUsd(int pageCount, string model)
    {
        var pricing = PricingFor(model);
        var inputTokens = (long)pageCount * (ImageTokensPerPage + PromptTextTokens);
        var outputTokens = (long)pageCount * TypicalOutputTokens;
        return (inputTokens * pricing.InputPerMillionUsd / 1_000_000m)
            + (outputTokens * pricing.OutputPerMillionUsd / 1_000_000m);
    }

    /// <summary>Actual cost of one call from response usage (thinking tokens bill as output).</summary>
    public static decimal ActualUsd(AiUsage usage, string model)
    {
        var pricing = PricingFor(model);
        return (usage.PromptTokens * pricing.InputPerMillionUsd / 1_000_000m)
            + (usage.TotalOutputTokens * pricing.OutputPerMillionUsd / 1_000_000m);
    }
}
