using System.Diagnostics;
using System.Windows;
using FgScanner.Data;
using NetSparkleUpdater;
using NetSparkleUpdater.Enums;
using NetSparkleUpdater.SignatureVerifiers;
using Serilog;

namespace FgScanner.App.Services;

/// <summary>
/// Auto-update over an Ed25519-signed appcast on GitHub Releases (PLAN prompt 9). Quiet check on
/// startup, honoring the NoUpdatePrompt setting; on accept the download is verified and
/// <see cref="UpdateInstaller"/> runs the Inno installer /VERYSILENT /NORESTART. Strict Ed25519
/// verification: an unsigned appcast is never accepted.
/// </summary>
/// <param name="lastBackup">The station's last backup, named in the offer (SPEC-SCANNER-2026-011).</param>
/// <param name="backUp">Opens the Back up window on the UI thread; true when it ended with OK.</param>
public sealed class UpdateService(
    AppSettingsService settings,
    Func<bool> captureInHand,
    Func<FgScanner.Core.Station.LastBackupRecord?>? lastBackup = null,
    Func<bool>? backUp = null) : IDisposable
{
    public const string NoUpdatePromptKey = "Update.NoUpdatePrompt";
    public const string AppcastUrl =
        "https://github.com/fgerster1/fgScanner/releases/latest/download/appcast.xml";

    /// <summary>
    /// Points a rehearsal station at one release's own appcast — a pre-release, which
    /// <c>releases/latest</c> never serves, so no other station is offered it (SPEC-SCANNER-2026-010
    /// Q-C3). Only this repository's release downloads are honoured; the signature check is the same.
    /// </summary>
    public const string AppcastOverrideVariable = "FGSCANNER_APPCAST_URL";

    private const string ReleaseDownloads = "/fgerster1/fgScanner/releases/download/";

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
            var appcast = ResolveAppcastUrl(Environment.GetEnvironmentVariable(AppcastOverrideVariable));
            if (appcast != AppcastUrl)
            {
                Log.Information("Update check uses the rehearsal appcast {Appcast}", appcast);
            }

            _sparkle = new SparkleUpdater(
                appcast, new Ed25519Checker(SecurityMode.Strict, Ed25519PublicKey))
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
                UpdateOffer.Text(latest.Version ?? "?", typeof(App).Assembly.GetName().Version?.ToString(3) ?? "?", lastBackup?.Invoke()),
                "FG Scanner update",
                System.Windows.MessageBoxButton.YesNoCancel,
                System.Windows.MessageBoxImage.Information);
            var choice = answer switch
            {
                System.Windows.MessageBoxResult.Yes => UpdateChoice.BackUpFirst,
                System.Windows.MessageBoxResult.No => UpdateChoice.InstallNow,
                _ => UpdateChoice.Later,
            };
            if (!UpdateOffer.InstallAfter(choice, () => backUp is not null && Application.Current.Dispatcher.Invoke(backUp)))
            {
                Log.Information("Update {Version} not installed now ({Choice})", latest.Version, choice);
                return;
            }

            // NetSparkle's own InstallUpdate is never called: its generated script is what failed
            // on every station through 0.7.1 (UpdateInstaller explains).
            var installer = new UpdateInstaller(
                Process.Start,
                () => Application.Current.Dispatcher.Invoke(() =>
                {
                    Application.Current.MainWindow?.Close();
                    Application.Current.Shutdown();
                }),
                () => Application.Current.Dispatcher.Invoke(captureInHand),
                UpdateInstaller.DefaultUpdateFolder,
                Task.Delay,
                path => latest.DownloadSignature is { } signature
                    && _sparkle.SignatureVerifier.VerifySignatureOfFile(signature, path) == ValidationResult.Valid);
            _sparkle.DownloadFinished += (item, path) => _ = InstallAsync(installer, path, item.Version ?? "update");
            await _sparkle.InitAndBeginDownload(latest);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Update check failed (will retry next launch)");
        }
    }

    /// <summary>The appcast to read: the override when it names one of this repository's release
    /// downloads over https, otherwise the Latest release's.</summary>
    public static string ResolveAppcastUrl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)
            || !Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps
            || !uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase)
            || !uri.AbsolutePath.StartsWith(ReleaseDownloads, StringComparison.Ordinal)
            || uri.OriginalString.Contains("..", StringComparison.Ordinal))
        {
            return AppcastUrl;
        }

        return value;
    }

    private static async Task InstallAsync(UpdateInstaller installer, string path, string version)
    {
        try
        {
            await installer.InstallAsync(path, version);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Update {Version} could not be started (will be offered again next launch)", version);
        }
    }

    public void Dispose() => _sparkle?.Dispose();
}
