using System.Windows;
using System.Windows.Interop;
using FgScanner.Core.Sharing;
using Serilog;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;

namespace FgScanner.App.Services;

/// <summary>
/// Route 1: Windows' own Share sheet. New Outlook registers as a share target, and the sheet
/// carries real file attachments rather than a `mailto:` that cannot (RFC 6068).
///
/// `DataTransferManager` is written for packaged apps and finds its window from the app's own
/// view, which an unpackaged WPF app does not have. The Windows SDK projection's
/// `DataTransferManagerInterop` is the supported way across that gap: it takes an HWND. A
/// hand-declared `IDataTransferManagerInterop` came first and could never work — it asked for the
/// manager by the projected class's GUID, which is a hash of its name rather than the interface's
/// IID, and a raw COM object cannot be cast to a projected class in any case.
///
/// The window is the app's main window, read on the UI thread. `GetActiveWindow` answered for
/// whichever thread asked — a pool thread has none — and for the UI thread only while FG Scanner
/// happened to be in front, which it need not be after a long build.
///
/// The sheet is shown and this returns. Whether anything is sent is the operator's decision in
/// whichever target they pick (AC-5).
/// </summary>
public static class ShareSheetRoute
{
    public static bool TryOpen(ShareRequest request)
    {
        if (Application.Current?.Dispatcher is not { } dispatcher)
        {
            Log.Warning("The Share sheet was not tried: there is no application window to attach it to");
            return false;
        }

        return dispatcher.CheckAccess()
            ? OpenOnUiThread(request)
            : dispatcher.Invoke(() => OpenOnUiThread(request));
    }

    public static DataTransferManager ManagerFor(IntPtr window) =>
        DataTransferManagerInterop.GetForWindow(window);

    private static bool OpenOnUiThread(ShareRequest request)
    {
        var owner = Application.Current.MainWindow;
        var window = owner is null ? IntPtr.Zero : new WindowInteropHelper(owner).Handle;
        if (window == IntPtr.Zero)
        {
            Log.Warning("The Share sheet was not tried: the main window has no handle yet");
            return false;
        }

        var manager = ManagerFor(window);

        // The files are read when the sheet asks for them, not now: the handler runs on the UI
        // thread while the sheet is open, and a deferral is what keeps it alive across the
        // asynchronous StorageFile lookups.
        void OnDataRequested(DataTransferManager _, DataRequestedEventArgs args)
        {
            manager.DataRequested -= OnDataRequested;
            var data = args.Request.Data;
            data.Properties.Title = request.Subject;
            // No count: the request carries files, and "1 scanned page(s)" for a sixty-page PDF
            // was the same miscount the status line made.
            data.Properties.Description = "Scanned pages from FG Scanner";

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
        DataTransferManagerInterop.ShowShareUIForWindow(window);
        return true;
    }
}
