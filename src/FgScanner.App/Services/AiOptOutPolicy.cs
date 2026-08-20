using Microsoft.Win32;

namespace FgScanner.App.Services;

/// <summary>
/// Machine-wide AI kill switch written by the installer's privacy checkbox (PLAN §4).
/// When set, the AI feature is fully disabled regardless of stored keys.
/// </summary>
public static class AiOptOutPolicy
{
    public static bool IsOptedOut
    {
        get
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\FGScanner");
                return key?.GetValue("AiOptOut") is int value && value != 0;
            }
            catch (System.Security.SecurityException)
            {
                return false;
            }
        }
    }
}
