namespace Pigeon.Core;

/// <summary>
/// Как текущее занятие меняет потребности (ТЗ §6.1). Положительный множитель — рост со
/// скоростью <see cref="NeedModel"/>, умноженной на него и на влияние трейтов; отрицательный —
/// снижение: усталость в единицах <see cref="NeedModel.FatigueRecoveryPerSecond"/>, скука и
/// внимание — в единицах их скорости роста. Ноль — потребность не меняется.
/// </summary>
/// <remarks>Полёт дороже ходьбы: у намерений с перелётом множитель усталости больше.</remarks>
public readonly record struct NeedEffects(double Fatigue, double Boredom, double Attention)
{
    /// <summary>Обычная активность: все потребности растут с базовой скоростью.</summary>
    public static NeedEffects Active { get; } = new(1.0, 1.0, 1.0);

    /// <summary>Отдых: усталость снимается, остальное растёт как обычно.</summary>
    public static NeedEffects Resting { get; } = new(-1.0, 1.0, 1.0);

    public void Validate()
    {
        if (!double.IsFinite(Fatigue) || !double.IsFinite(Boredom) || !double.IsFinite(Attention))
        {
            throw new ArgumentException($"Множители потребностей должны быть конечными числами: {this}.");
        }
    }
}
