using System.Globalization;
using FgScanner.Core.Capture;
using FgScanner.Core.Hooks;
using FgScanner.Core.Index;

namespace FgScanner.Data;

/// <summary>
/// Bridges group commits to <see cref="CommitHookService"/> (PLAN prompt 10): reads the feature
/// flag and configured command/webhook from Settings, runs the hook, and journals the outcome in
/// the group. Hook problems never fail the commit.
/// </summary>
public sealed class CommitHookRunner(AppSettingsService settings, CommitHookService hooks)
{
    public const string CommandKey = "Hook.CommandLine";
    public const string WebhookUrlKey = "Hook.WebhookUrl";

    public async Task<CommitHookResult?> RunAsync(
        IndexExportData data, CancellationToken cancellationToken = default)
    {
        if (!await FeatureFlags.IsEnabledAsync(settings, FeatureFlags.CommitHook, cancellationToken)
            .ConfigureAwait(false))
        {
            return null;
        }

        var commandLine = await settings.GetAsync(CommandKey, "", cancellationToken).ConfigureAwait(false);
        var webhookUrl = await settings.GetAsync(WebhookUrlKey, "", cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(commandLine) && string.IsNullOrWhiteSpace(webhookUrl))
        {
            return null;
        }

        var result = await hooks.RunAsync(
            new CommitHookOptions(commandLine, webhookUrl), data, cancellationToken).ConfigureAwait(false);
        if (result.RanAnything)
        {
            await GroupJournal.AppendAsync(data.GroupDirectory, "commit hook: " + Describe(result), cancellationToken)
                .ConfigureAwait(false);
        }

        return result;
    }

    public static string Describe(CommitHookResult result)
    {
        var parts = new List<string>();
        if (result.CommandExitCode is { } exitCode)
        {
            parts.Add("command exit " + exitCode.ToString(CultureInfo.InvariantCulture));
        }

        if (result.CommandError is { } commandError)
        {
            parts.Add("command failed: " + commandError);
        }

        if (result.WebhookStatus is { } status)
        {
            parts.Add("webhook HTTP " + status.ToString(CultureInfo.InvariantCulture));
        }

        if (result.WebhookError is { } webhookError)
        {
            parts.Add("webhook failed: " + webhookError);
        }

        return parts.Count == 0 ? "nothing configured" : string.Join("; ", parts);
    }
}
