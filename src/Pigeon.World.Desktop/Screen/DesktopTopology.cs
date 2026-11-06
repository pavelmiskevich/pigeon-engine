using Pigeon.World.Desktop.Capabilities;

namespace Pigeon.World.Desktop.Screen;

/// <summary>
/// Стабильный ключ монитора (ТЗ §11.3): один и тот же монитор получает тот же ключ между
/// запусками. Платформа выводит его из идентификатора устройства и не раскрывает сам
/// идентификатор: ключ годится для памяти и баг-репортов.
/// </summary>
public readonly record struct MonitorKey
{
    public MonitorKey(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value;
    }

    public string Value { get; }

    public override string ToString() => Value;
}

/// <summary>У какого края монитора стоит панель задач.</summary>
public enum ScreenEdge
{
    Bottom = 0,
    Top = 1,
    Left = 2,
    Right = 3,
}

/// <param name="Bounds">Границы в физических пикселях виртуального экрана.</param>
/// <param name="WorkArea">Рабочая область: без панели задач и других закреплённых панелей.</param>
/// <param name="Scale">Масштаб: 1.0 = 96 DPI.</param>
public sealed record MonitorState(MonitorKey Key, PixelRect Bounds, PixelRect WorkArea, double Scale, bool IsPrimary);

/// <summary>Панель задач (ТЗ §11.5) на мониторе <paramref name="Monitor"/>.</summary>
public sealed record TaskbarState(MonitorKey Monitor, PixelRect Bounds, ScreenEdge Edge);

/// <summary>
/// Что сейчас на экране (ТЗ §11.3, §11.5): мониторы и панели задач. Снимок — неизменяемый:
/// изменения видны как разница двух снимков (<see cref="DesktopWorld.Refresh"/>).
/// </summary>
/// <param name="TaskbarsKnown">
/// Удалось ли прочитать панели задач (C13). Нет — значит, поверхностей панелей нет, но это не
/// то же самое, что панелей нет на экране.
/// </param>
public sealed record DesktopTopology(
    IReadOnlyList<MonitorState> Monitors,
    IReadOnlyList<TaskbarState> Taskbars,
    bool TaskbarsKnown)
{
    public static DesktopTopology Empty { get; } = new([], [], TaskbarsKnown: false);

    public MonitorState? Find(MonitorKey key) => Monitors.FirstOrDefault(m => m.Key == key);

    public MonitorState? Primary => Monitors.FirstOrDefault(m => m.IsPrimary) ?? Monitors.FirstOrDefault();
}

/// <summary>Платформенный источник топологии экрана; реализации — в <c>Pigeon.Desktop.*</c>.</summary>
public interface IDesktopEnvironment
{
    /// <summary>
    /// Текущие мониторы и панели. Пустой список мониторов — перечисление сейчас не работает
    /// (например, сессия RDP отключена), а не «мониторов нет».
    /// </summary>
    DesktopTopology ReadTopology();
}
