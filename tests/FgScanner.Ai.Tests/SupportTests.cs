using Xunit;

namespace FgScanner.Ai.Tests;

public class DescriptionPostProcessorTests
{
    [Fact]
    public void Short_text_passes_through_with_whitespace_collapsed() =>
        Assert.Equal("A short  memo.".Replace("  ", " ", StringComparison.Ordinal),
            DescriptionPostProcessor.Normalize("A short\n\n memo."));

    [Fact]
    public void Long_text_truncates_at_the_last_sentence_boundary_within_1000()
    {
        var sentence = "This sentence is exactly fifty characters long!!! ";
        var text = string.Concat(Enumerable.Repeat(sentence, 30)); // 1500 chars

        var result = DescriptionPostProcessor.Normalize(text);

        Assert.True(result.Length <= 1000, $"length {result.Length}");
        Assert.EndsWith("!", result);
        Assert.Equal(49, result.Length % 50); // whole sentences only (49 chars + separator space each)
    }

    [Fact]
    public void Unbroken_text_falls_back_to_word_boundary_with_ellipsis()
    {
        var text = string.Join(' ', Enumerable.Repeat("word", 400)); // no sentence ends

        var result = DescriptionPostProcessor.Normalize(text);

        Assert.True(result.Length <= 1000);
        Assert.EndsWith("…", result);
    }

    [Theory]
    [InlineData("BLANK PAGE", true)]
    [InlineData("  blank page  ", true)]
    [InlineData("A blank page with a stamp.", false)]
    [InlineData(null, false)]
    public void Blank_sentinel_detection(string? text, bool expected) =>
        Assert.Equal(expected, DescriptionPostProcessor.IsBlankSentinel(text));
}

public class CostEstimatorTests
{
    [Fact]
    public void Estimate_uses_the_research_token_formula()
    {
        // 1000 pages × (1032+110) in @ $0.10/M + 1000 × 250 out @ $0.40/M
        var estimate = CostEstimator.EstimateUsd(1000, "gemini-2.5-flash-lite");

        Assert.Equal(0.2142m, estimate);
    }

    [Fact]
    public void Actual_cost_bills_thinking_tokens_as_output()
    {
        var withThoughts = CostEstimator.ActualUsd(new AiUsage(1032, 250, 500), "gemini-2.5-flash-lite");
        var without = CostEstimator.ActualUsd(new AiUsage(1032, 250, 0), "gemini-2.5-flash-lite");

        Assert.True(withThoughts > without);
        Assert.Equal(500m * 0.40m / 1_000_000m, withThoughts - without);
    }

    [Fact]
    public void Unknown_model_falls_back_to_default_pricing() =>
        Assert.Equal(
            CostEstimator.EstimateUsd(100, "gemini-2.5-flash-lite"),
            CostEstimator.EstimateUsd(100, "some-future-model"));
}

public class AiBackoffPolicyTests
{
    [Fact]
    public void Delay_grows_exponentially_and_caps()
    {
        var random = new Random(42);
        var first = AiBackoffPolicy.DelayFor(1, random);
        var second = AiBackoffPolicy.DelayFor(2, random);
        var huge = AiBackoffPolicy.DelayFor(30, random);

        Assert.InRange(first.TotalSeconds, 2, 2.5);
        Assert.InRange(second.TotalSeconds, 4, 4.5);
        Assert.True(huge <= AiBackoffPolicy.MaxDelay);
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData("one two three four", true)]
    [InlineData("one two three four five", false)]
    public void Blank_page_short_circuit_by_word_count(string? text, bool expectedBlank) =>
        Assert.Equal(expectedBlank, AiBackoffPolicy.IsBlankByOcr(text));
}

public sealed class CredentialStoreTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("fgs-cred").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void Dpapi_fallback_round_trips_and_clears()
    {
        // Credential Manager is disabled so tests never touch the user's real vault.
        var store = new CredentialStore(_dir, useCredentialManager: false);
        Assert.False(store.HasKey);

        store.SetKey("AIzaSy-test-key-123");

        Assert.True(store.HasKey);
        Assert.Equal("AIzaSy-test-key-123", store.GetKey());
        var raw = File.ReadAllBytes(Path.Combine(_dir, "ai.key.bin"));
        Assert.DoesNotContain("AIzaSy", System.Text.Encoding.UTF8.GetString(raw)); // encrypted at rest

        store.ClearKey();
        Assert.False(store.HasKey);
        Assert.Null(store.GetKey());
    }
}
