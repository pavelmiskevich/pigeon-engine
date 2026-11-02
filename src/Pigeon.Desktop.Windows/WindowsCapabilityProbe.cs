using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Pigeon.World.Desktop.Capabilities;
using static Pigeon.Desktop.Windows.Native;

namespace Pigeon.Desktop.Windows;

/// <summary>
/// Проба возможностей Windows (docs/platform/capability-matrix.md, механизмы Windows). Каждая
/// возможность проверяется вызовом API; данные пользователя (заголовки, имена приложений) не
/// читаются и не возвращаются.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsCapabilityProbe : ICapabilityProbe
{
    private static readonly IntPtr PerMonitorAwareV2 = -4;

    public CapabilityProbeResult Probe()
    {
        // Координаты и масштаб — в физических пикселях каждого монитора.
        var previousAwareness = SetThreadDpiAwarenessContext(PerMonitorAwareV2);
        try
        {
            var checks = new List<CapabilityCheck>();
            var monitors = EnumerateMonitors();
            var windowCount = CountVisibleWindows();

            ProbeOverlayWindow(checks);

            checks.Add(monitors.Count > 0
                ? new(Capability.MonitorEnumeration, CapabilityStatus.Available, "EnumDisplayMonitors + GetDpiForMonitor (Per-Monitor V2)")
                : new(Capability.MonitorEnumeration, CapabilityStatus.Unavailable, "EnumDisplayMonitors", "мониторы не найдены"));
            checks.Add(new(Capability.MonitorEvents, CapabilityStatus.Available, "WM_DISPLAYCHANGE, WM_DPICHANGED",
                "события приходят в оконную процедуру; проба их не дожидается", Verified: false));
            checks.Add(GetCursorPos(out _)
                ? new(Capability.GlobalCursor, CapabilityStatus.Available, "GetCursorPos")
                : new(Capability.GlobalCursor, CapabilityStatus.Unavailable, "GetCursorPos", "вызов не удался"));
            checks.Add(windowCount > 0
                ? new(Capability.WindowGeometry, CapabilityStatus.Available, "EnumWindows + DWMWA_EXTENDED_FRAME_BOUNDS, без cloaked")
                : new(Capability.WindowGeometry, CapabilityStatus.Unavailable, "EnumWindows", "видимых окон не найдено"));
            checks.Add(new(Capability.WindowZOrder, windowCount > 0 ? CapabilityStatus.Available : CapabilityStatus.Unavailable,
                "порядок EnumWindows"));
            checks.Add(ProbeWinEventHook());
            checks.Add(ProbeActiveApplication());
            checks.Add(ProbeWindowTitles());
            checks.Add(ProbeTaskbar());
            checks.Add(SHQueryUserNotificationState(out var state) == 0
                ? new(Capability.FullscreenDetection, CapabilityStatus.Available, "SHQueryUserNotificationState", $"состояние сейчас: {state}")
                : new(Capability.FullscreenDetection, CapabilityStatus.Unavailable, "SHQueryUserNotificationState"));
            checks.Add(ProbeIdle());
            checks.Add(ProbeSecretStore());
            checks.Add(ProbeAutostart());
            checks.Add(new(Capability.AllVirtualDesktops, CapabilityStatus.Limited, "WS_EX_TOOLWINDOW + topmost",
                "закрепление на всех столах — недокументированный API; поведение проверяется вручную", Verified: false));

            checks.Sort((a, b) => a.Capability.CompareTo(b.Capability));
            var environment = new ProbeEnvironment(
                "Windows",
                Environment.OSVersion.Version.ToString(),
                RuntimeInformation.OSArchitecture.ToString(),
                "win32",
                "DWM");
            return new CapabilityProbeResult(environment, checks, monitors, windowCount);
        }
        finally
        {
            if (previousAwareness != IntPtr.Zero)
            {
                SetThreadDpiAwarenessContext(previousAwareness);
            }
        }
    }

    /// <summary>
    /// C1–C4, C15, C17: создаёт невидимое окно с нужными стилями, проверяет, что система их
    /// приняла, и сразу его уничтожает.
    /// </summary>
    private static void ProbeOverlayWindow(List<CapabilityCheck> checks)
    {
        const uint exStyle = WsExLayered | WsExTransparent | WsExTopmost | WsExToolWindow | WsExNoActivate;
        var window = CreateWindowExW(exStyle, "STATIC", null, WsPopup, 0, 0, 1, 1, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        if (window == IntPtr.Zero)
        {
            var error = Marshal.GetLastPInvokeError();
            foreach (var capability in new[] { Capability.TransparentOverlay, Capability.ClickThrough, Capability.AlwaysOnTop, Capability.NoFocusNoTaskbar, Capability.CaptureExclusion, Capability.SessionEvents })
            {
                checks.Add(new(capability, CapabilityStatus.Unavailable, "CreateWindowEx", $"окно не создано, ошибка {error}"));
            }

            return;
        }

        try
        {
            var applied = (uint)GetWindowLongPtrW(window, GwlExStyle).ToInt64();
            bool Has(uint flag) => (applied & flag) == flag;

            checks.Add(Check(Capability.TransparentOverlay, Has(WsExLayered), "WS_EX_LAYERED (попиксельная альфа через UpdateLayeredWindow)"));
            checks.Add(Check(Capability.ClickThrough, Has(WsExTransparent) && Has(WsExLayered), "WS_EX_LAYERED + WS_EX_TRANSPARENT; прозрачные пиксели пропускают клики"));
            checks.Add(Check(Capability.AlwaysOnTop, Has(WsExTopmost), "WS_EX_TOPMOST"));
            checks.Add(Check(Capability.NoFocusNoTaskbar, Has(WsExNoActivate) && Has(WsExToolWindow), "WS_EX_NOACTIVATE + WS_EX_TOOLWINDOW"));
            checks.Add(SetWindowDisplayAffinity(window, WdaExcludeFromCapture)
                ? new(Capability.CaptureExclusion, CapabilityStatus.Available, "SetWindowDisplayAffinity(WDA_EXCLUDEFROMCAPTURE)")
                : new(Capability.CaptureExclusion, CapabilityStatus.Unavailable, "SetWindowDisplayAffinity", $"ошибка {Marshal.GetLastPInvokeError()}; нужна Windows 10 2004+"));

            if (WTSRegisterSessionNotification(window, NotifyForThisSession))
            {
                WTSUnRegisterSessionNotification(window);
                checks.Add(new(Capability.SessionEvents, CapabilityStatus.Available, "WTSRegisterSessionNotification + WM_POWERBROADCAST"));
            }
            else
            {
                checks.Add(new(Capability.SessionEvents, CapabilityStatus.Unavailable, "WTSRegisterSessionNotification", $"ошибка {Marshal.GetLastPInvokeError()}"));
            }
        }
        finally
        {
            DestroyWindow(window);
        }

        static CapabilityCheck Check(Capability capability, bool ok, string mechanism) =>
            new(capability, ok ? CapabilityStatus.Available : CapabilityStatus.Unavailable, mechanism, ok ? null : "система не приняла стиль окна");
    }

    private static List<World.Desktop.Capabilities.MonitorInfo> EnumerateMonitors()
    {
        var monitors = new List<World.Desktop.Capabilities.MonitorInfo>();
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (monitor, _, _, _) =>
        {
            var info = new Native.MonitorInfo { Size = Marshal.SizeOf<Native.MonitorInfo>() };
            if (GetMonitorInfo(monitor, ref info))
            {
                var scale = GetDpiForMonitor(monitor, MdtEffectiveDpi, out var dpiX, out _) == 0 ? dpiX / 96.0 : 1.0;
                monitors.Add(new(ToRect(info.Monitor), ToRect(info.Work), scale, (info.Flags & MonitorInfoFPrimary) != 0));
            }

            return true;
        }, IntPtr.Zero);
        return monitors;
    }

    /// <summary>Видимые, не свёрнутые и не cloaked окна с ненулевой рамкой. Без заголовков.</summary>
    private static int CountVisibleWindows()
    {
        var count = 0;
        EnumWindows((window, _) =>
        {
            if (IsWindowVisible(window) && !IsIconic(window)
                && DwmGetWindowAttributeInt(window, DwmwaCloaked, out var cloaked, sizeof(int)) == 0 && cloaked == 0
                && DwmGetWindowAttribute(window, DwmwaExtendedFrameBounds, out var bounds, Marshal.SizeOf<Rect>()) == 0
                && bounds.Right > bounds.Left && bounds.Bottom > bounds.Top)
            {
                count++;
            }

            return true;
        }, IntPtr.Zero);
        return count;
    }

    private static CapabilityCheck ProbeWinEventHook()
    {
        WinEventProc callback = static (_, _, _, _, _, _, _) => { };
        var hook = SetWinEventHook(EventSystemForeground, EventObjectLocationChange, IntPtr.Zero, callback, 0, 0, WinEventOutOfContext);
        if (hook == IntPtr.Zero)
        {
            return new(Capability.WindowEvents, CapabilityStatus.Unavailable, "SetWinEventHook", "хук не установлен");
        }

        UnhookWinEvent(hook);
        GC.KeepAlive(callback);
        return new(Capability.WindowEvents, CapabilityStatus.Available, "SetWinEventHook (out-of-context): foreground, location change, show/hide");
    }

    /// <summary>C11: путь к исполняемому файлу активного окна доступен; сам путь не возвращается.</summary>
    private static CapabilityCheck ProbeActiveApplication()
    {
        var foreground = GetForegroundWindow();
        if (foreground == IntPtr.Zero || GetWindowThreadProcessId(foreground, out var pid) == 0)
        {
            return new(Capability.ActiveApplication, CapabilityStatus.Unknown, "GetForegroundWindow", "нет активного окна во время пробы");
        }

        var process = OpenProcess(ProcessQueryLimitedInformation, false, pid);
        if (process == IntPtr.Zero)
        {
            return new(Capability.ActiveApplication, CapabilityStatus.Limited, "OpenProcess(QUERY_LIMITED_INFORMATION)",
                "процесс активного окна недоступен (например, запущен от администратора)");
        }

        try
        {
            var buffer = new char[1024];
            var size = (uint)buffer.Length;
            var ok = QueryFullProcessImageName(process, 0, buffer, ref size) && size > 0;
            Array.Clear(buffer);
            return ok
                ? new(Capability.ActiveApplication, CapabilityStatus.Available, "GetForegroundWindow → PID → QueryFullProcessImageName")
                : new(Capability.ActiveApplication, CapabilityStatus.Limited, "QueryFullProcessImageName", "имя исполняемого файла не получено");
        }
        finally
        {
            CloseHandle(process);
        }
    }

    /// <summary>C12: только длина заголовка, сам текст не читается (ТЗ §14).</summary>
    private static CapabilityCheck ProbeWindowTitles()
    {
        var foreground = GetForegroundWindow();
        return foreground != IntPtr.Zero && GetWindowTextLength(foreground) > 0
            ? new(Capability.WindowTitles, CapabilityStatus.Available, "GetWindowText (проверена только длина)", "читается только при capability Window, ТЗ §14")
            : new(Capability.WindowTitles, CapabilityStatus.Unknown, "GetWindowTextLength", "у активного окна нет заголовка");
    }

    private static CapabilityCheck ProbeTaskbar()
    {
        var data = new AppBarData { Size = (uint)Marshal.SizeOf<AppBarData>() };
        return SHAppBarMessage(AbmGetTaskbarPos, ref data) != UIntPtr.Zero
            ? new(Capability.TaskbarGeometry, CapabilityStatus.Available, "SHAppBarMessage(ABM_GETTASKBARPOS)", $"панель задач: {ToRect(data.Rect)}")
            : new(Capability.TaskbarGeometry, CapabilityStatus.Unavailable, "SHAppBarMessage", "панель задач не найдена");
    }

    private static CapabilityCheck ProbeIdle()
    {
        var info = new LastInputInfo { Size = (uint)Marshal.SizeOf<LastInputInfo>() };
        return GetLastInputInfo(ref info)
            ? new(Capability.IdleTime, CapabilityStatus.Available, "GetLastInputInfo", $"бездействие: {(GetTickCount() - info.Time) / 1000} с")
            : new(Capability.IdleTime, CapabilityStatus.Unavailable, "GetLastInputInfo");
    }

    /// <summary>C18: чтение несуществующей записи Credential Manager должно вернуть «не найдено».</summary>
    private static CapabilityCheck ProbeSecretStore()
    {
        if (CredRead("pigeon-probe/nonexistent", CredTypeGeneric, 0, out var credential))
        {
            CredFree(credential);
            return new(Capability.SecretStore, CapabilityStatus.Available, "Credential Manager (CredRead)");
        }

        var error = Marshal.GetLastPInvokeError();
        return error == ErrorNotFound
            ? new(Capability.SecretStore, CapabilityStatus.Available, "Credential Manager (CredRead)")
            : new(Capability.SecretStore, CapabilityStatus.Unavailable, "Credential Manager", $"ошибка {error}");
    }

    /// <summary>C19: ключ автозапуска открывается на чтение; проба ничего не записывает.</summary>
    private static CapabilityCheck ProbeAutostart()
    {
        if (RegOpenKeyEx(HkeyCurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Run", 0, KeyRead, out var key) == 0)
        {
            RegCloseKey(key);
            return new(Capability.Autostart, CapabilityStatus.Available, @"HKCU\...\CurrentVersion\Run (проверено чтение)");
        }

        return new(Capability.Autostart, CapabilityStatus.Unavailable, @"HKCU\...\CurrentVersion\Run", "ключ не открывается");
    }

    private static PixelRect ToRect(Rect r) => new(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top);
}
