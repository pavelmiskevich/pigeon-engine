namespace Pigeon.World.Desktop.Capabilities;

/// <summary>Уровни Desktop-опыта (docs/platform/capability-matrix.md, §4).</summary>
public enum DesktopLevel
{
    /// <summary>Отрисовки нет, симуляция идёт с редким тиком.</summary>
    Background = 0,

    /// <summary>Голубь живёт в обычном окне («вольер»).</summary>
    WindowMode = 1,

    /// <summary>Поверхности экрана: кромка work area, панели, края мониторов.</summary>
    ScreenWorld = 2,

    /// <summary>Всё из ТЗ: насесты на окнах с окклюзией, курсор на расстоянии.</summary>
    FullDesktopWorld = 3,
}

public static class DesktopLevels
{
    private static readonly Capability[] ScreenWorldRequires =
    [
        Capability.TransparentOverlay,
        Capability.ClickThrough,
        Capability.AlwaysOnTop,
        Capability.NoFocusNoTaskbar,
        Capability.MonitorEnumeration,
    ];

    private static readonly Capability[] FullDesktopRequires =
    [
        .. ScreenWorldRequires,
        Capability.GlobalCursor,
        Capability.WindowGeometry,
        Capability.WindowZOrder,
    ];

    /// <summary>
    /// L3 — C1–C5, C7, C8, C9; L2 — C1–C5; иначе L1. Возможность с ограничением (⚠️) считается
    /// пригодной. L0 проба не выбирает: он означает, что отрисовка недоступна прямо сейчас.
    /// </summary>
    public static DesktopLevel Compute(IReadOnlyCollection<CapabilityCheck> checks)
    {
        ArgumentNullException.ThrowIfNull(checks);

        var usable = checks.Where(c => c.IsUsable).Select(c => c.Capability).ToHashSet();
        if (FullDesktopRequires.All(usable.Contains))
        {
            return DesktopLevel.FullDesktopWorld;
        }

        return ScreenWorldRequires.All(usable.Contains) ? DesktopLevel.ScreenWorld : DesktopLevel.WindowMode;
    }

    /// <summary>Чего не хватает до следующего уровня.</summary>
    public static IReadOnlyList<Capability> MissingForNextLevel(IReadOnlyCollection<CapabilityCheck> checks)
    {
        ArgumentNullException.ThrowIfNull(checks);

        var usable = checks.Where(c => c.IsUsable).Select(c => c.Capability).ToHashSet();
        var target = Compute(checks) switch
        {
            DesktopLevel.FullDesktopWorld => [],
            DesktopLevel.ScreenWorld => FullDesktopRequires,
            _ => ScreenWorldRequires,
        };
        return [.. target.Where(c => !usable.Contains(c))];
    }

    public static string Code(this DesktopLevel level) => $"L{(int)level}";
}
