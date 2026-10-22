using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using WinRT.Interop;

namespace SysMonitor.App.Helpers;

/// <summary>
/// The smallest a window may be dragged to, in view pixels, scaled to the monitor it is on. Windows App SDK 1.5
/// has no setting for it - OverlappedPresenter's minimum size arrives in 1.7 - so the window answers Windows'
/// own WM_GETMINMAXINFO, which is what every resizable Win32 window does.
/// <para>
/// Without it the window shrank until the pages' fixed columns clipped: the Dashboard's four resource tiles need
/// about 860 view pixels beside the rail before they overlap.
/// </para>
/// </summary>
internal sealed class WindowMinimumSize
{
    private const uint WmGetMinMaxInfo = 0x0024;

    private readonly int _width;
    private readonly int _height;

    // Kept here so the delegate Windows calls back into is not collected while the window lives.
    private readonly SubclassProc _proc;

    private WindowMinimumSize(int width, int height)
    {
        _width = width;
        _height = height;
        _proc = WindowProc;
    }

    /// <summary>Holds <paramref name="window"/> to at least <paramref name="width"/> by <paramref name="height"/>.</summary>
    public static WindowMinimumSize Apply(Window window, int width, int height)
    {
        var limit = new WindowMinimumSize(width, height);
        var handle = WindowNative.GetWindowHandle(window);
        SetWindowSubclass(handle, limit._proc, 1, 0);
        return limit;
    }

    private IntPtr WindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam, nuint id, nuint data)
    {
        if (msg == WmGetMinMaxInfo && lParam != IntPtr.Zero)
        {
            var scale = GetDpiForWindow(hWnd) / 96.0;
            var info = Marshal.PtrToStructure<MinMaxInfo>(lParam);
            info.MinTrackSize.X = (int)Math.Ceiling(_width * scale);
            info.MinTrackSize.Y = (int)Math.Ceiling(_height * scale);
            Marshal.StructureToPtr(info, lParam, false);
        }

        return DefSubclassProc(hWnd, msg, wParam, lParam);
    }

    private delegate IntPtr SubclassProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam, nuint id, nuint data);

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MinMaxInfo
    {
        public Point Reserved;
        public Point MaxSize;
        public Point MaxPosition;
        public Point MinTrackSize;
        public Point MaxTrackSize;
    }

    [DllImport("comctl32.dll", SetLastError = true)]
    private static extern bool SetWindowSubclass(IntPtr hWnd, SubclassProc proc, nuint id, nuint data);

    [DllImport("comctl32.dll")]
    private static extern IntPtr DefSubclassProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hWnd);
}
