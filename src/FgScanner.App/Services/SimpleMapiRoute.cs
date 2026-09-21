using System.IO;
using System.Runtime.InteropServices;
using FgScanner.Core.Sharing;

namespace FgScanner.App.Services;

/// <summary>
/// Route 2: Simple MAPI. Opens a message in the station's registered mail client with the pages
/// attached, and stops there — `MAPI_DIALOG` is what makes the client show the message rather
/// than send it, and it is not optional here (AC-5).
///
/// Only ever reached when <see cref="MapiProbe"/> has already said a client is installed. Calling
/// this to find out would be the mistake the probe exists to prevent.
///
/// Re-implemented from the documented API, following the `SHFileOperationW` precedent in
/// <see cref="RecycleBinDiscarder"/>. NAPS2's own email code is GPL and was not read (CLAUDE.md).
/// </summary>
internal static class SimpleMapiRoute
{
    private const uint MapiDialog = 0x00000008;
    private const uint MapiLogonUi = 0x00000001;
    private const uint SuccessSuccess = 0;

    /// <summary>The operator closed the message without sending. It still opened, which is all this route promises.</summary>
    private const uint MapiUserAbort = 1;

    public static bool TryOpen(ShareRequest request)
    {
        var descriptorSize = Marshal.SizeOf<MapiFileDescriptor>();
        var files = Marshal.AllocHGlobal(descriptorSize * request.FilePaths.Count);
        try
        {
            for (var i = 0; i < request.FilePaths.Count; i++)
            {
                var path = Path.GetFullPath(request.FilePaths[i]);
                Marshal.StructureToPtr(
                    new MapiFileDescriptor
                    {
                        // -1 leaves the attachment at the end of the note rather than embedding it
                        // at a character position, which is what an attachment means here.
                        Position = -1,
                        PathName = path,
                        FileName = Path.GetFileName(path),
                    },
                    files + (i * descriptorSize),
                    fDeleteOld: false);
            }

            var message = new MapiMessage
            {
                Subject = request.Subject,
                FileCount = (uint)request.FilePaths.Count,
                Files = files,
            };

            var result = MAPISendMail(IntPtr.Zero, IntPtr.Zero, ref message, MapiDialog | MapiLogonUi, 0);
            return result is SuccessSuccess or MapiUserAbort;
        }
        finally
        {
            for (var i = 0; i < request.FilePaths.Count; i++)
            {
                Marshal.DestroyStructure<MapiFileDescriptor>(files + (i * descriptorSize));
            }

            Marshal.FreeHGlobal(files);
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MapiMessage
    {
        public uint Reserved;
        [MarshalAs(UnmanagedType.LPWStr)] public string? Subject;
        [MarshalAs(UnmanagedType.LPWStr)] public string? NoteText;
        [MarshalAs(UnmanagedType.LPWStr)] public string? MessageType;
        [MarshalAs(UnmanagedType.LPWStr)] public string? DateReceived;
        [MarshalAs(UnmanagedType.LPWStr)] public string? ConversationId;
        public uint Flags;
        public IntPtr Originator;
        public uint RecipientCount;
        public IntPtr Recipients;
        public uint FileCount;
        public IntPtr Files;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MapiFileDescriptor
    {
        public uint Reserved;
        public uint Flags;
        public int Position;
        [MarshalAs(UnmanagedType.LPWStr)] public string? PathName;
        [MarshalAs(UnmanagedType.LPWStr)] public string? FileName;
        public IntPtr FileType;
    }

    [DllImport("mapi32", EntryPoint = "MAPISendMailW", CharSet = CharSet.Unicode)]
    private static extern uint MAPISendMail(
        IntPtr session, IntPtr uiParam, ref MapiMessage message, uint flags, uint reserved);
}
