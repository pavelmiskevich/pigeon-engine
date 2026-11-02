using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Pigeon.World.Desktop.Capabilities;

namespace Pigeon.Desktop.MacOS;

/// <summary>
/// Проба macOS через C-API CoreGraphics, HIServices и Security (docs/platform/capability-matrix.md,
/// механизмы macOS). Окон не создаёт и разрешений не запрашивает: если разрешения нет, это и
/// есть результат. Возможности окна-оверлея (NSPanel) проверяет прототип S0.2, здесь они выводятся
/// из версии системы.
/// </summary>
[SupportedOSPlatform("macos")]
public sealed class MacCapabilityProbe : ICapabilityProbe
{
    private const string CoreGraphics = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";
    private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
    private const string ApplicationServices = "/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices";
    private const string Security = "/System/Library/Frameworks/Security.framework/Security";

    private const uint OnScreenOnly = 1;
    private const uint ExcludeDesktopElements = 16;
    private const int CFNumberIntType = 9;

    public CapabilityProbeResult Probe()
    {
        var monitors = Safe(Monitors, []);
        var (windowCount, frontPid) = Safe(Windows, (0, 0));

        var checks = new List<CapabilityCheck>
        {
            new(Capability.TransparentOverlay, CapabilityStatus.Available, "NSPanel без рамки, isOpaque = false", Verified: false),
            new(Capability.ClickThrough, CapabilityStatus.Limited, "ignoresMouseEvents, переключаемый по попаданию курсора в голубя",
                "флаг действует на всё окно", Verified: false),
            new(Capability.AlwaysOnTop, CapabilityStatus.Available, "NSWindow.level = .floating / .statusBar", Verified: false),
            new(Capability.NoFocusNoTaskbar, CapabilityStatus.Available, "non-activating NSPanel + LSUIElement", Verified: false),
            monitors.Count > 0
                ? new(Capability.MonitorEnumeration, CapabilityStatus.Available, "CGGetActiveDisplayList + CGDisplayCopyDisplayMode",
                    "рабочая область (без меню-бара и Dock) — через NSScreen.visibleFrame, проба её не читает")
                : new(Capability.MonitorEnumeration, CapabilityStatus.Unavailable, "CGGetActiveDisplayList", "мониторы не найдены"),
            new(Capability.MonitorEvents, CapabilityStatus.Available, "CGDisplayRegisterReconfigurationCallback", Verified: false),
            Safe(() => Cursor(), false)
                ? new(Capability.GlobalCursor, CapabilityStatus.Available, "CGEventGetLocation (без разрешений)")
                : new(Capability.GlobalCursor, CapabilityStatus.Unavailable, "CGEventGetLocation"),
            windowCount > 0
                ? new(Capability.WindowGeometry, CapabilityStatus.Available, "CGWindowListCopyWindowInfo: kCGWindowBounds (без разрешений)")
                : new(Capability.WindowGeometry, CapabilityStatus.Unavailable, "CGWindowListCopyWindowInfo", "окна не найдены"),
            new(Capability.WindowZOrder, windowCount > 0 ? CapabilityStatus.Available : CapabilityStatus.Unavailable,
                "порядок CGWindowList (front-to-back) и kCGWindowLayer"),
            Safe(AXIsProcessTrusted, false)
                ? new(Capability.WindowEvents, CapabilityStatus.Available, "AXObserver (разрешение Accessibility есть)")
                : new(Capability.WindowEvents, CapabilityStatus.Limited, "опрос CGWindowList",
                    "push-события — только через Accessibility; без него следование за окном опросом"),
            frontPid > 0
                ? new(Capability.ActiveApplication, CapabilityStatus.Available, "kCGWindowOwnerPID переднего окна (имя приложения не читается)")
                : new(Capability.ActiveApplication, CapabilityStatus.Unknown, "kCGWindowOwnerPID", "нет переднего окна во время пробы"),
            Safe(CGPreflightScreenCaptureAccess, false)
                ? new(Capability.WindowTitles, CapabilityStatus.Available, "kCGWindowName (разрешение Screen Recording есть)",
                    "читается только при capability Window, ТЗ §14")
                : new(Capability.WindowTitles, CapabilityStatus.Limited, "kCGWindowName",
                    "нужно разрешение Screen Recording; Pigeon его не запрашивает"),
            new(Capability.TaskbarGeometry, CapabilityStatus.Available, "NSScreen.visibleFrame (меню-бар и Dock)",
                "Dock с автоскрытием — ⚠️", Verified: false),
            new(Capability.FullscreenDetection, CapabilityStatus.Limited, "отдельный Space + геометрия переднего окна", Verified: false),
            new(Capability.CaptureExclusion, CapabilityStatus.Limited, "NSWindow.sharingType = .none",
                "ScreenCaptureKit в новых версиях может это игнорировать", Verified: false),
            Safe(() => IdleSeconds() >= 0, false)
                ? new(Capability.IdleTime, CapabilityStatus.Available, "CGEventSourceSecondsSinceLastEventType")
                : new(Capability.IdleTime, CapabilityStatus.Unavailable, "CGEventSourceSecondsSinceLastEventType"),
            new(Capability.SessionEvents, CapabilityStatus.Available, "NSWorkspace: willSleep / didWake, screensDidSleep", Verified: false),
            Safe(() => SecKeychainCopyDefault(out var keychain) == 0 && Release(keychain), false)
                ? new(Capability.SecretStore, CapabilityStatus.Available, "Keychain (SecKeychainCopyDefault)")
                : new(Capability.SecretStore, CapabilityStatus.Unavailable, "Keychain", "связка ключей по умолчанию недоступна"),
            Environment.OSVersion.Version.Major >= 13
                ? new(Capability.Autostart, CapabilityStatus.Available, "SMAppService (macOS 13+)", Verified: false)
                : new(Capability.Autostart, CapabilityStatus.Limited, "LaunchAgent", "SMAppService появился в macOS 13", Verified: false),
            new(Capability.AllVirtualDesktops, CapabilityStatus.Available, "collectionBehavior = [.canJoinAllSpaces, .stationary]", Verified: false),
        };

        var environment = new ProbeEnvironment("macOS", Environment.OSVersion.Version.ToString(),
            RuntimeInformation.OSArchitecture.ToString(), "quartz", "WindowServer");
        return new CapabilityProbeResult(environment, checks, monitors, windowCount);
    }

