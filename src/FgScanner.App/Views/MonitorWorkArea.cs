using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace FgScanner.App.Views;

/// <summary>
/// The usable area (screen minus taskbar) of the monitor a window is on, in layout units.
/// SystemParameters.WorkArea only describes the primary monitor, so a window opening on a second,
/// smaller screen would be sized for the wrong one.
/// </summary>
internal static class MonitorWorkArea
{
    private const uint MonitorDefaultToNearest = 2;

    public static WindowBounds For(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        var monitor = handle == IntPtr.Zero ? IntPtr.Zero : MonitorFromWindow(handle, MonitorDefaultToNearest);
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (monitor == IntPtr.Zero || !GetMonitorInfo(monitor, ref info))
        {
            return Primary();
        }

        // Win32 reports device pixels; WPF lays out in units scaled by that monitor's DPI.
        var fromDevice = PresentationSource.FromVisual(window)?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
        var topLeft = fromDevice.Transform(new Point(info.Work.Left, info.Work.Top));
        var bottomRight = fromDevice.Transform(new Point(info.Work.Right, info.Work.Bottom));
        return new WindowBounds(topLeft.X, topLeft.Y, bottomRight.X - topLeft.X, bottomRight.Y - topLeft.Y);
    }

    /// <summary>For windows with no handle yet, such as a dialog shown before the shell exists.</summary>
    public static WindowBounds Primary()
    {
        var area = SystemParameters.WorkArea;
        return new WindowBounds(area.Left, area.Top, area.Width, area.Height);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public NativeRect Monitor;
        public NativeRect Work;
        public uint Flags;
    }

    [DllImport("user32", EntryPoint = "MonitorFromWindow")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

    [DllImport("user32", EntryPoint = "GetMonitorInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
}
