using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Pigeon.World.Desktop.Capabilities;

namespace Pigeon.Desktop.Linux;

/// <summary>
/// Проба Linux: Wayland, если есть <c>WAYLAND_DISPLAY</c>, иначе X11. С <paramref name="forceX11"/>
/// проверяется XWayland внутри Wayland-сессии.
/// </summary>
[SupportedOSPlatform("linux")]
public sealed class LinuxCapabilityProbe(bool forceX11 = false) : ICapabilityProbe
{
    public CapabilityProbeResult Probe()
    {
        var result = (forceX11 ? null : WaylandProbe.Probe()) ?? X11Probe.Probe();
        if (result is not null)
        {
            return result;
        }

        // Ни композитора, ни X-сервера: остаётся только режим окна.
        var environment = new ProbeEnvironment("Linux", LinuxCommon.OsVersion(), RuntimeInformation.OSArchitecture.ToString(),
            "none", null);
        var checks = Enum.GetValues<Capability>()
            .Select(c => new CapabilityCheck(c, CapabilityStatus.Unknown, "—", "нет ни WAYLAND_DISPLAY, ни DISPLAY"))
            .ToList();
        return new CapabilityProbeResult(environment, checks, [], 0);
    }
}
