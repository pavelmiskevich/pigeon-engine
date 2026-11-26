using Pigeon.Common.Hashing;

namespace Pigeon.Core;

/// <summary>Вид события хроники (ТЗ §22). Числа — часть формата снимка: новые только в конец.</summary>
public enum LifeEventKind
{
    Born = 0,
    FirstWalk = 1,
    FirstInteraction = 2,
    FirstSleep = 3,
    FirstAdventure = 4,
    FirstMultiMonitorTransition = 5,
    FirstVisitToMonitor = 6,
    FirstFavoriteTerritory = 7,
}

/// <summary>Запись хроники.</summary>
/// <param name="Key">Идемпотентный ключ: второй раз событие с тем же ключом не записывается.</param>
/// <param name="Place">Место события; есть только у <see cref="LifeEventKind.FirstVisitToMonitor"/>.</param>
/// <param name="Tick">Тик симуляции, когда событие случилось.</param>
/// <param name="LifeSeconds">Возраст голубя в секундах, включая время, пока приложение не работало.</param>
/// <param name="Version">Версия определения события (<see cref="LifeStory.CurrentVersion"/>).</param>
public sealed record LifeEvent(string Key, LifeEventKind Kind, PlaceKey? Place, long Tick, long LifeSeconds, int Version);

/// <summary>
/// Хроника жизни голубя (#33, ТЗ §22): только добавление, каждое событие «впервые» — один раз.
/// Живёт в состоянии голубя и его снимке, поэтому после восстановления и воспроизведения
/// журнала события не повторяются.
/// </summary>
public sealed class LifeStory
{
    /// <summary>Версия определений событий, с которой пишутся новые записи.</summary>
    public const int CurrentVersion = 1;

    /// <summary>Больше записей «впервые на мониторе» не бывает: столько мониторов не подключают.</summary>
    public const int MaxMonitorVisits = 32;

    private readonly List<LifeEvent> _events = [];
    private readonly HashSet<string> _keys = new(StringComparer.Ordinal);
    private int _monitorVisits;

    public LifeStory() => Events = _events.AsReadOnly();

    /// <summary>Записи в порядке добавления.</summary>
    public IReadOnlyList<LifeEvent> Events { get; }

    /// <exception cref="ArgumentException">Ключ повторяется, не соответствует виду, вид неизвестен или числа отрицательные.</exception>
    public static LifeStory FromSnapshot(IReadOnlyList<LifeEvent> events)
    {
        ArgumentNullException.ThrowIfNull(events);

        var story = new LifeStory();
        foreach (var entry in events)
        {
            ArgumentNullException.ThrowIfNull(entry, nameof(events));
            if (!Enum.IsDefined(entry.Kind))
            {
                throw new ArgumentException($"Неизвестный вид события хроники: {(int)entry.Kind}.", nameof(events));
            }

            if (entry.Tick < 0 || entry.LifeSeconds < 0 || entry.Version < 1)
            {
                throw new ArgumentException($"Запись хроники «{entry.Key}» с отрицательным временем или неверной версией.", nameof(events));
            }

            if ((entry.Kind == LifeEventKind.FirstVisitToMonitor) != entry.Place.HasValue
                || !string.Equals(entry.Key, KeyOf(entry.Kind, entry.Place), StringComparison.Ordinal))
            {
                throw new ArgumentException($"Ключ записи хроники «{entry.Key}» не соответствует её виду {entry.Kind}.", nameof(events));
            }

            if (!story.TryAdd(entry))
            {
                throw new ArgumentException($"Запись хроники «{entry.Key}» повторяется или их больше предела.", nameof(events));
            }
        }

        return story;
    }

    /// <summary>Ключ события. Ключи — формат снимка, менять их нельзя.</summary>
    /// <exception cref="ArgumentException">У <see cref="LifeEventKind.FirstVisitToMonitor"/> нет места.</exception>
    public static string KeyOf(LifeEventKind kind, PlaceKey? place) => kind switch
    {
        LifeEventKind.Born => "born",
        LifeEventKind.FirstWalk => "first-walk",
        LifeEventKind.FirstInteraction => "first-interaction",
        LifeEventKind.FirstSleep => "first-sleep",
        LifeEventKind.FirstAdventure => "first-adventure",
        LifeEventKind.FirstMultiMonitorTransition => "first-multi-monitor-transition",
        LifeEventKind.FirstVisitToMonitor => place?.Value is { } value
            ? "first-visit:" + value
            : throw new ArgumentException("У первого визита на монитор должно быть место.", nameof(place)),
        LifeEventKind.FirstFavoriteTerritory => "first-favorite-territory",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Неизвестный вид события хроники."),
    };

    public bool Contains(string key) => _keys.Contains(key);

    /// <summary>
    /// Записывает событие; <c>false</c> — такое уже было (или мониторов в хронике уже
    /// <see cref="MaxMonitorVisits"/>), хроника не меняется.
    /// </summary>
    public bool Record(LifeEventKind kind, PlaceKey? place, long tick, long lifeSeconds)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(tick);
        ArgumentOutOfRangeException.ThrowIfNegative(lifeSeconds);

        var key = KeyOf(kind, place);
        return TryAdd(new LifeEvent(key, kind, kind == LifeEventKind.FirstVisitToMonitor ? place : null, tick, lifeSeconds, CurrentVersion));
    }

    public void AddTo(StableHasher hasher)
    {
        ArgumentNullException.ThrowIfNull(hasher);

        hasher.Add(_events.Count);
        foreach (var entry in _events)
        {
            hasher.Add(entry.Key);
            hasher.Add((int)entry.Kind);
            hasher.Add(entry.Place.HasValue);
            hasher.Add(entry.Place?.Value ?? string.Empty);
            hasher.Add(entry.Tick);
            hasher.Add(entry.LifeSeconds);
            hasher.Add(entry.Version);
        }
    }

    private bool TryAdd(LifeEvent entry)
    {
        var monitor = entry.Kind == LifeEventKind.FirstVisitToMonitor;
        if (_keys.Contains(entry.Key) || (monitor && _monitorVisits >= MaxMonitorVisits))
        {
            return false;
        }

        _keys.Add(entry.Key);
        _events.Add(entry);
        if (monitor)
        {
            _monitorVisits++;
        }

        return true;
    }
}
