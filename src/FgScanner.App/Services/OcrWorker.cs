using System.IO;
using FgScanner.Data;
using FgScanner.Ocr;
using Serilog;

namespace FgScanner.App.Services;

/// <summary>
/// Background OCR worker: drains the durable queue (PLAN §5.5). Runs for the app's lifetime,
/// wakes when jobs are enqueued, and re-exports a group's index files when its pages finish so
/// the OCRed column stays current. Replaced .md sidecars are archived through the Trash first.
/// </summary>
public sealed class OcrWorker : IDisposable
{
    private readonly OcrQueueService _queue;
    private readonly OcrPipeline _pipeline;
    private readonly IndexingService _indexing;
    private readonly TrashService _trash;
    private readonly ActiveGroupStore _activeGroup;
    private readonly AppSettingsService _settings;
    private readonly SemaphoreSlim _wake = new(0);
    private readonly CancellationTokenSource _stop = new();
    private Task? _loop;

    public OcrWorker(
        OcrQueueService queue,
        OcrPipeline pipeline,
        IndexingService indexing,
        TrashService trash,
        ActiveGroupStore activeGroup,
        AppSettingsService settings)
    {
        _queue = queue;
        _pipeline = pipeline;
        _indexing = indexing;
        _trash = trash;
        _activeGroup = activeGroup;
        _settings = settings;
        queue.JobsEnqueued += () => _wake.Release();
    }

    public void Start() => _loop = Task.Run(RunAsync);

    private async Task RunAsync()
    {
        var requeued = await _queue.ResetInFlightAsync(_stop.Token);
        if (requeued > 0)
        {
            Log.Information("Re-queued {Count} OCR job(s) from a previous run", requeued);
        }

        while (!_stop.IsCancellationRequested)
        {
            try
            {
                var touchedGroups = new HashSet<Guid>();
                while (await _queue.ClaimNextAsync(_stop.Token) is { } job)
                {
                    await ProcessAsync(job);
                    touchedGroups.Add(job.GroupId);
                }

                foreach (var groupId in touchedGroups)
                {
                    await _indexing.ReexportIfCommittedAsync(groupId, _stop.Token);
                }

                if (touchedGroups.Count > 0)
                {
                    System.Windows.Application.Current?.Dispatcher.Invoke(
                        _activeGroup.NotifyGroupContentChanged);
                }

                // Sleep until new work arrives (or a periodic safety poll).
                await _wake.WaitAsync(TimeSpan.FromSeconds(30), _stop.Token);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "OCR worker loop");
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

    private async Task ProcessAsync(OcrQueueService.ClaimedJob job)
    {
        try
        {
            if (!File.Exists(job.ImagePath))
            {
                await _queue.FailAsync(job.JobId, $"Image not found: {job.ImagePath}", _stop.Token);
                return;
            }

            // A re-run replaces the sidecar; the old one stays restorable (PLAN §5.2).
            var sidecar = Path.Combine(
                Path.GetDirectoryName(job.ImagePath)!,
                Path.GetFileNameWithoutExtension(job.ImagePath) + ".md");
            if (File.Exists(sidecar))
            {
                await _trash.ArchiveReplacedFileAsync(job.GroupId, sidecar, _stop.Token);
            }

            var languages = await _settings.GetAsync(AppSettingsService.OcrLanguagesKey, "eng", _stop.Token);
            var outcome = await _pipeline.ProcessPageAsync(
                job.ImagePath, ReadDpi(job.ImagePath), languages, _stop.Token);
            if (outcome.Success)
            {
                await _queue.CompleteAsync(
                    job.JobId, outcome.PlainText ?? "", outcome.MeanConfidence, _stop.Token);
            }
            else
            {
                await _queue.FailAsync(job.JobId, outcome.Error ?? "unknown", _stop.Token);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "OCR of {Image}", job.ImagePath);
            await _queue.FailAsync(job.JobId, ex.Message, _stop.Token);
        }
    }

    /// <summary>Tesseract's window sizes scale with DPI, so pass the image's declared resolution.</summary>
    private static int ReadDpi(string imagePath)
    {
        try
        {
            using var image = System.Drawing.Image.FromFile(imagePath);
            var dpi = (int)Math.Round(image.HorizontalResolution);
            return dpi is >= 70 and <= 1200 ? dpi : 300;
        }
        catch (Exception ex) when (ex is IOException or OutOfMemoryException)
        {
            return 300;
        }
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
