using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using Pigeon.World.Desktop.Capabilities;

namespace Pigeon.Desktop.Linux;

/// <summary>
/// Проба Linux X11 через Xlib, XRandR, XShape и XScreenSaver (docs/platform/capability-matrix.md,
/// механизмы X11). Поддержку оконного менеджера читает из <c>_NET_SUPPORTED</c>.
/// </summary>
[SupportedOSPlatform("linux")]
internal static class X11Probe
{
    private const string Xlib = "libX11.so.6";
    private const string Xrandr = "libXrandr.so.2";
    private const string Xext = "libXext.so.6";
    private const string Xss = "libXss.so.1";
    private const nuint AnyPropertyType = 0;

    public static CapabilityProbeResult? Probe()
    {
        IntPtr display;
        try
        {
            display = XOpenDisplay(IntPtr.Zero);
        }
        catch (DllNotFoundException)
        {
            return null;
        }

        if (display == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            return ProbeDisplay(display);
        }
        finally
        {
            XCloseDisplay(display);
        }
    }

    private static CapabilityProbeResult ProbeDisplay(IntPtr display)
    {
        var screen = XDefaultScreen(display);
        var root = XRootWindow(display, screen);
        var supported = AtomNames(display, GetAtoms(display, root, "_NET_SUPPORTED"));
        bool Supports(string atom) => supported.Contains(atom);

        var wmName = WindowManagerName(display, root);
        var compositing = XGetSelectionOwner(display, XInternAtom(display, $"_NET_WM_CM_S{screen}", false)) != 0;
        var scale = XftScale(display);
        var monitors = Monitors(display, root, scale, WorkArea(display, root));
        var windows = GetAtoms(display, root, "_NET_CLIENT_LIST_STACKING");
        var hasShape = Try(() => XShapeQueryExtension(display, out _, out _));
        var hasRandr = Try(() => XRRQueryExtension(display, out _, out _));
        var hasIdle = Try(() => XScreenSaverQueryExtension(display, out _, out _));

        var checks = new List<CapabilityCheck>
        {
            compositing
                ? new(Capability.TransparentOverlay, CapabilityStatus.Available, "ARGB visual + композитор (_NET_WM_CM_S0)")
                : new(Capability.TransparentOverlay, CapabilityStatus.Unavailable, "_NET_WM_CM_S0", "композитор не запущен: без него прозрачности нет, уровень L1"),
            hasShape
                ? new(Capability.ClickThrough, CapabilityStatus.Available, "XShape input region")
                : new(Capability.ClickThrough, CapabilityStatus.Unavailable, "XShape", "расширение SHAPE недоступно"),
            Supports("_NET_WM_STATE_ABOVE")
                ? new(Capability.AlwaysOnTop, CapabilityStatus.Limited, "_NET_WM_STATE_ABOVE", "заявлено оконным менеджером; выполнение зависит от него")
                : new(Capability.AlwaysOnTop, CapabilityStatus.Unavailable, "_NET_WM_STATE_ABOVE", "не поддерживается оконным менеджером"),
            Supports("_NET_WM_STATE_SKIP_TASKBAR") && Supports("_NET_WM_WINDOW_TYPE_UTILITY")
                ? new(Capability.NoFocusNoTaskbar, CapabilityStatus.Available, "_NET_WM_WINDOW_TYPE_UTILITY + _NET_WM_STATE_SKIP_TASKBAR, input hint = false")
                : new(Capability.NoFocusNoTaskbar, CapabilityStatus.Limited, "EWMH", "оконный менеджер не заявляет SKIP_TASKBAR или UTILITY"),
            monitors.Count > 0
                ? new(Capability.MonitorEnumeration, CapabilityStatus.Limited, "XRandR (XRRGetMonitors)", "масштаб в X11 глобальный (Xft.dpi), не для каждого монитора")
                : new(Capability.MonitorEnumeration, CapabilityStatus.Unavailable, "XRandR", "мониторы не найдены"),
            new(Capability.MonitorEvents, hasRandr ? CapabilityStatus.Available : CapabilityStatus.Unavailable, "RRScreenChangeNotify",
                Verified: false),
            Try(() => XQueryPointer(display, root, out _, out _, out _, out _, out _, out _, out _))
                ? new(Capability.GlobalCursor, CapabilityStatus.Available, "XQueryPointer")
                : new(Capability.GlobalCursor, CapabilityStatus.Unavailable, "XQueryPointer"),
            Supports("_NET_CLIENT_LIST_STACKING")
                ? new(Capability.WindowGeometry, CapabilityStatus.Available, "_NET_CLIENT_LIST_STACKING + XGetGeometry + _NET_FRAME_EXTENTS")
                : new(Capability.WindowGeometry, CapabilityStatus.Unavailable, "_NET_CLIENT_LIST_STACKING", "не поддерживается оконным менеджером"),
            new(Capability.WindowZOrder, Supports("_NET_CLIENT_LIST_STACKING") ? CapabilityStatus.Available : CapabilityStatus.Unavailable,
                "порядок _NET_CLIENT_LIST_STACKING"),
            new(Capability.WindowEvents, CapabilityStatus.Available, "SubstructureNotify и PropertyNotify на корневом окне", Verified: false),
            Supports("_NET_ACTIVE_WINDOW") && Supports("_NET_WM_PID")
                ? new(Capability.ActiveApplication, CapabilityStatus.Available, "_NET_ACTIVE_WINDOW → _NET_WM_PID / WM_CLASS")
                : new(Capability.ActiveApplication, CapabilityStatus.Limited, "WM_CLASS", "оконный менеджер не заявляет _NET_ACTIVE_WINDOW или _NET_WM_PID"),
            new(Capability.WindowTitles, Supports("_NET_WM_NAME") ? CapabilityStatus.Available : CapabilityStatus.Limited,
                "_NET_WM_NAME (не читается пробой)", "читается только при capability Window, ТЗ §14"),
            Supports("_NET_WORKAREA") || Supports("_NET_WM_STRUT_PARTIAL")
                ? new(Capability.TaskbarGeometry, CapabilityStatus.Available, "_NET_WORKAREA и _NET_WM_STRUT_PARTIAL панелей")
                : new(Capability.TaskbarGeometry, CapabilityStatus.Unavailable, "_NET_WORKAREA"),
            new(Capability.FullscreenDetection, Supports("_NET_WM_STATE_FULLSCREEN") ? CapabilityStatus.Available : CapabilityStatus.Limited,
                "_NET_WM_STATE_FULLSCREEN активного окна"),
            new(Capability.CaptureExclusion, CapabilityStatus.Unavailable, "—", "в X11 механизма нет", Verified: false),
            hasIdle
                ? new(Capability.IdleTime, CapabilityStatus.Available, "XScreenSaverQueryInfo")
                : new(Capability.IdleTime, CapabilityStatus.Unknown, "XScreenSaver", "расширение или libXss недоступны"),
            LinuxCommon.SessionEvents(),
            LinuxCommon.SecretStore(),
            LinuxCommon.Autostart(),
            Supports("_NET_WM_DESKTOP")
                ? new(Capability.AllVirtualDesktops, CapabilityStatus.Available, "_NET_WM_DESKTOP = 0xFFFFFFFF")
                : new(Capability.AllVirtualDesktops, CapabilityStatus.Unavailable, "_NET_WM_DESKTOP"),
        };

        var environment = new ProbeEnvironment("Linux", LinuxCommon.OsVersion(), RuntimeInformation.OSArchitecture.ToString(),
            Environment.GetEnvironmentVariable("WAYLAND_DISPLAY") is null ? "x11" : "xwayland",
            wmName ?? Environment.GetEnvironmentVariable("XDG_CURRENT_DESKTOP"));
        return new CapabilityProbeResult(environment, checks, monitors, windows.Length);
    }