    private static T Safe<T>(Func<T> call, T fallback)
    {
        try
        {
            return call();
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or MarshalDirectiveException)
        {
            return fallback;
        }
    }

    private static bool Release(IntPtr value)
    {
        if (value != IntPtr.Zero)
        {
            CFRelease(value);
        }

        return true;
    }

    private static List<MonitorInfo> Monitors()
    {
        var ids = new uint[16];
        if (CGGetActiveDisplayList((uint)ids.Length, ids, out var count) != 0)
        {
            return [];
        }

        var main = CGMainDisplayID();
        var monitors = new List<MonitorInfo>();
        foreach (var id in ids.Take((int)count))
        {
            var bounds = CGDisplayBounds(id);
            var scale = 1.0;
            var mode = CGDisplayCopyDisplayMode(id);
            if (mode != IntPtr.Zero)
            {
                var points = CGDisplayModeGetWidth(mode);
                scale = points > 0 ? (double)CGDisplayModeGetPixelWidth(mode) / points : 1.0;
                CGDisplayModeRelease(mode);
            }

            // CGDisplayBounds — в точках; переводим в физические пиксели.
            var rect = new PixelRect((int)(bounds.X * scale), (int)(bounds.Y * scale), (int)(bounds.Width * scale), (int)(bounds.Height * scale));
            monitors.Add(new MonitorInfo(rect, rect, scale, id == main));
        }

        return monitors;
    }

