using Pigeon.Common.Time;

namespace Pigeon.Simulation;

/// <summary>Связывает часы хоста с симуляцией: каждый кадр передаёт ей прошедшее время.</summary>
public sealed class SimulationDriver
{
    private readonly IClock _clock;
    private readonly PigeonSimulation _simulation;
    private TimeSpan _lastElapsed;

    public SimulationDriver(IClock clock, PigeonSimulation simulation)
    {
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(simulation);

        _clock = clock;
        _simulation = simulation;
        _lastElapsed = clock.Elapsed;
    }

    /// <summary>Вызывается хостом раз в кадр.</summary>
    public AdvanceResult Pump()
    {
        var now = _clock.Elapsed;
        var delta = now - _lastElapsed;
        _lastElapsed = now;
        return _simulation.Advance(delta);
    }
}
