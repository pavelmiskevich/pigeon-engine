namespace Pigeon.Simulation;

/// <param name="TicksToRun">Сколько тиков выполнить сейчас.</param>
/// <param name="TicksDropped">Сколько тиков отброшено из-за <see cref="SimulationOptions.MaxTicksPerAdvance"/>.</param>
/// <param name="Alpha">Доля шага, накопленная сверх выполненных тиков: 0..1, для интерполяции при отрисовке.</param>
public readonly record struct AdvanceResult(int TicksToRun, long TicksDropped, double Alpha);

/// <summary>
/// Переводит реальное время кадров в целое число фиксированных шагов. Вся арифметика — в тиках
/// <see cref="TimeSpan"/>, без плавающей точки, поэтому число шагов зависит только от суммы
/// прошедшего времени, а не от того, как оно нарезано на кадры.
/// </summary>
public sealed class FixedStepScheduler
{
    private readonly long _stepTicks;
    private readonly int _maxTicksPerAdvance;
    private long _accumulatedTicks;

    public FixedStepScheduler(SimulationOptions options)
        : this(options, TimeSpan.Zero)
    {
    }

    /// <param name="accumulated">Накопленное время из снимка: меньше одного шага.</param>
    public FixedStepScheduler(SimulationOptions options, TimeSpan accumulated)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        ArgumentOutOfRangeException.ThrowIfLessThan(accumulated, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(accumulated, options.Step);

        _stepTicks = options.Step.Ticks;
        _maxTicksPerAdvance = options.MaxTicksPerAdvance;
        _accumulatedTicks = accumulated.Ticks;
    }

    /// <summary>Время, накопленное сверх выполненных шагов.</summary>
    public TimeSpan Accumulated => TimeSpan.FromTicks(_accumulatedTicks);

    public AdvanceResult Advance(TimeSpan realDelta)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(realDelta, TimeSpan.Zero);

        _accumulatedTicks += realDelta.Ticks;
        var due = _accumulatedTicks / _stepTicks;
        var toRun = (int)Math.Min(due, _maxTicksPerAdvance);
        var dropped = due - toRun;

        _accumulatedTicks -= due * _stepTicks;
        return new AdvanceResult(toRun, dropped, (double)_accumulatedTicks / _stepTicks);
    }
}
