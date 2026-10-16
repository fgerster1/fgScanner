using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using Serilog;

namespace FgScanner.App.Services;

/// <summary>
/// Runs a downloaded, signature-checked installer (SPEC-SCANNER-2026-010 AC-11). Through 0.7.1
/// NetSparkle's own install step did this with a generated <c>.cmd</c> that waited 90 seconds for
/// an app nothing closed, then ran the download — saved with no extension under a profile path
/// with a space — unquoted, so cmd split it at the space and nothing was installed. Here the file
/// is copied under its real name and started directly; Windows raises the administrator prompt,
/// and the app closes only once the installer is running.
/// </summary>
/// <param name="start">Starts a process (<see cref="Process.Start(ProcessStartInfo)"/>).</param>
/// <param name="close">Closes the app through its normal close path.</param>
/// <param name="captureInHand">A sheet or a stack is mid-capture; closing now would strand it.</param>
/// <param name="delay">Waits between checks of <paramref name="captureInHand"/>.</param>
public sealed class UpdateInstaller(
    Func<ProcessStartInfo, Process?> start,
    Action close,
    Func<bool> captureInHand,
    string updateFolder,
    Func<TimeSpan, Task> delay)
{
    public const string InstallerArguments = "/VERYSILENT /NORESTART";

    /// <summary>ERROR_CANCELLED: the operator answered No to the administrator prompt.</summary>
    private const int Cancelled = 1223;

    public static string DefaultUpdateFolder => Path.Combine(Path.GetTempPath(), "FGScanner", "update");

    /// <returns>True when the installer is running and the app has been asked to close.</returns>
    public async Task<bool> InstallAsync(string downloadedPath, string version)
    {
        // The download takes a while, and Jim may have started scanning; an update is never worth
        // a half-captured sheet.
        while (captureInHand())
        {
            await delay(TimeSpan.FromSeconds(5)).ConfigureAwait(true);
        }

        Directory.CreateDirectory(updateFolder);
        var installer = Path.Combine(updateFolder, $"fgscanner-{Safe(version)}-win-x64.exe");
        var staging = installer + ".tmp";
        File.Copy(downloadedPath, staging, overwrite: true);
        File.Move(staging, installer, overwrite: true);

        try
        {
            start(new ProcessStartInfo(installer)
            {
                Arguments = InstallerArguments,
                UseShellExecute = true,
            });
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == Cancelled)
        {
            Log.Information("Update {Version} not installed: the administrator prompt was refused (offered again next start)", version);
            TryDelete(installer);
            return false;
        }

        Log.Information("Update {Version} installer started from {Installer}; closing so it can replace the app", version, installer);
        close();
        return true;
    }

    /// <summary>The version comes from a signed appcast, but it still names a file: keep it to
    /// characters a version has, so it can never reach outside the update folder.</summary>
    private static string Safe(string version)
    {
        var kept = new string([.. version.Where(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-')]).Trim('.');
        return kept.Length == 0 || kept.Contains("..", StringComparison.Ordinal) ? "update" : kept;
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