    private static bool Try(Func<bool> call)
    {
        try
        {
            return call();
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return false;
        }
    }

    private static nuint[] GetAtoms(IntPtr display, nuint window, string property) =>
        GetProperty(display, window, property, out var format, out var data) && format == 32 ? data : [];

    private static bool GetProperty(IntPtr display, nuint window, string property, out int format, out nuint[] data)
    {
        data = [];
        format = 0;
        var atom = XInternAtom(display, property, true);
        if (atom == 0
            || XGetWindowProperty(display, window, atom, 0, 1 << 16, false, AnyPropertyType,
                out _, out format, out var count, out _, out var pointer) != 0
            || pointer == IntPtr.Zero)
        {
            return false;
        }

        try
        {
            if (format == 32)
            {
                // Формат 32 в Xlib — массив long, то есть 8 байт на 64-битной системе.
                data = new nuint[(int)count];
                for (var i = 0; i < (int)count; i++)
                {
                    data[i] = (nuint)Marshal.ReadIntPtr(pointer, i * IntPtr.Size);
                }
            }
            else if (format == 8)
            {
                var bytes = new byte[(int)count];
                Marshal.Copy(pointer, bytes, 0, bytes.Length);
                data = [.. bytes.Select(b => (nuint)b)];
            }

            return true;
        }
        finally
        {
            XFree(pointer);
        }
    }

