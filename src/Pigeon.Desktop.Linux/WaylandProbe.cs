using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Pigeon.World.Desktop.Capabilities;

namespace Pigeon.Desktop.Linux;

/// <summary>
/// Проба Wayland: возможности определяются набором глобальных протоколов композитора
/// (docs/platform/capability-matrix.md, механизмы Wayland). Подключается к композитору, читает
/// <c>wl_registry</c> и отключается; окон не создаёт.
/// </summary>
[SupportedOSPlatform("linux")]
internal static unsafe class WaylandProbe
{
    private const string Client = "libwayland-client.so.0";
    private const int DisplayGetRegistry = 1;

    private static readonly List<string> Globals = [];

    public static CapabilityProbeResult? Probe()
    {
        if (Environment.GetEnvironmentVariable("WAYLAND_DISPLAY") is null || !NativeLibrary.TryLoad(Client, out var library))
        {
            return null;
        }

        try
        {
            var globals = ReadGlobals(library);
            return globals is null ? null : Build(globals);
        }
        finally
        {
            NativeLibrary.Free(library);
        }
    }

    private static List<string>? ReadGlobals(IntPtr library)
    {
        var display = wl_display_connect(IntPtr.Zero);
        if (display == IntPtr.Zero)
        {
            return null;
        }

        var listener = (IntPtr*)NativeMemory.Alloc(2, (nuint)sizeof(IntPtr));
        try
        {
            // wl_display_get_registry — inline-функция над wl_proxy_marshal_constructor.
            var registryInterface = NativeLibrary.GetExport(library, "wl_registry_interface");
            var registry = wl_proxy_marshal_constructor(display, DisplayGetRegistry, registryInterface, IntPtr.Zero);
            if (registry == IntPtr.Zero)
            {
                return null;
            }

            listener[0] = (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, IntPtr, uint, IntPtr, uint, void>)&OnGlobal;
            listener[1] = (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, IntPtr, uint, void>)&OnGlobalRemove;
            Globals.Clear();
            wl_proxy_add_listener(registry, (IntPtr)listener, IntPtr.Zero);
            wl_display_roundtrip(display);
            wl_proxy_destroy(registry);
            return [.. Globals];
        }
        finally
        {
            wl_display_disconnect(display);
            NativeMemory.Free(listener);
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnGlobal(IntPtr data, IntPtr registry, uint name, IntPtr iface, uint version)
    {
        var interfaceName = Marshal.PtrToStringUTF8(iface);
        if (interfaceName is not null)
        {
            Globals.Add(interfaceName);
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnGlobalRemove(IntPtr data, IntPtr registry, uint name)
    {
    }

    private static CapabilityProbeResult Build(List<string> globals)
    {
        bool Has(string name) => globals.Contains(name);

        var layerShell = Has("zwlr_layer_shell_v1");
        var toplevels = new[] { "zwlr_foreign_toplevel_manager_v1", "org_kde_plasma_window_management", "ext_foreign_toplevel_list_v1" }
            .Where(Has).ToList();
        var toplevelMechanism = toplevels.Count == 0 ? "foreign-toplevel" : string.Join(", ", toplevels);
        var outputs = globals.Count(g => g == "wl_output");
        var idle = new[] { "ext_idle_notifier_v1", "org_kde_kwin_idle" }.Where(Has).ToList();

        var checks = new List<CapabilityCheck>
        {
            Has("wl_compositor") && Has("wl_shm")
                ? new(Capability.TransparentOverlay, CapabilityStatus.Available, "wl_compositor + ARGB-буфер")
                : new(Capability.TransparentOverlay, CapabilityStatus.Unavailable, "wl_compositor", "нет wl_compositor или wl_shm"),
            new(Capability.ClickThrough, Has("wl_compositor") ? CapabilityStatus.Available : CapabilityStatus.Unavailable,
                "wl_surface.set_input_region — только хитбокс голубя"),
            layerShell
                ? new(Capability.AlwaysOnTop, CapabilityStatus.Available, "zwlr_layer_shell_v1, слой overlay")
                : new(Capability.AlwaysOnTop, CapabilityStatus.Unavailable, "zwlr_layer_shell_v1", "композитор не поддерживает layer-shell (так у GNOME)"),
            layerShell
                ? new(Capability.NoFocusNoTaskbar, CapabilityStatus.Available, "layer-shell surface, keyboard_interactivity = none")
                : new(Capability.NoFocusNoTaskbar, CapabilityStatus.Limited, "xdg_toplevel", "обычное окно видно в переключателе окон"),
            outputs > 0
                ? new(Capability.MonitorEnumeration, CapabilityStatus.Available,
                    Has("zxdg_output_manager_v1") ? "wl_output + zxdg_output_manager_v1" : "wl_output", $"мониторов: {outputs}")
                : new(Capability.MonitorEnumeration, CapabilityStatus.Unavailable, "wl_output", "мониторы не объявлены"),
            new(Capability.MonitorEvents, CapabilityStatus.Available, "wl_registry: появление и удаление wl_output", Verified: false),
            new(Capability.GlobalCursor, CapabilityStatus.Unavailable, "—",
                "глобального курсора в протоколе нет: позиция известна только над голубем"),
            new(Capability.WindowGeometry, CapabilityStatus.Unavailable, "—",
                "протокол не отдаёт геометрию чужих окон; на KDE возможен скрипт KWin (вне MVP)"),
            new(Capability.WindowZOrder, CapabilityStatus.Unavailable, "—", "протокол не отдаёт z-order"),
            new(Capability.WindowEvents, CapabilityStatus.Unavailable, "—", "без геометрии окон событий перемещения нет"),
            toplevels.Count > 0
                ? new(Capability.ActiveApplication, CapabilityStatus.Limited, toplevelMechanism, "app_id и состояние activated, без геометрии")
                : new(Capability.ActiveApplication, CapabilityStatus.Unavailable, "foreign-toplevel", "композитор не отдаёт список окон"),
            toplevels.Count > 0
                ? new(Capability.WindowTitles, CapabilityStatus.Limited, toplevelMechanism, "читается только при capability Window, ТЗ §14")
                : new(Capability.WindowTitles, CapabilityStatus.Unavailable, "foreign-toplevel"),
            layerShell
                ? new(Capability.TaskbarGeometry, CapabilityStatus.Limited, "layer-shell exclusive zone", "панели видны только через уменьшение рабочей области")
                : new(Capability.TaskbarGeometry, CapabilityStatus.Unavailable, "—"),
            toplevels.Count > 0
                ? new(Capability.FullscreenDetection, CapabilityStatus.Limited, toplevelMechanism, "состояние fullscreen, без геометрии")
                : new(Capability.FullscreenDetection, CapabilityStatus.Unavailable, "foreign-toplevel"),
            new(Capability.CaptureExclusion, CapabilityStatus.Unavailable, "—", "механизма нет", Verified: false),
            idle.Count > 0
                ? new(Capability.IdleTime, CapabilityStatus.Available, string.Join(", ", idle))
                : new(Capability.IdleTime, CapabilityStatus.Limited, "D-Bus композитора", "протокола idle-notify нет"),
            LinuxCommon.SessionEvents(),
            LinuxCommon.SecretStore(),
            LinuxCommon.Autostart(),
            layerShell
                ? new(Capability.AllVirtualDesktops, CapabilityStatus.Available, "layer-shell surface не привязана к рабочему столу")
                : new(Capability.AllVirtualDesktops, CapabilityStatus.Unavailable, "—", "обычное окно живёт на одном рабочем столе"),
        };

        var environment = new ProbeEnvironment("Linux", LinuxCommon.OsVersion(), RuntimeInformation.OSArchitecture.ToString(), "wayland",
            Environment.GetEnvironmentVariable("XDG_CURRENT_DESKTOP"));
        return new CapabilityProbeResult(environment, checks, [], 0)
        {
            Protocols = [.. globals.Distinct().Order(StringComparer.Ordinal)],
        };
    }

    [DllImport(Client)]
    private static extern IntPtr wl_display_connect(IntPtr name);

    [DllImport(Client)]
    private static extern void wl_display_disconnect(IntPtr display);

    [DllImport(Client)]
    private static extern int wl_display_roundtrip(IntPtr display);

    // Вариадическая функция; на Linux x64 и arm64 аргументы-указатели передаются как у обычной.
    [DllImport(Client)]
    private static extern IntPtr wl_proxy_marshal_constructor(IntPtr proxy, uint opcode, IntPtr iface, IntPtr newId);

    [DllImport(Client)]
    private static extern int wl_proxy_add_listener(IntPtr proxy, IntPtr implementation, IntPtr data);

    [DllImport(Client)]
    private static extern void wl_proxy_destroy(IntPtr proxy);
}
