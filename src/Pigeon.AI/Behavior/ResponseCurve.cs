namespace Pigeon.AI.Behavior;

/// <summary>Форма кривой отклика (ТЗ §9.2).</summary>
public enum CurveKind
{
    /// <summary>Не зависит от входа: всегда <see cref="ResponseCurve.Value"/>.</summary>
    Constant = 0,

    /// <summary>Прямая от <see cref="ResponseCurve.From"/> до <see cref="ResponseCurve.To"/>.</summary>
    Linear = 1,

    /// <summary>Плавная ступень 3t² − 2t³: медленно у краёв, быстро в середине.</summary>
    Smoothstep = 2,

    /// <summary>tⁿ с целым n: слабо в начале, круто к концу.</summary>
    Power = 3,
}

/// <summary>
/// Кривая отклика consideration: вход отображается в t = (x − From) / (To − From), обрезается до
/// 0..1, проходит через форму и растягивается в Min..Max. Если From больше To, кривая убывает.
/// Только четыре действия арифметики — результат одинаков на всех платформах (ADR-0016).
/// </summary>
public sealed record ResponseCurve(
    CurveKind Kind,
    double From = 0.0,
    double To = 1.0,
    double Min = 0.0,
    double Max = 1.0,
    int Exponent = 2,
    double Value = 1.0)
{
    public static ResponseCurve Constant(double value) => new(CurveKind.Constant, Value: value);

    public double Evaluate(double x)
    {
        if (Kind == CurveKind.Constant)
        {
            return Value;
        }

        var t = Math.Clamp((x - From) / (To - From), 0.0, 1.0);
        var shaped = Kind switch
        {
            CurveKind.Linear => t,
            CurveKind.Smoothstep => t * t * (3.0 - 2.0 * t),
            CurveKind.Power => Common.Numerics.DeterministicMath.PowInt(t, Exponent),
            _ => throw new InvalidOperationException($"Неизвестная форма кривой: {Kind}."),
        };

        return Min + (Max - Min) * shaped;
    }

    /// <summary>Ошибки кривой для сообщения о конфигурации; пусто — кривая корректна.</summary>
    public IEnumerable<string> Problems()
    {
        if (!Enum.IsDefined(Kind))
        {
            yield return $"неизвестная форма {(int)Kind}";
            yield break;
        }

        if (Kind == CurveKind.Constant)
        {
            if (Value is < 0.0 or > 1.0 || double.IsNaN(Value))
            {
                yield return $"значение {Value} вне 0..1";
            }

            yield break;
        }

        if (!double.IsFinite(From) || !double.IsFinite(To) || From == To)
        {
            yield return $"from и to должны быть разными конечными числами (from = {From}, to = {To})";
        }

        if (Min is < 0.0 or > 1.0 || Max is < 0.0 or > 1.0 || double.IsNaN(Min) || double.IsNaN(Max))
        {
            yield return $"min и max должны быть в 0..1 (min = {Min}, max = {Max})";
        }

        if (Kind == CurveKind.Power && Exponent is < 1 or > 8)
        {
            yield return $"показатель {Exponent} вне 1..8";
        }
    }
}
