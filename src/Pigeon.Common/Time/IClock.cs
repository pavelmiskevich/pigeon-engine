namespace Pigeon.Common.Time;

/// <summary>
/// Монотонные часы хоста. Ядро получает время только через эту абстракцию (ТЗ §3.3):
/// <c>DateTime.Now</c> и <c>Stopwatch</c> в ядре запрещены.
/// </summary>
public interface IClock
{
    /// <summary>Время от произвольной точки отсчёта. Никогда не уменьшается.</summary>
    TimeSpan Elapsed { get; }
}
