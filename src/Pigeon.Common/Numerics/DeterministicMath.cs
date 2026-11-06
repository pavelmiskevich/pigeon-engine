namespace Pigeon.Common.Numerics;

/// <summary>
/// Арифметика для детерминированной симуляции (ТЗ §3.3, ADR-0016). Сложение, вычитание,
/// умножение и деление округляются по IEEE 754 одинаково везде, а <c>Math.Exp</c> и
/// <c>Math.Pow</c> берутся из математической библиотеки ОС и могут отличаться в последнем бите
/// между Windows, Linux, macOS и WASM. Поэтому здесь только четыре действия.
/// </summary>
public static class DeterministicMath
{
    /// <summary>
    /// <paramref name="value"/> в целой неотрицательной степени возведением в квадрат: порядок
    /// умножений зависит только от показателя, поэтому результат побитово одинаков на всех
    /// платформах. Нужна для затухания за много шагов сразу, например при досчёте отсутствия.
    /// </summary>
    public static double PowInt(double value, long exponent)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(exponent);

        var result = 1.0;
        var power = value;
        while (exponent > 0)
        {
            if ((exponent & 1) != 0)
            {
                result *= power;
            }

            exponent >>= 1;
            if (exponent > 0)
            {
                power *= power;
            }
        }

        return result;
    }
}
