namespace Pigeon.Desktop;

/// <summary>Почему тихо; ручная тишина важнее: она держится до срока, даже когда полный экран кончился.</summary>
internal enum QuietReason
{
    None,
    Manual,
    Fullscreen,
}

/// <summary>
/// Режим тишины в хосте (#31, ТЗ §11.7): ручной до срока по монотонным часам и автоматический,
/// пока Windows сообщает о полноэкранном приложении. Тихо, если действует хотя бы один. Хост
/// подаёт в симуляцию <c>QuietChanged</c> только когда состояние сменилось — методы возвращают
/// <c>true</c> ровно тогда.
/// </summary>
internal sealed class QuietMode
{
    /// <summary>До какого часа длится «Тихо до утра».</summary>
    private const int MorningHour = 6;

    private TimeSpan _manualUntil = TimeSpan.MinValue;
    private bool _fullscreen;

    public bool IsQuiet => Reason != QuietReason.None;

    public QuietReason Reason { get; private set; }

    /// <summary>Тихо на <paramref name="duration"/> с момента <paramref name="now"/> — последний выбор человека заменяет прежний.</summary>
    public bool QuietFor(TimeSpan duration, TimeSpan now)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(duration, TimeSpan.Zero);

        _manualUntil = now + duration;
        return Settle(now);
    }

    /// <summary>Очередной опрос: идёт ли полноэкранное приложение и не истёк ли срок ручной тишины.</summary>
    public bool Update(TimeSpan now, bool fullscreen)
    {
        _fullscreen = fullscreen;
        return Settle(now);
    }

    /// <summary>
    /// Сколько осталось до ближайших 06:00 по местному времени — «Тихо до утра». Ровно в 06:00 —
    /// до следующего утра.
    /// </summary>
    public static TimeSpan UntilMorning(DateTime localNow)
    {
        var morning = localNow.Date.AddHours(MorningHour);
        if (morning <= localNow)
        {
            morning = morning.AddDays(1);
        }

        return morning - localNow;
    }

    private bool Settle(TimeSpan now)
    {
        var reason = now < _manualUntil ? QuietReason.Manual
            : _fullscreen ? QuietReason.Fullscreen
            : QuietReason.None;
        var changed = (reason != QuietReason.None) != IsQuiet;
        Reason = reason;
        return changed;
    }
}
