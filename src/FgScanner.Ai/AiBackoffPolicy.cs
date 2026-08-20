namespace FgScanner.Ai;

/// <summary>Exponential backoff with jitter for 429/408/5xx (PLAN §5.6).</summary>
public static class AiBackoffPolicy
{
    public static readonly TimeSpan MaxDelay = TimeSpan.FromSeconds(30);

    public static TimeSpan DelayFor(int attempt, Random? random = null)
    {
        var seconds = Math.Min(Math.Pow(2, Math.Max(1, attempt)), MaxDelay.TotalSeconds);
        var jitterMs = (random ?? Random.Shared).Next(0, 500);
        var delay = TimeSpan.FromSeconds(seconds) + TimeSpan.FromMilliseconds(jitterMs);
        return delay <= MaxDelay ? delay : MaxDelay;
    }

    /// <summary>Word count for the blank-page short-circuit: &lt;5 OCR words → no API call.</summary>
    public static bool IsBlankByOcr(string? ocrText) =>
        (ocrText ?? "").Split(' ', '\n', '\t')
            .Count(w => !string.IsNullOrWhiteSpace(w)) < 5;
}
