using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace FgScanner.Ocr;

public sealed record OcrRunResult(
    bool Success,
    string? TsvPath,
    string? HocrPath,
    string? PdfPath,
    string? Error,
    TimeSpan Duration);

/// <summary>
/// Shells out to tesseract.exe (PLAN §5.5, research-3 safety rules): ArgumentList only (no shell,
/// no string concat), absolute exe path, whitelisted language codes, TESSDATA_PREFIX and
/// OMP_THREAD_LIMIT=1 on the child environment only, concurrent stdout/stderr drains, kill on
/// timeout. One recognition pass emits pdf + hocr + tsv together.
/// </summary>
public sealed partial class TesseractRunner(
    string? exePath = null,
    string? tessdataDir = null,
    int? maxParallelism = null,
    TimeSpan? timeout = null) : IDisposable
{
    [GeneratedRegex("^[a-z]{3}(\\+[a-z]{3})*$")]
    private static partial Regex LanguagePattern();

    private readonly string _exePath = exePath ?? TesseractPaths.DefaultExePath;
    private readonly string _tessdataDir = tessdataDir ?? TesseractPaths.DefaultUserTessdataDir;
    private readonly TimeSpan _timeout = timeout ?? TimeSpan.FromMinutes(2);

    // Physical cores ≈ logical/2 on SMT machines; one tesseract per physical core is the
    // researched throughput sweet spot (each child is pinned to one thread via OMP_THREAD_LIMIT).
    private readonly SemaphoreSlim _pool = new(
        maxParallelism ?? Math.Max(1, Environment.ProcessorCount / 2));

    public async Task<OcrRunResult> RecognizeAsync(
        string imagePath, string outputBase, int dpi, string languages = "eng",
        CancellationToken cancellationToken = default)
    {
        if (!LanguagePattern().IsMatch(languages))
        {
            throw new ArgumentException($"Invalid language string \"{languages}\".", nameof(languages));
        }

        if (!File.Exists(_exePath))
        {
            return new OcrRunResult(false, null, null, null, $"tesseract.exe not found at {_exePath}.", TimeSpan.Zero);
        }

        await _pool.WaitAsync(cancellationToken).ConfigureAwait(false);
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var info = new ProcessStartInfo
            {
                FileName = Path.GetFullPath(_exePath),
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            info.ArgumentList.Add(Path.GetFullPath(imagePath));
            info.ArgumentList.Add(Path.GetFullPath(outputBase));
            info.ArgumentList.Add("--dpi");
            info.ArgumentList.Add(dpi.ToString(System.Globalization.CultureInfo.InvariantCulture));
            info.ArgumentList.Add("--oem");
            info.ArgumentList.Add("1");
            info.ArgumentList.Add("--psm");
            info.ArgumentList.Add("3");
            info.ArgumentList.Add("-l");
            info.ArgumentList.Add(languages);
            // -c parameters instead of the "pdf hocr tsv" config names: those are files under
            // tessdata/configs/, which the slim language download does not include.
            info.ArgumentList.Add("-c");
            info.ArgumentList.Add("tessedit_create_pdf=1");
            info.ArgumentList.Add("-c");
            info.ArgumentList.Add("tessedit_create_hocr=1");
            info.ArgumentList.Add("-c");
            info.ArgumentList.Add("tessedit_create_tsv=1");
            info.Environment["TESSDATA_PREFIX"] = Path.GetFullPath(_tessdataDir);
            info.Environment["OMP_THREAD_LIMIT"] = "1";

            using var process = new Process { StartInfo = info };
            var stderr = new StringBuilder();
            process.ErrorDataReceived += (_, e) =>
            {
                if (e.Data is not null)
                {
                    stderr.AppendLine(e.Data);
                }
            };
            process.OutputDataReceived += (_, _) => { };
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(_timeout);
            try
            {
                await process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch (InvalidOperationException)
                {
                }

                cancellationToken.ThrowIfCancellationRequested();
                return new OcrRunResult(
                    false, null, null, null, $"Timed out after {_timeout.TotalSeconds:0}s.", stopwatch.Elapsed);
            }

            if (process.ExitCode != 0)
            {
                return new OcrRunResult(
                    false, null, null, null,
                    $"tesseract exited with {process.ExitCode}: {stderr.ToString().Trim()}", stopwatch.Elapsed);
            }

            return new OcrRunResult(
                true, outputBase + ".tsv", outputBase + ".hocr", outputBase + ".pdf", null, stopwatch.Elapsed);
        }
        finally
        {
            _pool.Release();
        }
    }

    public void Dispose() => _pool.Dispose();
}
