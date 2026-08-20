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
public sealed class CredentialStore(string? fallbackDirectory = null, bool useCredentialManager = true)
{
    private const string TargetName = "FGScanner:GeminiApiKey";

    private readonly string _fallbackFile = Path.Combine(
        fallbackDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FGScanner"),
        "ai.key.bin");

    public string? GetKey()
    {
        if (useCredentialManager && TryReadCredentialManager(out var key))
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

    public void SetKey(string key)
    {
        if (useCredentialManager && TryWriteCredentialManager(key))
        {
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(_fallbackFile)!);
        File.WriteAllBytes(
            _fallbackFile,
            ProtectedData.Protect(Encoding.UTF8.GetBytes(key), null, DataProtectionScope.CurrentUser));
    }

    public void ClearKey()
    {
        if (useCredentialManager)
        {
            _ = CredDelete(TargetName, CredTypeGeneric, 0);
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

    private static bool TryReadCredentialManager(out string? key)
    {
        key = null;
        if (!OperatingSystem.IsWindows() || !CredRead(TargetName, CredTypeGeneric, 0, out var handle))
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

    private static bool TryWriteCredentialManager(string key)
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        var blob = Encoding.UTF8.GetBytes(key);
        var blobPtr = Marshal.AllocHGlobal(blob.Length);
        var targetPtr = Marshal.StringToHGlobalUni(TargetName);
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
            return CredWrite(ref credential, 0);
        }
        finally
        {
            Marshal.FreeHGlobal(blobPtr);
            Marshal.FreeHGlobal(targetPtr);
            Marshal.FreeHGlobal(userPtr);
        }
    }
}
