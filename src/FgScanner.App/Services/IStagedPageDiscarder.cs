using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using Serilog;

namespace FgScanner.App.Services;

/// <summary>Removes a scanned page file that was never saved to a group.</summary>
public interface IStagedPageDiscarder
{
    /// <summary>False, with the reason, when the file could not be removed or lies outside the session folder.</summary>
    bool TryDiscard(string sessionFolder, string filePath, out string reason);
}

/// <summary>
/// Sends a scanned page to the Windows Recycle Bin, so a mis-click on page 180 of 200 can be put
/// back by hand. Calls the shell directly: the Microsoft.VisualBasic recycle helper ships only with
/// Windows Forms, which this WPF app does not reference (SPEC-2026-003 §08).
/// </summary>
public sealed class RecycleBinDiscarder : IStagedPageDiscarder
{
    private const uint FoDelete = 3;
    private const ushort FofSilent = 0x0004;
    private const ushort FofNoConfirmation = 0x0010;
    private const ushort FofAllowUndo = 0x0040;
    private const ushort FofNoErrorUi = 0x0400;

    /// <summary>
    /// Where the Recycle Bin cannot take a file (turned off by policy, some network drives), the shell
    /// otherwise deletes it permanently without a word. This makes Windows ask first; a "No" comes
    /// back as an aborted operation and the page is reported as not recycled.
    /// </summary>
    private const ushort FofWantNukeWarning = 0x4000;

    public bool TryDiscard(string sessionFolder, string filePath, out string reason)
    {
        // The path comes from the recovery index on disk; a corrupt one must not aim this anywhere else.
        if (!IsInside(sessionFolder, filePath))
        {
            reason = $"{filePath} is not inside the scan session folder {sessionFolder}.";
            Log.Error("Refused to discard {File}: outside the scan session {Folder}", filePath, sessionFolder);
            return false;
        }

        // The shell expands wildcards in the paths it is given, so "*" would take every page, not one.
        if (filePath.AsSpan().IndexOfAny('*', '?') >= 0)
        {
            reason = $"{filePath} names more than one file.";
            Log.Error("Refused to discard {File}: the shell would expand its wildcard", filePath);
            return false;
        }

        var operation = new FileOperation
        {
            Function = FoDelete,
            // The shell reads a list of paths ended by an empty one; marshalling adds the final null.
            From = Path.GetFullPath(filePath) + "\0",
            Flags = FofSilent | FofNoConfirmation | FofAllowUndo | FofNoErrorUi | FofWantNukeWarning,
        };
        var result = SHFileOperation(ref operation);
        if (result != 0)
        {
            reason = "Windows could not move it to the Recycle Bin (code 0x"
                + result.ToString("X", CultureInfo.InvariantCulture) + ").";
            return false;
        }

        if (operation.AnyOperationsAborted)
        {
            reason = "Moving it to the Recycle Bin was cancelled.";
            return false;
        }

        reason = "";
        return true;
    }

    private static bool IsInside(string folder, string path)
    {
        // The trailing separator keeps a sibling like "abcd" from passing as inside "abc".
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder)) + Path.DirectorySeparatorChar;
        var full = Path.GetFullPath(path);
        return full.Length > root.Length && full.StartsWith(root, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// SHFILEOPSTRUCTW. Default packing matches shell32 on x64; the x86 header packs it to 1, and this
    /// app ships x64 only.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct FileOperation
    {
        public IntPtr Window;
        public uint Function;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string From;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string? To;
        public ushort Flags;
        [MarshalAs(UnmanagedType.Bool)]
        public bool AnyOperationsAborted;
        public IntPtr NameMappings;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string? ProgressTitle;
    }

    [DllImport("shell32", EntryPoint = "SHFileOperationW", CharSet = CharSet.Unicode)]
    private static extern int SHFileOperation(ref FileOperation operation);
}
