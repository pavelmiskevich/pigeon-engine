namespace Pigeon.Common.Randomness;

/// <summary>
/// Детерминированный источник случайности. Ядро получает случайность только через него
/// (ТЗ §3.3): <c>System.Random</c> в ядре запрещён, его алгоритм не гарантирован между версиями .NET.
/// </summary>
public interface IRandomSource
{
    uint NextUInt32();

    /// <summary>Равномерно в диапазоне [0, <paramref name="maxExclusive"/>), без смещения.</summary>
    int NextInt32(int maxExclusive);

    /// <summary>Равномерно в [0, 1) с 53 значащими битами.</summary>
    double NextDouble();
}
