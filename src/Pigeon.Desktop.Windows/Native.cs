using System.Runtime.InteropServices;

namespace Pigeon.Desktop.Windows;

/// <summary>Win32 API для пробы возможностей (docs/platform/capability-matrix.md, Windows).</summary>
internal static class Native
{
    public const int GwlExStyle = -20;
    public const uint WsPopup = 0x80000000;
    public const uint WsExTopmost = 0x00000008;
    public const uint WsExTransparent = 0x00000020;
    public const uint WsExToolWindow = 0x00000080;
    public const uint WsExLayered = 0x00080000;
    public const uint WsExNoActivate = 0x08000000;
    public const uint WdaExcludeFromCapture = 0x00000011;
    public const uint MonitorInfoFPrimary = 0x1;
    public const int MdtEffectiveDpi = 0;
    public const int DwmwaExtendedFrameBounds = 9;
    public const int DwmwaCloaked = 14;
    public const uint EventSystemForeground = 0x0003;
    public const uint EventObjectLocationChange = 0x800B;
    public const uint WinEventOutOfContext = 0x0000;
    public const uint AbmGetTaskbarPos = 0x00000005;
    public const uint AbmGetState = 0x00000004;
    public const uint AbsAutoHide = 0x0000001;
    public const uint EddGetDeviceInterfaceName = 0x00000001;
    public const uint ProcessQueryLimitedInformation = 0x1000;
    public const int ErrorNotFound = 1168;
    public const uint CredTypeGeneric = 1;
    public const uint NotifyForThisSession = 0;
    public static readonly IntPtr HkeyCurrentUser = unchecked((IntPtr)(int)0x80000001);
    public const int KeyRead = 0x20019;

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
    public struct MonitorInfo
    {
        public int Size;
        public Rect Monitor;
        public Rect Work;
        public uint Flags;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct MonitorInfoEx
    {
        public int Size;
        public Rect Monitor;
        public Rect Work;
        public uint Flags;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string Device;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct DisplayDevice
    {
        public int Size;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string DeviceName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceString;

        public uint StateFlags;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceId;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceKey;
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

    [StructLayout(LayoutKind.Sequential)]
    public struct Message
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
    public struct Size
    {
        public int Width;
        public int Height;
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

    public delegate IntPtr WindowProc(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern ushort RegisterClassExW(ref WindowClass windowClass);

    [DllImport("user32.dll")]
    public static extern IntPtr DefWindowProcW(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool ShowWindow(IntPtr window, int command);

    [DllImport("user32.dll")]
    public static extern void PostQuitMessage(int exitCode);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool PeekMessageW(out Message message, IntPtr window, uint min, uint max, uint remove);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool TranslateMessage(ref Message message);

    [DllImport("user32.dll")]
    public static extern IntPtr DispatchMessageW(ref Message message);

    [DllImport("user32.dll")]
    public static extern uint MsgWaitForMultipleObjectsEx(uint count, IntPtr handles, uint milliseconds, uint wakeMask, uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool UpdateLayeredWindow(IntPtr window, IntPtr screenDc, ref Point destination, ref Size size,
        IntPtr sourceDc, ref Point source, uint colorKey, ref BlendFunction blend, uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetWindowPos(IntPtr window, IntPtr insertAfter, int x, int y, int width, int height, uint flags);

    [DllImport("user32.dll")]
    public static extern IntPtr GetAncestor(IntPtr window, uint flags);

    [DllImport("user32.dll")]
    public static extern IntPtr SetProcessDpiAwarenessContext(IntPtr context);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr GetModuleHandleW(string? name);

    [DllImport("user32.dll")]
    public static extern IntPtr LoadCursorW(IntPtr instance, IntPtr name);

    [DllImport("gdi32.dll")]
    public static extern IntPtr CreateCompatibleDC(IntPtr dc);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool DeleteDC(IntPtr dc);

    [DllImport("gdi32.dll")]
    public static extern IntPtr CreateDIBSection(IntPtr dc, ref BitmapInfoHeader header, uint usage, out IntPtr bits, IntPtr section, uint offset);

    [DllImport("gdi32.dll")]
    public static extern IntPtr SelectObject(IntPtr dc, IntPtr gdiObject);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool DeleteObject(IntPtr gdiObject);

    [StructLayout(LayoutKind.Sequential)]
    public struct LastInputInfo
    {
        public uint Size;
        public uint Time;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct AppBarData
    {
        public uint Size;
        public IntPtr Window;
        public uint CallbackMessage;
        public uint Edge;
        public Rect Rect;
        public IntPtr Param;
    }

    public delegate bool MonitorEnumProc(IntPtr monitor, IntPtr hdc, IntPtr rect, IntPtr data);

    public delegate bool WindowEnumProc(IntPtr window, IntPtr data);

    public delegate void WinEventProc(IntPtr hook, uint eventType, IntPtr window, int objectId, int childId, uint thread, uint time);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern IntPtr CreateWindowExW(uint exStyle, string className, string? windowName, uint style,
        int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool DestroyWindow(IntPtr window);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr GetWindowLongPtrW(IntPtr window, int index);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetWindowDisplayAffinity(IntPtr window, uint affinity);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnumProc callback, IntPtr data);

    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetMonitorInfoEx(IntPtr monitor, ref MonitorInfoEx info);

    [DllImport("user32.dll", EntryPoint = "EnumDisplayDevicesW", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool EnumDisplayDevices(string? device, uint index, ref DisplayDevice displayDevice, uint flags);

    [DllImport("user32.dll", EntryPoint = "GetClassNameW", CharSet = CharSet.Unicode)]
    public static extern int GetClassName(IntPtr window, char[] className, int maxCount);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetWindowRect(IntPtr window, out Rect rect);

    [DllImport("shcore.dll")]
    public static extern int GetDpiForMonitor(IntPtr monitor, int type, out uint dpiX, out uint dpiY);

    [DllImport("user32.dll")]
    public static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetCursorPos(out Point point);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool EnumWindows(WindowEnumProc callback, IntPtr data);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsWindowVisible(IntPtr window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsIconic(IntPtr window);

    [DllImport("dwmapi.dll")]
    public static extern int DwmGetWindowAttribute(IntPtr window, int attribute, out Rect value, int size);

    [DllImport("dwmapi.dll", EntryPoint = "DwmGetWindowAttribute")]
    public static extern int DwmGetWindowAttributeInt(IntPtr window, int attribute, out int value, int size);

    [DllImport("user32.dll")]
    public static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr module, WinEventProc callback,
        uint process, uint thread, uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool UnhookWinEvent(IntPtr hook);

    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern IntPtr OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint processId);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "QueryFullProcessImageNameW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool QueryFullProcessImageName(IntPtr process, uint flags, char[] buffer, ref uint size);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool CloseHandle(IntPtr handle);

    [DllImport("user32.dll", EntryPoint = "GetWindowTextLengthW", SetLastError = true)]
    public static extern int GetWindowTextLength(IntPtr window);

    [DllImport("shell32.dll")]
    public static extern UIntPtr SHAppBarMessage(uint message, ref AppBarData data);

    [DllImport("shell32.dll")]
    public static extern int SHQueryUserNotificationState(out int state);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetLastInputInfo(ref LastInputInfo info);

    [DllImport("kernel32.dll")]
    public static extern uint GetTickCount();

    [DllImport("wtsapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool WTSRegisterSessionNotification(IntPtr window, uint flags);

    [DllImport("wtsapi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool WTSUnRegisterSessionNotification(IntPtr window);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "CredReadW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool CredRead(string target, uint type, uint flags, out IntPtr credential);

    [DllImport("advapi32.dll")]
    public static extern void CredFree(IntPtr buffer);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, EntryPoint = "RegOpenKeyExW")]
    public static extern int RegOpenKeyEx(IntPtr key, string subKey, int options, int desired, out IntPtr result);

    [DllImport("advapi32.dll")]
    public static extern int RegCloseKey(IntPtr key);

    [DllImport("user32.dll")]
    public static extern IntPtr SetCapture(IntPtr window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    public static extern IntPtr GetCapture();

    [DllImport("user32.dll")]
    public static extern IntPtr SetCursor(IntPtr cursor);

    [DllImport("user32.dll")]
    public static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool AppendMenuW(IntPtr menu, uint flags, UIntPtr id, string? text);

    [DllImport("user32.dll")]
    public static extern int TrackPopupMenuEx(IntPtr menu, uint flags, int x, int y, IntPtr window, IntPtr parameters);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool DestroyMenu(IntPtr menu);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetForegroundWindow(IntPtr window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool PostMessageW(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
}
