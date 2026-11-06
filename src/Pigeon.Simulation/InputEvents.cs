namespace Pigeon.Simulation;

/// <summary>
/// Внешний вход симуляции: действие пользователя или событие мира. Входы — единственное, что
/// влияет на симуляцию снаружи, поэтому их журнал вместе с seed воспроизводит её (ТЗ §3.3).
/// </summary>
public abstract record InputEvent;

/// <summary>Пользователь ткнул голубя: отдых прерывается, голубь немного вздрагивает.</summary>
public sealed record UserPoked : InputEvent;

/// <summary>
/// Приложение снова запущено после паузы: хост сообщает, сколько прошло по настенным часам с
/// последнего сохранения. Симуляция досчитывает это время с ограничением (ТЗ §6.3).
/// </summary>
public sealed record AbsenceEnded : InputEvent
{
    public AbsenceEnded(TimeSpan duration)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(duration, TimeSpan.Zero);
        Duration = duration;
    }

    public TimeSpan Duration { get; }
}

/// <summary>Вход, применённый в конкретном тике.</summary>
public readonly record struct JournalEntry(long Tick, InputEvent Event);
