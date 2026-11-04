namespace Pigeon.Prototypes.Overlay;

/// <summary>
/// Расписание кадров 60 Гц поверх синхронизации с экраном любой частоты: кадр рисуется на первой
/// синхронизации не раньше дедлайна минус допуск, в среднем выходит 60 кадров в секунду.
/// </summary>
public static class FramePacer
{
    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(1.0 / 60);

    /// <summary>Половина кадра экрана 144 Гц.</summary>
    public static readonly TimeSpan Tolerance = TimeSpan.FromMilliseconds(3.5);

    /// <summary>Следующий дедлайн; если отстали больше чем на кадр — отсчёт от текущего момента.</summary>
    public static TimeSpan Next(TimeSpan previous, TimeSpan now)
    {
        var next = previous + Interval;
        return next < now - Interval ? now + Interval : next;
    }
}
