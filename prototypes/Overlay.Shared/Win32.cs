using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Pigeon.Prototypes.Overlay;

/// <summary>Win32 для обоих прототипов: мониторы, стили окна, проверка попадания.</summary>
[SupportedOSPlatform("windows")]
public static class Win32
{
    public const int GwlExStyle = -20;
    public const long WsExTopmost = 0x8;
    public const long WsExTransparent = 0x20;
    public const long WsExToolWindow = 0x80;
    public const long WsExLayered = 0x80000;
    public const long WsExNoActivate = 0x8000000;
    private const uint GaRoot = 2;

    public static readonly IntPtr PerMonitorAwareV2 = -4;

    [StructLayout(LayoutKind.Sequential)]
    public struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct Point
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public Rect Monitor;
        public Rect Work;
        public uint Flags;
    }

    private delegate bool MonitorEnumProc(IntPtr monitor, IntPtr hdc, IntPtr rect, IntPtr data);

    /// <summary>Мониторы: основной первым, рабочая область и масштаб.</summary>
    public static List<ScreenInfo> Screens()
    {
        var list = new List<(ScreenInfo Info, bool Primary)>();
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (monitor, _, _, _) =>
        {
            var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
            if (GetMonitorInfoW(monitor, ref info))
            {
                var scale = GetDpiForMonitor(monitor, 0, out var dpi, out _) == 0 ? dpi / 96.0 : 1.0;
                var w = info.Work;
                list.Add((new ScreenInfo(new ScreenRect(w.Left, w.Top, w.Right - w.Left, w.Bottom - w.Top), scale), (info.Flags & 1) != 0));
            }

            return true;
        }, IntPtr.Zero);
        return [.. list.OrderByDescending(s => s.Primary).Select(s => s.Info)];
    }

    public static long ExStyle(IntPtr window) => GetWindowLongPtrW(window, GwlExStyle).ToInt64();

    public static void SetExStyle(IntPtr window, long style) => SetWindowLongPtrW(window, GwlExStyle, (IntPtr)style);

    /// <summary>Чьё окно верхнего уровня под точкой экрана.</summary>
    public static IntPtr RootWindowAt(int x, int y)
    {
        var window = WindowFromPoint(new Point { X = x, Y = y });
        return window == IntPtr.Zero ? IntPtr.Zero : GetAncestor(window, GaRoot);
    }

    public static (int X, int Y) Cursor() => GetCursorPos(out var p) ? (p.X, p.Y) : (int.MinValue, int.MinValue);

    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    public static extern IntPtr SetProcessDpiAwarenessContext(IntPtr context);

    [DllImport("user32.dll")]
    public static extern uint GetDpiForWindow(IntPtr window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetWindowRect(IntPtr window, out Rect rect);

    [DllImport("dwmapi.dll")]
    public static extern int DwmFlush();

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindowLongPtrW(IntPtr window, int index);

    [DllImport("user32.dll")]
    private static extern IntPtr SetWindowLongPtrW(IntPtr window, int index, IntPtr value);

    [DllImport("user32.dll")]
    private static extern IntPtr WindowFromPoint(Point point);

    [DllImport("user32.dll")]
    private static extern IntPtr GetAncestor(IntPtr window, uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out Point point);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnumProc callback, IntPtr data);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfoW(IntPtr monitor, ref MonitorInfo info);

    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(IntPtr monitor, int type, out uint dpiX, out uint dpiY);
}
