using System.IO;
using Microsoft.Win32;

namespace FgScanner.App.Services;

/// <summary>Reads a registry string, so the probe can be tested without this machine's registry.</summary>
public interface IRegistryReader
{
    /// <summary>The value, or null when the key or value is absent. A null name reads the default.</summary>
    string? ReadString(string keyPath, string? valueName);
}

/// <summary>Reads HKLM for real. Never writes.</summary>
public sealed class WindowsRegistryReader : IRegistryReader
{
    public string? ReadString(string keyPath, string? valueName) =>
        Registry.GetValue(keyPath, valueName ?? "", null) as string;
}

/// <summary>
/// Answers whether Simple MAPI is worth offering — by asking the registry, never by calling MAPI
/// to find out (SPEC-2026-007 §08, AC-7). `MAPISendMailW` on a station with no client does not
/// return a tidy error: it can raise a modal Windows dialog or spin up a COM server in front of
/// the operator, which is not something a fallback chain may do while deciding what to try next.
///
/// All three conditions are false on this station, which is why the route order puts the Share
/// sheet first (research `2026-08-30-quick-scan-spikes.md:75, :117-119`).
/// </summary>
public sealed class MapiProbe(IRegistryReader registry, Func<string, bool>? fileExists = null)
{
    private const string MailClients = @"HKEY_LOCAL_MACHINE\SOFTWARE\Clients\Mail";
    private const string Subsystem = @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows Messaging Subsystem";

    private readonly Func<string, bool> _fileExists = fileExists ?? File.Exists;

    /// <summary>
    /// True only when all three hold:
    ///
    ///   1. `HKLM\SOFTWARE\Clients\Mail` has a non-empty default value — a client is registered.
    ///   2. That client's subkey has a `DLLPathEx` (or the older `DLLPath`) naming a file that
    ///      exists — the registration is not a leftover pointing at something uninstalled.
    ///   3. `HKLM\SOFTWARE\Microsoft\Windows Messaging Subsystem` has a non-empty `MAPI` value —
    ///      the subsystem itself is switched on.
    /// </summary>
    public bool IsAvailable()
    {
        var client = registry.ReadString(MailClients, null);
        if (string.IsNullOrWhiteSpace(client))
        {
            return false;
        }

        var clientKey = $@"{MailClients}\{client}";
        var library = registry.ReadString(clientKey, "DLLPathEx")
            ?? registry.ReadString(clientKey, "DLLPath");
        if (string.IsNullOrWhiteSpace(library) || !_fileExists(library))
        {
            return false;
        }

        return !string.IsNullOrWhiteSpace(registry.ReadString(Subsystem, "MAPI"));
    }
}
