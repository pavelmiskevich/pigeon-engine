namespace Pigeon.Common.Time;

/// <summary>Часы, которые двигает вызывающий код: тесты, воспроизведение, ускорение времени.</summary>
public sealed class ManualClock : IClock
{
    public TimeSpan Elapsed { get; private set; }

    public void Advance(TimeSpan delta)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(delta, TimeSpan.Zero);
        Elapsed += delta;
    }
}
