using FgScanner.Data;
using NetSparkleUpdater;
using NetSparkleUpdater.Enums;
using NetSparkleUpdater.SignatureVerifiers;
using Serilog;

namespace FgScanner.App.Services;

/// <summary>
/// Auto-update over an Ed25519-signed appcast on GitHub Releases (PLAN prompt 9). Quiet check on
/// startup, honoring the NoUpdatePrompt setting; on accept the Inno installer runs
/// /VERYSILENT /NORESTART. Strict Ed25519 verification: an unsigned appcast is never accepted.
/// </summary>
public sealed class UpdateService(AppSettingsService settings) : IDisposable
{
    public const string NoUpdatePromptKey = "Update.NoUpdatePrompt";
    public const string AppcastUrl =
        "https://github.com/fgerster1/fgScanner/releases/latest/download/appcast.xml";

    /// <summary>Ed25519 public key (base64) — generated 2026-08-20 per docs/release.md; the matching
    /// private key lives only in the SPARKLE_ED25519_PRIVATE_KEY GitHub secret.</summary>
    public const string Ed25519PublicKey = "AYGJKjx0kHdK1dPayOwD71kSEa4yS7j0iVMofJ9RVm4=";

    private SparkleUpdater? _sparkle;

    public async Task CheckOnStartupAsync()
    {
        if (Ed25519PublicKey.StartsWith("REPLACE", StringComparison.Ordinal))
        {
            return; // no signing key published yet — never accept unsigned updates
        }

        if (await settings.GetAsync(NoUpdatePromptKey, "false") == "true")
        {
            return;
        }

        try
        {
            _sparkle = new SparkleUpdater(
                AppcastUrl, new Ed25519Checker(SecurityMode.Strict, Ed25519PublicKey))
            {
                UIFactory = null,
                RelaunchAfterUpdate = false,
                CustomInstallerArguments = "/VERYSILENT /NORESTART",
            };
            var updates = await _sparkle.CheckForUpdatesQuietly();
            if (updates.Status != UpdateStatus.UpdateAvailable || updates.Updates.Count == 0)
            {
                return;
            }

            var latest = updates.Updates[0];
            var answer = System.Windows.MessageBox.Show(
                $"FG Scanner {latest.Version} is available (you have {typeof(App).Assembly.GetName().Version?.ToString(3)}).\n\n" +
                "Download and install now? The app closes briefly during the update.",
                "FG Scanner update",
                System.Windows.MessageBoxButton.YesNo,
                System.Windows.MessageBoxImage.Information);
            if (answer != System.Windows.MessageBoxResult.Yes)
            {
                return;
            }

            _sparkle.DownloadFinished += (item, path) => _sparkle.InstallUpdate(item, path);
            await _sparkle.InitAndBeginDownload(latest);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Update check failed (will retry next launch)");
        }
    }

    public void Dispose() => _sparkle?.Dispose();
}
