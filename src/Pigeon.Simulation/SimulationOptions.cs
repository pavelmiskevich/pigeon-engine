namespace Pigeon.Simulation;

/// <param name="Step">Фиксированный шаг симуляции, не зависящий от частоты кадров (ТЗ §3.3).</param>
/// <param name="MaxTicksPerAdvance">
/// Сколько тиков можно догнать за один вызов. Остаток отбрасывается, чтобы долгая пауза
/// (сон компьютера, отладчик) не заморозила хост; досчёт отсутствия — отдельная логика ТЗ §6.3.
/// </param>
/// <param name="DecisionIntervalTicks">Как часто пересматривается намерение.</param>
public sealed record SimulationOptions(TimeSpan Step, int MaxTicksPerAdvance, int DecisionIntervalTicks)
{
    /// <summary>Desktop: 10 Гц (ТЗ §37), решение раз в секунду.</summary>
    public static SimulationOptions Desktop { get; } = new(TimeSpan.FromMilliseconds(100), 50, 10);

    public void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(Step, TimeSpan.Zero, nameof(Step));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaxTicksPerAdvance, nameof(MaxTicksPerAdvance));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(DecisionIntervalTicks, nameof(DecisionIntervalTicks));
    }
}
