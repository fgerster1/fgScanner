using System.Runtime.InteropServices;
using FgScanner.Core.Sharing;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;

namespace FgScanner.App.Services;

/// <summary>
/// Route 1: Windows' own Share sheet. New Outlook registers as a share target, and the sheet
/// carries real file attachments rather than a `mailto:` that cannot (RFC 6068).
///
/// `DataTransferManager` is written for packaged apps and finds its window from the app's own
/// view, which an unpackaged WPF app does not have. `IDataTransferManagerInterop` is the
/// supported way across that gap: it takes an HWND. The activation factory is obtained through
/// `RoGetActivationFactory`, following the P/Invoke style of `SHFileOperationW` in
/// <see cref="RecycleBinDiscarder"/>.
///
/// The sheet is shown and this returns. Whether anything is sent is the operator's decision in
/// whichever target they pick (AC-5).
/// </summary>
internal static class ShareSheetRoute
{
    private const string ClassId = "Windows.ApplicationModel.DataTransfer.DataTransferManager";

    public static bool TryOpen(ShareRequest request)
    {
        var window = GetActiveWindow();
        if (window == IntPtr.Zero)
        {
            return false;
        }

        var interop = GetInterop();
        var managerId = typeof(DataTransferManager).GUID;
        var manager = interop.GetForWindow(window, ref managerId);

        // The files are read when the sheet asks for them, not now: the handler runs on the UI
        // thread while the sheet is open, and a deferral is what keeps it alive across the
        // asynchronous StorageFile lookups.
        void OnDataRequested(DataTransferManager _, DataRequestedEventArgs args)
        {
            manager.DataRequested -= OnDataRequested;
            var data = args.Request.Data;
            data.Properties.Title = request.Subject;
            data.Properties.Description = $"{request.FilePaths.Count} scanned page(s) from FG Scanner";

            var deferral = args.Request.GetDeferral();
            try
            {
                var items = new List<IStorageItem>(request.FilePaths.Count);
                foreach (var path in request.FilePaths)
                {
                    items.Add(StorageFile.GetFileFromPathAsync(path).AsTask().GetAwaiter().GetResult());
                }

                data.SetStorageItems(items);
            }
            finally
            {
                deferral.Complete();
            }
        }

        manager.DataRequested += OnDataRequested;
        interop.ShowShareUIForWindow(window);
        return true;
    }

    private static IDataTransferManagerInterop GetInterop()
    {
        var iid = typeof(IDataTransferManagerInterop).GUID;
        WindowsCreateString(ClassId, ClassId.Length, out var classId);
        try
        {
            RoGetActivationFactory(classId, ref iid, out var factory);
            try
            {
                return (IDataTransferManagerInterop)Marshal.GetObjectForIUnknown(factory);
            }
            finally
            {
                Marshal.Release(factory);
            }
        }
        finally
        {
            WindowsDeleteString(classId);
        }
    }

    [ComImport]
    [Guid("3A3DCD6C-3EAB-43DC-BCDE-45671CE800C8")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDataTransferManagerInterop
    {
        DataTransferManager GetForWindow([In] IntPtr appWindow, [In] ref Guid riid);

        void ShowShareUIForWindow([In] IntPtr appWindow);
    }

    [DllImport("user32")]
    private static extern IntPtr GetActiveWindow();

    [DllImport("combase", CharSet = CharSet.Unicode, PreserveSig = false)]
    private static extern void WindowsCreateString(
        [MarshalAs(UnmanagedType.LPWStr)] string sourceString, int length, out IntPtr hstring);

    [DllImport("combase", PreserveSig = false)]
    private static extern void WindowsDeleteString(IntPtr hstring);

    [DllImport("combase", PreserveSig = false)]
    private static extern void RoGetActivationFactory(
        IntPtr activatableClassId, [In] ref Guid iid, out IntPtr factory);
}
