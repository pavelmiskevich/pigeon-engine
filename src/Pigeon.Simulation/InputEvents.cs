using Pigeon.Core;
using Pigeon.World.Abstractions;

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

/// <summary>
/// Что мир предлагает голубю (ТЗ §32): ответ <see cref="Pigeon.World.Abstractions.IAffordanceSource"/>,
/// записанный в журнал. Список упорядочивается по идентификатору, повторы идентификаторов запрещены.
/// </summary>
public sealed record AffordancesObserved : InputEvent
{
    public AffordancesObserved(IEnumerable<Affordance> affordances)
    {
        ArgumentNullException.ThrowIfNull(affordances);

        var sorted = affordances.OrderBy(a => a.Id.Value).ToArray();
        for (var i = 0; i < sorted.Length; i++)
        {
            ArgumentNullException.ThrowIfNull(sorted[i], nameof(affordances));
            sorted[i].Validate();
            if (i > 0 && sorted[i].Id == sorted[i - 1].Id)
            {
                throw new ArgumentException($"Affordance {sorted[i].Id} встречается дважды.", nameof(affordances));
            }
        }

        Affordances = sorted;
    }

    public IReadOnlyList<Affordance> Affordances { get; }

    public bool Equals(AffordancesObserved? other) =>
        other is not null && Affordances.SequenceEqual(other.Affordances);

    public override int GetHashCode() => Affordances.Count;
}

/// <summary>
/// Голубь лишился опоры не по своей воле (ТЗ §11.4, §12): отключили его монитор, пропала
/// поверхность или возможность платформы. Голубь пугается — робкий сильнее — и сразу решает,
/// что делать дальше.
/// </summary>
public sealed record FootingLost(DisplacementReason Reason) : InputEvent;

/// <summary>
/// Мир закончил исполнять намерение (ТЗ §9.1): дошёл, долетел, исследовал — или не смог.
/// Относится к намерению, начатому в тике <see cref="IntentStartedTick"/>: если голубь к этому
/// времени уже занят другим, ответ устарел и не учитывается.
/// </summary>
public sealed record ActionFinished(IntentKind Intent, long IntentStartedTick, ActionOutcome Outcome, ActionFailure? Failure = null)
    : InputEvent;

/// <summary>Вход, применённый в конкретном тике.</summary>
public readonly record struct JournalEntry(long Tick, InputEvent Event);
