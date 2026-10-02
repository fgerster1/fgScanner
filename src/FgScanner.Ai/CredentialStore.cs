using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;

namespace FgScanner.Ai;

/// <summary>
/// API-key storage (PLAN §5.6): Windows Credential Manager first (user-visible and revocable in
/// the Windows UI), DPAPI CurrentUser file as fallback. The key is never logged and never leaves
/// this class except to authenticate calls.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class CredentialStore(
    string? fallbackDirectory = null,
    bool useCredentialManager = true,
    string targetName = CredentialStore.DefaultTargetName)
{
    // A test passes its own target so the real Credential Manager path runs without touching
    // the user's key.
    public const string DefaultTargetName = "FGScanner:GeminiApiKey";

    private readonly string _fallbackFile = Path.Combine(
        fallbackDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FGScanner"),
        "ai.key.bin");

    public string? GetKey()
    {
        if (useCredentialManager && TryReadCredentialManager(targetName, out var key))
        {
            return key;
        }

        try
        {
            if (File.Exists(_fallbackFile))
            {
                var bytes = ProtectedData.Unprotect(
                    File.ReadAllBytes(_fallbackFile), null, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(bytes);
            }
        }
        catch (CryptographicException)
        {
        }
        catch (IOException)
        {
        }

        return null;
    }

    /// <summary>Stores the key and says where it went, so the caller never claims a store it
    /// did not use.</summary>
    public KeyStoreResult SetKey(string key)
    {
        var credentialManagerError = 0;
        if (useCredentialManager)
        {
            if (TryWriteCredentialManager(targetName, key, out credentialManagerError))
            {
                // 2026-09-28: Settings said "stored" and Credential Manager held nothing.
                if (GetKey() != key)
                {
                    throw new InvalidOperationException(
                        "The key was not stored: Windows Credential Manager accepted it but does not "
                        + "return it when asked. Try again.");
                }

                return new KeyStoreResult(InCredentialManager: true, CredentialManagerError: 0);
            }
        }

        Directory.CreateDirectory(Path.GetDirectoryName(_fallbackFile)!);
        File.WriteAllBytes(
            _fallbackFile,
            ProtectedData.Protect(Encoding.UTF8.GetBytes(key), null, DataProtectionScope.CurrentUser));

        // GetKey prefers Credential Manager, so an older key left there would still be the one
        // in use. Never report a store the next read does not return.
        if (GetKey() != key)
        {
            throw new InvalidOperationException(
                "The key was not stored: Windows Credential Manager refused it (error "
                + credentialManagerError.ToString(System.Globalization.CultureInfo.InvariantCulture)
                + ") and an older key there would still be used. Clear the stored key, then try again.");
        }

        return new KeyStoreResult(InCredentialManager: false, credentialManagerError);
    }

    public void ClearKey()
    {
        if (useCredentialManager)
        {
            _ = CredDelete(targetName, CredTypeGeneric, 0);
        }

        try
        {
            if (File.Exists(_fallbackFile))
            {
                File.Delete(_fallbackFile);
            }
        }
        catch (IOException)
        {
        }
    }

    public bool HasKey => GetKey() is not null;

    // ---- Credential Manager P/Invoke (advapi32) ----

    private const int CredTypeGeneric = 1;
    private const int CredPersistLocalMachine = 2;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct Credential
    {
        public int Flags;
        public int Type;
        public IntPtr TargetName;
        public IntPtr Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public int CredentialBlobSize;
        public IntPtr CredentialBlob;
        public int Persist;
        public int AttributeCount;
        public IntPtr Attributes;
        public IntPtr TargetAlias;
        public IntPtr UserName;
    }

    [DllImport("advapi32", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "CredReadW")]
    private static extern bool CredRead(string target, int type, int flags, out IntPtr credential);

    [DllImport("advapi32", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "CredWriteW")]
    private static extern bool CredWrite(ref Credential credential, int flags);

    [DllImport("advapi32", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "CredDeleteW")]
    private static extern bool CredDelete(string target, int type, int flags);

    [DllImport("advapi32", EntryPoint = "CredFree")]
    private static extern void CredFree(IntPtr buffer);

    private static bool TryReadCredentialManager(string targetName, out string? key)
    {
        key = null;
        if (!OperatingSystem.IsWindows() || !CredRead(targetName, CredTypeGeneric, 0, out var handle))
        {
            return false;
        }

        try
        {
            var credential = Marshal.PtrToStructure<Credential>(handle);
            if (credential.CredentialBlobSize <= 0)
            {
                return false;
            }

            var bytes = new byte[credential.CredentialBlobSize];
            Marshal.Copy(credential.CredentialBlob, bytes, 0, bytes.Length);
            key = Encoding.UTF8.GetString(bytes);
            return true;
        }
        finally
        {
            CredFree(handle);
        }
    }

    private static bool TryWriteCredentialManager(string targetName, string key, out int error)
    {
        error = 0;
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        var blob = Encoding.UTF8.GetBytes(key);
        var blobPtr = Marshal.AllocHGlobal(blob.Length);
        var targetPtr = Marshal.StringToHGlobalUni(targetName);
        var userPtr = Marshal.StringToHGlobalUni("api-key");
        try
        {
            Marshal.Copy(blob, 0, blobPtr, blob.Length);
            var credential = new Credential
            {
                Type = CredTypeGeneric,
                TargetName = targetPtr,
                CredentialBlobSize = blob.Length,
                CredentialBlob = blobPtr,
                Persist = CredPersistLocalMachine,
                UserName = userPtr,
            };
            if (CredWrite(ref credential, 0))
            {
                return true;
            }

            error = Marshal.GetLastPInvokeError();
            return false;
        }
        finally
        {
            Marshal.FreeHGlobal(blobPtr);
            Marshal.FreeHGlobal(targetPtr);
            Marshal.FreeHGlobal(userPtr);
        }
    }
}

/// <summary>Where <see cref="CredentialStore.SetKey"/> put the key; the error is Credential
/// Manager's Win32 code when it refused the key and the encrypted file was used instead.</summary>
public sealed record KeyStoreResult(bool InCredentialManager, int CredentialManagerError);
