using System.Diagnostics;
using System.Globalization;
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
/// How a page is turned. <paramref name="RotateClockwiseDegrees"/> is what must be APPLIED to make
/// the page upright, matching <c>PageEdit.Rotate</c>'s clockwise convention, so the two compose
/// without a sign flip.
/// </summary>
public sealed record OrientationResult(int RotateClockwiseDegrees, double Confidence);

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

        var run = await RunAsync(
            [
                Path.GetFullPath(imagePath),
                Path.GetFullPath(outputBase),
                "--dpi", dpi.ToString(System.Globalization.CultureInfo.InvariantCulture),
                "--oem", "1",
                "--psm", "3",
                "-l", languages,

                // -c parameters instead of the "pdf hocr tsv" config names: those are files under
                // tessdata/configs/, which the slim language download does not include.
                "-c", "tessedit_create_pdf=1",
                "-c", "tessedit_create_hocr=1",
                "-c", "tessedit_create_tsv=1",
            ],
            cancellationToken).ConfigureAwait(false);

        if (run.MissingExe)
        {
            return new OcrRunResult(false, null, null, null, $"tesseract.exe not found at {_exePath}.", TimeSpan.Zero);
        }

        if (run.TimedOut)
        {
            return new OcrRunResult(
                false, null, null, null, $"Timed out after {_timeout.TotalSeconds:0}s.", run.Duration);
        }

        if (run.ExitCode != 0)
        {
            return new OcrRunResult(
                false, null, null, null,
                $"tesseract exited with {run.ExitCode}: {run.StdErr.Trim()}", run.Duration);
        }

        return new OcrRunResult(
            true, outputBase + ".tsv", outputBase + ".hocr", outputBase + ".pdf", null, run.Duration);
    }

    [GeneratedRegex(@"^Rotate:\s*(\d+)", RegexOptions.Multiline)]
    private static partial Regex RotatePattern();

    [GeneratedRegex(@"^Orientation confidence:\s*([0-9.]+)", RegexOptions.Multiline)]
    private static partial Regex OrientationConfidencePattern();

    /// <summary>
    /// Which way up the page is, via Tesseract's OSD pass (<c>--psm 0</c>). Null means "cannot
    /// say" — a blank sheet, too little text, a missing model — and must never be read as
    /// "upright", because silently keeping a misfed page is the failure this exists to prevent.
    /// No <c>--oem</c>: osd.traineddata is a legacy-format model and OSD refuses under LSTM-only.
    /// </summary>
    public async Task<OrientationResult?> DetectOrientationAsync(
        string imagePath, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(imagePath))
        {
            return null;
        }

        var run = await RunAsync(
            [Path.GetFullPath(imagePath), "stdout", "--psm", "0"], cancellationToken).ConfigureAwait(false);
        if (run.MissingExe || run.TimedOut || run.ExitCode != 0)
        {
            return null;
        }

        // OSD writes its report to stdout; older builds echo it on stderr instead.
        var report = run.StdOut + run.StdErr;
        var rotate = RotatePattern().Match(report);
        if (!rotate.Success
            || !int.TryParse(rotate.Groups[1].Value, CultureInfo.InvariantCulture, out var degrees))
        {
            return null;
        }

        var confidence = OrientationConfidencePattern().Match(report);
        _ = double.TryParse(
            confidence.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var score);
        return new OrientationResult(((degrees % 360) + 360) % 360, score);
    }

    private sealed record ProcessOutput(
        bool MissingExe, bool TimedOut, int ExitCode, string StdOut, string StdErr, TimeSpan Duration);

    /// <summary>
    /// One tesseract invocation under the safety rules (PLAN §5.5, research-3): ArgumentList only
    /// (no shell, no string concat), absolute exe path, child-only environment, concurrent
    /// stdout/stderr drains, kill on timeout.
    /// </summary>
    private async Task<ProcessOutput> RunAsync(
        IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        if (!File.Exists(_exePath))
        {
            return new ProcessOutput(true, false, 0, "", "", TimeSpan.Zero);
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
            foreach (var argument in arguments)
            {
                info.ArgumentList.Add(argument);
            }

            info.Environment["TESSDATA_PREFIX"] = Path.GetFullPath(_tessdataDir);
            info.Environment["OMP_THREAD_LIMIT"] = "1";

            using var process = new Process { StartInfo = info };
            var stdout = new StringBuilder();
            var stderr = new StringBuilder();
            process.OutputDataReceived += (_, e) =>
            {
                if (e.Data is not null)
                {
                    stdout.AppendLine(e.Data);
                }
            };
            process.ErrorDataReceived += (_, e) =>
            {
                if (e.Data is not null)
                {
                    stderr.AppendLine(e.Data);
                }
            };
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
                    // Kill is asynchronous — wait for the exit so the child's output-file
                    // handles are released before callers clean up the work directory.
                    process.WaitForExit(5000);
                }
                catch (SystemException)
                {
                }

                cancellationToken.ThrowIfCancellationRequested();
                return new ProcessOutput(false, true, 0, stdout.ToString(), stderr.ToString(), stopwatch.Elapsed);
            }

            return new ProcessOutput(
                false, false, process.ExitCode, stdout.ToString(), stderr.ToString(), stopwatch.Elapsed);
        }
        finally
        {
            _pool.Release();
        }
    }

    public void Dispose() => _pool.Dispose();
}
