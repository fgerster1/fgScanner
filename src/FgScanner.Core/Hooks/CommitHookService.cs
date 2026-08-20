using System.Diagnostics;
using System.Text;
using FgScanner.Core.Index;

namespace FgScanner.Core.Hooks;

/// <summary>What to run after a group commit. Empty/null entries are skipped.</summary>
public sealed record CommitHookOptions(string? CommandLine, string? WebhookUrl);

public sealed record CommitHookResult(
    int? CommandExitCode, string? CommandError, int? WebhookStatus, string? WebhookError)
{
    public bool RanAnything => CommandExitCode is not null || WebhookStatus is not null
        || CommandError is not null || WebhookError is not null;
}

/// <summary>
/// Post-commit automation (PLAN prompt 10, research-5 item 19): an optional command line with
/// $(group)/$(dir)/$(manifest) tokens, and an optional webhook POST whose JSON body is the same
/// manifest + rows payload as index.json. Failures are reported, never thrown — a broken hook
/// must not un-commit a group.
/// </summary>
public sealed class CommitHookService(HttpMessageHandler? httpHandler = null)
{
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan WebhookTimeout = TimeSpan.FromSeconds(15);

    public async Task<CommitHookResult> RunAsync(
        CommitHookOptions options, IndexExportData data, CancellationToken cancellationToken = default)
    {
        int? exitCode = null;
        string? commandError = null;
        if (!string.IsNullOrWhiteSpace(options.CommandLine))
        {
            (exitCode, commandError) = await RunCommandAsync(options.CommandLine, data, cancellationToken)
                .ConfigureAwait(false);
        }

        int? status = null;
        string? webhookError = null;
        if (!string.IsNullOrWhiteSpace(options.WebhookUrl))
        {
            (status, webhookError) = await PostWebhookAsync(options.WebhookUrl, data, cancellationToken)
                .ConfigureAwait(false);
        }

        return new CommitHookResult(exitCode, commandError, status, webhookError);
    }

    public static string ExpandTokens(string commandLine, IndexExportData data) => commandLine
        .Replace("$(group)", data.GroupName, StringComparison.OrdinalIgnoreCase)
        .Replace("$(dir)", data.GroupDirectory, StringComparison.OrdinalIgnoreCase)
        .Replace("$(manifest)", Path.Combine(data.GroupDirectory, "manifest.json"), StringComparison.OrdinalIgnoreCase);

    private static async Task<(int? ExitCode, string? Error)> RunCommandAsync(
        string commandLine, IndexExportData data, CancellationToken cancellationToken)
    {
        try
        {
            // The command is the user's own shell line (documented as cmd.exe syntax), so it goes
            // through cmd /c rather than ArgumentList — that is the feature, not an injection hole.
            var info = new ProcessStartInfo
            {
                FileName = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe",
                Arguments = "/d /c " + ExpandTokens(commandLine, data),
                WorkingDirectory = data.GroupDirectory,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
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
            timeoutCts.CancelAfter(CommandTimeout);
            try
            {
                await process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                    process.WaitForExit(5000);
                }
                catch (SystemException)
                {
                }

                cancellationToken.ThrowIfCancellationRequested();
                return (null, $"Command timed out after {CommandTimeout.TotalSeconds:0}s.");
            }

            return process.ExitCode == 0
                ? (0, null)
                : (process.ExitCode, stderr.Length > 0 ? stderr.ToString().Trim() : null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return (null, ex.Message);
        }
    }

    private async Task<(int? Status, string? Error)> PostWebhookAsync(
        string url, IndexExportData data, CancellationToken cancellationToken)
    {
        try
        {
            using var client = httpHandler is null
                ? new HttpClient()
                : new HttpClient(httpHandler, disposeHandler: false);
            client.Timeout = WebhookTimeout;
            using var content = new StringContent(IndexPayload.ToJson(data), Encoding.UTF8, "application/json");
            using var response = await client.PostAsync(new Uri(url), content, cancellationToken).ConfigureAwait(false);
            return ((int)response.StatusCode, response.IsSuccessStatusCode ? null : response.ReasonPhrase);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return (null, ex.Message);
        }
    }
}
