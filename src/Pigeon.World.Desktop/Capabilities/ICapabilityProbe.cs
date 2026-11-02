namespace Pigeon.World.Desktop.Capabilities;

/// <summary>
/// Определяет возможности платформы при запуске (ТЗ §2.3, ADR-0011). Реализации — в
/// платформенных проектах <c>Pigeon.Desktop.*</c>; ядро видит только результат.
/// </summary>
public interface ICapabilityProbe
{
    CapabilityProbeResult Probe();
}

/// <param name="Session">Тип сессии: win32, quartz, x11, wayland.</param>
/// <param name="Compositor">Композитор или оконный менеджер, если известен.</param>
public sealed record ProbeEnvironment(
    string Os,
    string OsVersion,
    string Architecture,
    string Session,
    string? Compositor);

/// <param name="Bounds">Границы в физических пикселях.</param>
/// <param name="Scale">Масштаб: 1.0 = 96 DPI.</param>
public sealed record MonitorInfo(
    PixelRect Bounds,
    PixelRect WorkArea,
    double Scale,
    bool IsPrimary);

public readonly record struct PixelRect(int X, int Y, int Width, int Height)
{
    public override string ToString() => $"{Width}×{Height} @ ({X}, {Y})";
}

/// <summary>
/// Результат пробы. Без заголовков окон, имён приложений и путей: отчёт пригоден для
/// баг-репорта (ТЗ §14).
/// </summary>
public sealed record CapabilityProbeResult(
    ProbeEnvironment Environment,
    IReadOnlyList<CapabilityCheck> Checks,
    IReadOnlyList<MonitorInfo> Monitors,
    int VisibleWindowCount)
{
    public DesktopLevel Level => DesktopLevels.Compute(Checks);

    /// <summary>Глобальные протоколы композитора Wayland, если проба их читала.</summary>
    public IReadOnlyList<string> Protocols { get; init; } = [];

    public CapabilityCheck this[Capability capability] =>
        Checks.Single(c => c.Capability == capability);
}
