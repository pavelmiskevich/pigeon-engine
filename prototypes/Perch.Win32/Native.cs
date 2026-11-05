using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Pigeon.Prototypes.Perch;

[SupportedOSPlatform("windows")]
internal static class Native
{
    public const uint WsPopup = 0x80000000;
    public const uint WsOverlappedWindow = 0x00CF0000;
    public const uint WsVisible = 0x10000000;
    public const uint WmDestroy = 0x0002;
    public const uint WmClose = 0x0010;
    public const uint WmQuit = 0x0012;
    public const uint WmMouseActivate = 0x0021;
    public const uint WmRButtonUp = 0x0205;
    public const int MaNoActivate = 3;
    public const int SwShowNoActivate = 4;
    public const int SwMinimize = 6;
    public const int SwRestore = 9;
    public const int SwHide = 0;
    public const uint PmRemove = 1;
    public const uint UlwAlpha = 2;
    public const uint SwpNoSize = 0x1;
    public const uint SwpNoZOrder = 0x4;
    public const uint SwpNoActivate = 0x10;
    public const uint SwpShowWindow = 0x40;
    public const uint QsAllInput = 0x04FF;
    public const uint MwmoInputAvailable = 0x4;
    public const uint EventSystemForeground = 0x0003;
    public const uint EventSystemMinimizeStart = 0x0016;
    public const uint EventObjectDestroy = 0x8001;
    public const uint EventObjectLocationChange = 0x800B;
    public const uint WinEventOutOfContext = 0;
    public const int ObjIdWindow = 0;
    public const int DwmwaExtendedFrameBounds = 9;
    public const int DwmwaCloaked = 14;
    public const int GwlExStyle = -20;
    public const long WsExToolWindow = 0x80;
    public const long WsExLayered = 0x80000;
    public const long WsExTopmost = 0x8;
    public const long WsExNoActivate = 0x8000000;
    public static readonly IntPtr HwndTop = IntPtr.Zero;
    public static readonly IntPtr HwndTopmost = -1;

    public delegate IntPtr WndProc(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

    public delegate bool EnumProc(IntPtr window, IntPtr data);

    public delegate void WinEventProc(IntPtr hook, uint eventType, IntPtr window, int objectId, int childId, uint thread, uint time);

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
    public struct Size
    {
        public int Width;
        public int Height;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct Msg
    {
        public IntPtr Window;
        public uint Id;
        public IntPtr WParam;
        public IntPtr LParam;
        public uint Time;
        public Point Point;
        public uint Private;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct BlendFunction
    {
        public byte BlendOp;
        public byte BlendFlags;
        public byte SourceConstantAlpha;
        public byte AlphaFormat;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct BitmapInfoHeader
    {
        public uint Size;
        public int Width;
        public int Height;
        public ushort Planes;
        public ushort BitCount;
        public uint Compression;
        public uint SizeImage;
        public int XPelsPerMeter;
        public int YPelsPerMeter;
        public uint ClrUsed;
        public uint ClrImportant;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct WindowClass
    {
        public uint Size;
        public uint Style;
        public IntPtr WndProc;
        public int ClassExtra;
        public int WindowExtra;
        public IntPtr Instance;
        public IntPtr Icon;
        public IntPtr Cursor;
        public IntPtr Background;
        public string? MenuName;
        public string ClassName;
        public IntPtr SmallIcon;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern ushort RegisterClassExW(ref WindowClass windowClass);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr CreateWindowExW(uint exStyle, string className, string windowName, uint style,
        int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);

    [DllImport("user32.dll")]
    public static extern IntPtr DefWindowProcW(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool ShowWindow(IntPtr window, int command);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool DestroyWindow(IntPtr window);

    [DllImport("user32.dll")]
    public static extern void PostQuitMessage(int exitCode);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool PostMessageW(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool PeekMessageW(out Msg message, IntPtr window, uint min, uint max, uint remove);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool TranslateMessage(ref Msg message);

    [DllImport("user32.dll")]
    public static extern IntPtr DispatchMessageW(ref Msg message);

    [DllImport("user32.dll")]
    public static extern uint MsgWaitForMultipleObjectsEx(uint count, IntPtr handles, uint milliseconds, uint wakeMask, uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool UpdateLayeredWindow(IntPtr window, IntPtr screenDc, ref Point destination, ref Size size,
        IntPtr sourceDc, ref Point source, uint colorKey, ref BlendFunction blend, uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetWindowRect(IntPtr window, out Rect rect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsWindow(IntPtr window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsWindowVisible(IntPtr window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsIconic(IntPtr window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool EnumWindows(EnumProc callback, IntPtr data);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetClassNameW(IntPtr window, char[] name, int max);

    [DllImport("user32.dll")]
    public static extern IntPtr GetWindowLongPtrW(IntPtr window, int index);

    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    [DllImport("user32.dll")]
    public static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr module, WinEventProc callback,
        uint process, uint thread, uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool UnhookWinEvent(IntPtr hook);

    [DllImport("dwmapi.dll")]
    public static extern int DwmGetWindowAttribute(IntPtr window, int attribute, out Rect value, int size);

    [DllImport("dwmapi.dll", EntryPoint = "DwmGetWindowAttribute")]
    public static extern int DwmGetWindowAttributeInt(IntPtr window, int attribute, out int value, int size);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr GetModuleHandleW(string? name);

    [DllImport("gdi32.dll")]
    public static extern IntPtr CreateCompatibleDC(IntPtr dc);

    [DllImport("gdi32.dll")]
    public static extern IntPtr CreateDIBSection(IntPtr dc, ref BitmapInfoHeader header, uint usage, out IntPtr bits, IntPtr section, uint offset);

    [DllImport("gdi32.dll")]
    public static extern IntPtr SelectObject(IntPtr dc, IntPtr gdiObject);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool DeleteObject(IntPtr gdiObject);
}
