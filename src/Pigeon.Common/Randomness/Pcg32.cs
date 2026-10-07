using System.Numerics;

namespace Pigeon.Common.Randomness;

/// <summary>Состояние <see cref="Pcg32"/>: всё, что нужно для снимка и восстановления потока.</summary>
public readonly record struct Pcg32State(ulong State, ulong Increment);

/// <summary>
/// PCG32 (XSH-RR, 64-битное состояние, 32-битный выход) по эталонной реализации pcg-c-basic.
/// Только целочисленная арифметика — результат одинаков на любой ОС, архитектуре и в WASM.
/// </summary>
public sealed class Pcg32 : IRandomSource
{
    private const ulong Multiplier = 6364136223846793005UL;
    private const double TwoPow26 = 67108864.0;
    private const double TwoPow53 = 9007199254740992.0;

    private ulong _state;
    private readonly ulong _increment;

    /// <param name="seed">Начальное состояние.</param>
    /// <param name="sequence">Номер последовательности: разные значения дают независимые потоки.</param>
    public Pcg32(ulong seed, ulong sequence)
    {
        _increment = (sequence << 1) | 1UL;
        _state = 0;
        Step();
        _state = unchecked(_state + seed);
        Step();
    }

    private Pcg32(Pcg32State state)
    {
        if ((state.Increment & 1UL) == 0)
        {
            throw new ArgumentException("Приращение PCG32 должно быть нечётным.", nameof(state));
        }

        _state = state.State;
        _increment = state.Increment;
    }

    public Pcg32State State => new(_state, _increment);

    public static Pcg32 Restore(Pcg32State state) => new(state);

    public uint NextUInt32()
    {
        var old = _state;
        Step();
        var xorShifted = (uint)(((old >> 18) ^ old) >> 27);
        var rotation = (int)(old >> 59);
        return BitOperations.RotateRight(xorShifted, rotation);
    }

    public int NextInt32(int maxExclusive)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxExclusive);

        // Метод Лемира: умножение вместо деления по модулю, с отбрасыванием смещённого хвоста.
        var range = (uint)maxExclusive;
        var product = (ulong)NextUInt32() * range;
        var low = (uint)product;
        if (low < range)
        {
            var threshold = unchecked(0u - range) % range;
            while (low < threshold)
            {
                product = (ulong)NextUInt32() * range;
                low = (uint)product;
            }
        }

        return (int)(product >> 32);
    }

    public double NextDouble()
    {
        // 27 + 26 бит, как genrand_res53: все операции точные, результат не зависит от платформы.
        var high = NextUInt32() >> 5;
        var low = NextUInt32() >> 6;
        return ((high * TwoPow26) + low) / TwoPow53;
    }

    private void Step() => _state = unchecked((_state * Multiplier) + _increment);
}
