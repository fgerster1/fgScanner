namespace FgScanner.Core.Index;

/// <summary>
/// Atomic write with lock-retry (PLAN §5.2): content lands in a temp file in the same directory,
/// then replaces the target. If Excel (or sync software) holds the target, we retry with backoff;
/// a persistent lock is reported — never an exception, and never a torn file.
/// </summary>
public sealed class AtomicFileWriter(int maxAttempts = 5, TimeSpan? initialDelay = null, TimeProvider? time = null)
{
    private readonly TimeSpan _initialDelay = initialDelay ?? TimeSpan.FromMilliseconds(200);
    private readonly TimeProvider _time = time ?? TimeProvider.System;

    public async Task<(ExportOutcome Outcome, string? Message)> WriteAsync(
        string targetPath, Func<Stream, Task> writeContent, CancellationToken cancellationToken = default)
    {
        var tempPath = targetPath + ".tmp";
        try
        {
            await using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                await writeContent(stream).ConfigureAwait(false);
            }

            var delay = _initialDelay;
            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    File.Move(tempPath, targetPath, overwrite: true);
                    return (ExportOutcome.Success, null);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException && attempt < maxAttempts)
                {
                    await Task.Delay(delay, _time, cancellationToken).ConfigureAwait(false);
                    delay *= 2;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    return (ExportOutcome.Locked,
                        $"\"{Path.GetFileName(targetPath)}\" is open in another program ({ex.Message}). " +
                        "The data is safe in the database; the file will refresh on the next export.");
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return (ExportOutcome.Error, ex.Message);
        }
        finally
        {
            try
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }
            catch (IOException)
            {
            }
        }
    }
}
