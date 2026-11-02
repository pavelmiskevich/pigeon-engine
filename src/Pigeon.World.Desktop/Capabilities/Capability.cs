namespace Pigeon.World.Desktop.Capabilities;

/// <summary>Возможности платформы C1–C20 (docs/platform/capability-matrix.md, §2).</summary>
public enum Capability
{
    /// <summary>Прозрачное окно-оверлей с попиксельной альфой.</summary>
    TransparentOverlay = 1,

    /// <summary>Click-through везде, кроме голубя.</summary>
    ClickThrough = 2,

    /// <summary>Поверх обычных окон.</summary>
    AlwaysOnTop = 3,

    /// <summary>Не забирает фокус, не видно в Alt-Tab, таскбаре и Dock.</summary>
    NoFocusNoTaskbar = 4,

    /// <summary>Мониторы: границы, рабочая область, масштаб, основной.</summary>
    MonitorEnumeration = 5,

    /// <summary>События подключения и отключения мониторов, смены DPI.</summary>
    MonitorEvents = 6,

    /// <summary>Глобальная позиция курсора.</summary>
    GlobalCursor = 7,

    /// <summary>Геометрия чужих окон верхнего уровня.</summary>
    WindowGeometry = 8,

    /// <summary>Z-order окон.</summary>
    WindowZOrder = 9,

    /// <summary>Push-события чужих окон.</summary>
    WindowEvents = 10,

    /// <summary>Идентичность активного приложения.</summary>
    ActiveApplication = 11,

    /// <summary>Заголовки окон (только по явному разрешению, ТЗ §14).</summary>
    WindowTitles = 12,

    /// <summary>Геометрия taskbar, Dock и панелей.</summary>
    TaskbarGeometry = 13,

    /// <summary>Детекция полноэкранного приложения и презентации.</summary>
    FullscreenDetection = 14,

    /// <summary>Исключение оверлея из захвата экрана.</summary>
    CaptureExclusion = 15,

    /// <summary>Время бездействия пользователя.</summary>
    IdleTime = 16,

    /// <summary>Сон и пробуждение, блокировка сессии.</summary>
    SessionEvents = 17,

    /// <summary>Защищённое хранилище секретов.</summary>
    SecretStore = 18,

    /// <summary>Автозапуск.</summary>
    Autostart = 19,

    /// <summary>Показ на всех виртуальных рабочих столах.</summary>
    AllVirtualDesktops = 20,
}

/// <summary>Обозначения матрицы: ✅ / ⚠️ / ❌ / ?.</summary>
public enum CapabilityStatus
{
    Unknown,
    Available,
    Limited,
    Unavailable,
}

/// <param name="Mechanism">Чем проверено или чем обеспечивается (API, протокол).</param>
/// <param name="Note">Ограничение или причина отсутствия.</param>
/// <param name="Verified">true — проверено вызовом API; false — выведено из версии ОС или документации.</param>
public sealed record CapabilityCheck(
    Capability Capability,
    CapabilityStatus Status,
    string Mechanism,
    string? Note = null,
    bool Verified = true)
{
    public string Id => $"C{(int)Capability}";

    public bool IsUsable => Status is CapabilityStatus.Available or CapabilityStatus.Limited;
}
