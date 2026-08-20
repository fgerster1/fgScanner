using System.Globalization;
using System.IO;
using FgScanner.Ai;
using FgScanner.Data;
using Serilog;

namespace FgScanner.App.Services;

/// <summary>
/// Background AI-description worker (PLAN §5.6): drains the durable queue with bounded
/// concurrency 4, halving globally on the first 429; exponential backoff + jitter on transient
/// failures; blank pages (Tesseract &lt;5 words) skip the API entirely; spend accumulates from
/// response usage. Idle without a stored key — the feature stays dark until one exists.
/// </summary>
public sealed class AiWorker : IDisposable
{
    public const string SpendSettingKey = "Ai.SpendTotalUsd";
    public const string ModelSettingKey = "Ai.Model";

    private readonly AiQueueService _queue;
    private readonly AppSettingsService _settings;
    private readonly IndexingService _indexing;
    private readonly ActiveGroupStore _activeGroup;
    private readonly CredentialStore _credentials;
    private readonly SemaphoreSlim _wake = new(0);
    private readonly CancellationTokenSource _stop = new();
    private Task? _loop;
    private int _concurrencyLimit = 4;

    public AiWorker(
        AiQueueService queue,
        AppSettingsService settings,
        IndexingService indexing,
        ActiveGroupStore activeGroup,
        CredentialStore credentials)
    {
        _queue = queue;
        _settings = settings;
        _indexing = indexing;
        _activeGroup = activeGroup;
        _credentials = credentials;
        queue.JobsEnqueued += () => _wake.Release();
    }

    public void Start() => _loop = Task.Run(RunAsync);

    private async Task RunAsync()
    {
        var requeued = await _queue.ResetInFlightAsync(_stop.Token);
        if (requeued > 0)
        {
            Log.Information("Re-queued {Count} AI job(s) from a previous run", requeued);
        }

        while (!_stop.IsCancellationRequested)
        {
            try
            {
                if (_credentials.GetKey() is { } key && await _queue.PendingCountAsync(_stop.Token) > 0)
                {
                    await DrainAsync(key);
                }

                await _wake.WaitAsync(TimeSpan.FromSeconds(30), _stop.Token);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "AI worker loop");
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(5), _stop.Token);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }
    }

    private async Task DrainAsync(string apiKey)
    {
        var model = await _settings.GetAsync(ModelSettingKey, GeminiDescriptionProvider.DefaultModel, _stop.Token);
        using var provider = new GeminiDescriptionProvider(apiKey, model);
        var touchedGroups = new HashSet<Guid>();
        var running = new List<Task>();

        while (!_stop.IsCancellationRequested)
        {
            running.RemoveAll(t => t.IsCompleted);
            if (running.Count >= Volatile.Read(ref _concurrencyLimit))
            {
                await Task.WhenAny(running);
                continue;
            }

            var job = await _queue.ClaimNextAsync(_stop.Token);
            if (job is null)
            {
                break;
            }

            lock (touchedGroups)
            {
                touchedGroups.Add(job.GroupId);
            }

            running.Add(Task.Run(() => ProcessAsync(provider, job, model), _stop.Token));
        }

        await Task.WhenAll(running);
        foreach (var groupId in touchedGroups)
        {
            await _indexing.ReexportIfCommittedAsync(groupId, _stop.Token);
        }

        if (touchedGroups.Count > 0)
        {
            System.Windows.Application.Current?.Dispatcher.Invoke(_activeGroup.NotifyGroupContentChanged);
        }
    }

    private async Task ProcessAsync(
        GeminiDescriptionProvider provider, AiQueueService.ClaimedJob job, string model)
    {
        try
        {
            if (!File.Exists(job.ImagePath))
            {
                await _queue.FailAsync(job.JobId, $"Image not found: {job.ImagePath}", retryable: false, _stop.Token);
                return;
            }

            // Blank-page short-circuit: OCR already proved there is nothing to describe.
            if (job.OcrStatus == OcrStatus.Yes && AiBackoffPolicy.IsBlankByOcr(job.OcrText))
            {
                await _queue.SkipAsync(job.JobId, DescriptionPrompt.BlankPageSentinel, _stop.Token);
                return;
            }

            var result = await provider.DescribeAsync(job.ImagePath, _stop.Token);
            if (result.Usage is { } usage)
            {
                Log.Debug(
                    "AI usage for {Image}: prompt={Prompt} output={Output} thoughts={Thoughts}",
                    Path.GetFileName(job.ImagePath), usage.PromptTokens, usage.OutputTokens, usage.ThoughtTokens);
                await AccumulateSpendAsync(usage, model);
            }

            if (result.Success)
            {
                var description = DescriptionPostProcessor.IsBlankSentinel(result.Description)
                    ? DescriptionPrompt.BlankPageSentinel
                    : result.Description!;
                await _queue.CompleteAsync(job.JobId, description, _stop.Token);
                return;
            }

            if (result.Retryable)
            {
                if (result.FailureReason?.StartsWith("HTTP 429", StringComparison.Ordinal) == true)
                {
                    HalveConcurrency();
                }

                await Task.Delay(AiBackoffPolicy.DelayFor(job.Attempt), _stop.Token);
            }

            await _queue.FailAsync(job.JobId, result.FailureReason ?? "unknown", result.Retryable, _stop.Token);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Log.Error(ex, "AI description of {Image}", job.ImagePath);
            await _queue.FailAsync(job.JobId, ex.Message, retryable: false, _stop.Token);
        }
    }

    /// <summary>The first 429 halves global concurrency for the rest of the app session.</summary>
    private void HalveConcurrency()
    {
        var current = Volatile.Read(ref _concurrencyLimit);
        if (current > 1)
        {
            Volatile.Write(ref _concurrencyLimit, current / 2);
            Log.Information("Rate limited (429) — AI concurrency reduced to {Limit}", current / 2);
        }
    }

    private async Task AccumulateSpendAsync(AiUsage usage, string model)
    {
        var cost = CostEstimator.ActualUsd(usage, model);
        var stored = await _settings.GetAsync(SpendSettingKey, "0", _stop.Token);
        _ = decimal.TryParse(stored, NumberStyles.Number, CultureInfo.InvariantCulture, out var total);
        await _settings.SetAsync(
            SpendSettingKey, (total + cost).ToString("0.######", CultureInfo.InvariantCulture), _stop.Token);
    }

    public void Dispose()
    {
        _stop.Cancel();
        try
        {
            _loop?.Wait(TimeSpan.FromSeconds(5));
        }
        catch (AggregateException)
        {
        }

        _stop.Dispose();
        _wake.Dispose();
    }
}