    /// <summary>Число обычных окон (слой 0) и PID владельца самого верхнего. Без имён и заголовков.</summary>
    private static (int Count, int FrontPid) Windows()
    {
        var list = CGWindowListCopyWindowInfo(OnScreenOnly | ExcludeDesktopElements, 0);
        if (list == IntPtr.Zero)
        {
            return (0, 0);
        }

        try
        {
            var graphics = NativeLibrary.Load(CoreGraphics);
            var layerKey = Marshal.ReadIntPtr(NativeLibrary.GetExport(graphics, "kCGWindowLayer"));
            var pidKey = Marshal.ReadIntPtr(NativeLibrary.GetExport(graphics, "kCGWindowOwnerPID"));

            int count = 0, frontPid = 0;
            var total = CFArrayGetCount(list);
            for (nint i = 0; i < total; i++)
            {
                var window = CFArrayGetValueAtIndex(list, i);
                if (ReadInt(window, layerKey) != 0)
                {
                    continue;
                }

                count++;
                if (frontPid == 0)
                {
                    frontPid = ReadInt(window, pidKey);
                }
            }

            return (count, frontPid);
        }
        finally
        {
            CFRelease(list);
        }
    }

    private static int ReadInt(IntPtr dictionary, IntPtr key)
    {
        var number = CFDictionaryGetValue(dictionary, key);
        return number != IntPtr.Zero && CFNumberGetValue(number, CFNumberIntType, out var value) ? value : -1;
    }

    private static bool Cursor()
    {
        var e = CGEventCreate(IntPtr.Zero);
        if (e == IntPtr.Zero)
        {
            return false;
        }

        _ = CGEventGetLocation(e);
        CFRelease(e);
        return true;
    }

    private static double IdleSeconds() => CGEventSourceSecondsSinceLastEventType(0, uint.MaxValue);

    [StructLayout(LayoutKind.Sequential)]
    private struct CGRect
    {
        public double X;
        public double Y;
        public double Width;
        public double Height;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CGPoint
    {
        public double X;
        public double Y;
    }

    [DllImport(CoreGraphics)]
    private static extern int CGGetActiveDisplayList(uint maxDisplays, [Out] uint[] displays, out uint count);

    [DllImport(CoreGraphics)]
    private static extern uint CGMainDisplayID();

    [DllImport(CoreGraphics)]
    private static extern CGRect CGDisplayBounds(uint display);

    [DllImport(CoreGraphics)]
    private static extern IntPtr CGDisplayCopyDisplayMode(uint display);

    [DllImport(CoreGraphics)]
    private static extern nuint CGDisplayModeGetWidth(IntPtr mode);

    [DllImport(CoreGraphics)]
    private static extern nuint CGDisplayModeGetPixelWidth(IntPtr mode);

    [DllImport(CoreGraphics)]
    private static extern void CGDisplayModeRelease(IntPtr mode);

    [DllImport(CoreGraphics)]
    private static extern IntPtr CGWindowListCopyWindowInfo(uint option, uint relativeToWindow);

    [DllImport(CoreGraphics)]
    private static extern IntPtr CGEventCreate(IntPtr source);

    [DllImport(CoreGraphics)]
    private static extern CGPoint CGEventGetLocation(IntPtr e);

    [DllImport(CoreGraphics)]
    private static extern double CGEventSourceSecondsSinceLastEventType(int stateId, uint eventType);

    [DllImport(CoreGraphics)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool CGPreflightScreenCaptureAccess();

    [DllImport(ApplicationServices)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool AXIsProcessTrusted();

    [DllImport(Security)]
    private static extern int SecKeychainCopyDefault(out IntPtr keychain);

    [DllImport(CoreFoundation)]
    private static extern nint CFArrayGetCount(IntPtr array);

    [DllImport(CoreFoundation)]
    private static extern IntPtr CFArrayGetValueAtIndex(IntPtr array, nint index);

    [DllImport(CoreFoundation)]
    private static extern IntPtr CFDictionaryGetValue(IntPtr dictionary, IntPtr key);

    [DllImport(CoreFoundation)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool CFNumberGetValue(IntPtr number, int type, out int value);

    [DllImport(CoreFoundation)]
    private static extern void CFRelease(IntPtr value);
}