    private static HashSet<string> AtomNames(IntPtr display, nuint[] atoms)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var atom in atoms)
        {
            var name = XGetAtomName(display, atom);
            if (name != IntPtr.Zero)
            {
                names.Add(Marshal.PtrToStringUTF8(name) ?? string.Empty);
                XFree(name);
            }
        }

        return names;
    }

    /// <summary>Имя оконного менеджера через _NET_SUPPORTING_WM_CHECK — это не данные пользователя.</summary>
    private static string? WindowManagerName(IntPtr display, nuint root)
    {
        var check = GetAtoms(display, root, "_NET_SUPPORTING_WM_CHECK");
        if (check.Length == 0 || !GetProperty(display, check[0], "_NET_WM_NAME", out var format, out var data) || format != 8)
        {
            return null;
        }

        return Encoding.UTF8.GetString([.. data.Select(b => (byte)b)]);
    }

    private static PixelRect? WorkArea(IntPtr display, nuint root)
    {
        var area = GetAtoms(display, root, "_NET_WORKAREA");
        return area.Length >= 4 ? new PixelRect((int)area[0], (int)area[1], (int)area[2], (int)area[3]) : null;
    }

    private static double XftScale(IntPtr display)
    {
        var resources = Marshal.PtrToStringUTF8(XResourceManagerString(display)) ?? string.Empty;
        foreach (var line in resources.Split('\n'))
        {
            if (line.StartsWith("Xft.dpi:", StringComparison.Ordinal)
                && double.TryParse(line["Xft.dpi:".Length..].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var dpi))
            {
                return dpi / 96.0;
            }
        }

        return 1.0;
    }

    private static List<MonitorInfo> Monitors(IntPtr display, nuint root, double scale, PixelRect? workArea)
    {
        var monitors = new List<MonitorInfo>();
        IntPtr list;
        int count;
        try
        {
            list = XRRGetMonitors(display, root, true, out count);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return monitors;
        }

        if (list == IntPtr.Zero)
        {
            return monitors;
        }

        try
        {
            // XRRMonitorInfo: Atom name; Bool primary, automatic; int noutput, x, y, width, height,
            // mwidth, mheight; RROutput* outputs — 56 байт на 64-битной системе.
            const int size = 56;
            for (var i = 0; i < count; i++)
            {
                var item = list + (i * size);
                var primary = Marshal.ReadInt32(item, 8) != 0;
                var bounds = new PixelRect(Marshal.ReadInt32(item, 20), Marshal.ReadInt32(item, 24), Marshal.ReadInt32(item, 28), Marshal.ReadInt32(item, 32));
                monitors.Add(new MonitorInfo(bounds, primary && workArea is { } area ? area : bounds, scale, primary));
            }
        }
        finally
        {
            XRRFreeMonitors(list);
        }

        return monitors;
    }

    [DllImport(Xlib)]
    private static extern IntPtr XOpenDisplay(IntPtr name);

    [DllImport(Xlib)]
    private static extern int XCloseDisplay(IntPtr display);

    [DllImport(Xlib)]
    private static extern int XDefaultScreen(IntPtr display);

    [DllImport(Xlib)]
    private static extern nuint XRootWindow(IntPtr display, int screen);

    [DllImport(Xlib)]
    private static extern nuint XInternAtom(IntPtr display, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, [MarshalAs(UnmanagedType.Bool)] bool onlyIfExists);

    [DllImport(Xlib)]
    private static extern IntPtr XGetAtomName(IntPtr display, nuint atom);

    [DllImport(Xlib)]
    private static extern int XGetWindowProperty(IntPtr display, nuint window, nuint property, nint offset, nint length,
        [MarshalAs(UnmanagedType.Bool)] bool delete, nuint requestedType, out nuint actualType, out int actualFormat,
        out nuint itemCount, out nuint bytesAfter, out IntPtr data);

    [DllImport(Xlib)]
    private static extern int XFree(IntPtr data);

    [DllImport(Xlib)]
    private static extern nuint XGetSelectionOwner(IntPtr display, nuint selection);

    [DllImport(Xlib)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool XQueryPointer(IntPtr display, nuint window, out nuint root, out nuint child,
        out int rootX, out int rootY, out int windowX, out int windowY, out uint mask);

    [DllImport(Xlib)]
    private static extern IntPtr XResourceManagerString(IntPtr display);

    [DllImport(Xrandr)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool XRRQueryExtension(IntPtr display, out int eventBase, out int errorBase);

    [DllImport(Xrandr)]
    private static extern IntPtr XRRGetMonitors(IntPtr display, nuint window, [MarshalAs(UnmanagedType.Bool)] bool active, out int count);

    [DllImport(Xrandr)]
    private static extern void XRRFreeMonitors(IntPtr monitors);

    [DllImport(Xext)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool XShapeQueryExtension(IntPtr display, out int eventBase, out int errorBase);

    [DllImport(Xss)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool XScreenSaverQueryExtension(IntPtr display, out int eventBase, out int errorBase);
}
