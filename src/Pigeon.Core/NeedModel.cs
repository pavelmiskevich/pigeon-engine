namespace Pigeon.Core;

/// <summary>
/// Правила потребностей (ТЗ §6): пороги, скорость роста и снижения, влияние трейтов и досчёт
/// времени отсутствия. Скорости — в долях шкалы за секунду симуляции.
/// </summary>
/// <remarks>
/// Значения по умолчанию подобраны под Desktop: активный голубь устаёт до <c>urgent</c> примерно
/// за 28 минут и отдыхает несколько минут, скука доходит до <c>urgent</c> за 14 минут, а
/// потребность во внимании — за 42 минуты без взаимодействия. Средний трейт (50) скорость не
/// меняет, крайние значения меняют её в полтора раза в ту или другую сторону.
/// </remarks>
public sealed record NeedModel
{
    public static NeedModel Default { get; } = new();

    /// <summary>Ниже — <see cref="NeedLevel.Comfort"/>.</summary>
    public double ComfortBelow { get; init; } = 0.3;

    /// <summary>Выше — <see cref="NeedLevel.Urgent"/>; между порогами — <see cref="NeedLevel.Want"/>.</summary>
    public double UrgentAbove { get; init; } = 0.7;

    /// <summary>Рост усталости в активности. Energy замедляет.</summary>
    public double FatigueGrowthPerSecond { get; init; } = 1.0 / 2_400;

    /// <summary>Снижение усталости в отдыхе и во сне, пока приложение закрыто.</summary>
    public double FatigueRecoveryPerSecond { get; init; } = 1.0 / 360;

    /// <summary>Рост скуки без новых стимулов. Curiosity ускоряет.</summary>
    public double BoredomGrowthPerSecond { get; init; } = 1.0 / 1_200;

    /// <summary>Рост потребности во внимании без взаимодействия. Sociability ускоряет, Independence замедляет.</summary>
    public double AttentionGrowthPerSecond { get; init; } = 1.0 / 3_600;

    /// <summary>Больше этого времени отсутствия не досчитывается (ТЗ §6.3).</summary>
    public TimeSpan AbsenceCatchUpLimit { get; init; } = TimeSpan.FromHours(8);

    public NeedLevel LevelOf(double urgency) =>
        urgency > UrgentAbove ? NeedLevel.Urgent
        : urgency < ComfortBelow ? NeedLevel.Comfort
        : NeedLevel.Want;

    /// <summary>
    /// Насколько потребность вышла за <see cref="ComfortBelow"/>: 0 в комфорте, 1 при полной
    /// срочности. Этим давлением потребности тянут настроение (ТЗ §7).
    /// </summary>
    public double PressureOf(double urgency) => Math.Max(0.0, urgency - ComfortBelow) / (1.0 - ComfortBelow);

    /// <summary>
    /// Потребности через <paramref name="seconds"/> секунд занятия с эффектами
    /// <paramref name="effects"/> (см. <see cref="NeedEffects"/>).
    /// </summary>
    public Needs Advance(Needs needs, Personality personality, NeedEffects effects, double seconds)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(seconds);

        return needs
            .Change(NeedKind.Fatigue, Delta(effects.Fatigue, FatigueGrowthPerSecond * FatigueFactor(personality), FatigueRecoveryPerSecond) * seconds)
            .Change(NeedKind.Boredom, Delta(effects.Boredom, BoredomGrowthPerSecond * BoredomFactor(personality), BoredomGrowthPerSecond) * seconds)
            .Change(NeedKind.Attention, Delta(effects.Attention, AttentionGrowthPerSecond * AttentionFactor(personality), AttentionGrowthPerSecond) * seconds);
    }

    /// <summary>
    /// Досчёт времени, пока приложение было закрыто (ТЗ §6.3): голубь спал, поэтому усталость
    /// снижается, а остальные потребности растут, но не выше верхней границы <c>want</c>.
    /// Потребность, которая уже была выше этой границы, досчёт не меняет. Учитывается не больше
    /// <see cref="AbsenceCatchUpLimit"/>.
    /// </summary>
    public Needs CatchUpAbsence(Needs needs, Personality personality, TimeSpan away)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(away, TimeSpan.Zero);

        var seconds = AbsenceSeconds(away);
        return needs
            .Change(NeedKind.Fatigue, -FatigueRecoveryPerSecond * seconds)
            .With(NeedKind.Boredom, Capped(needs.Boredom, BoredomGrowthPerSecond * BoredomFactor(personality) * seconds))
            .With(NeedKind.Attention, Capped(needs.Attention, AttentionGrowthPerSecond * AttentionFactor(personality) * seconds));
    }

    /// <summary>Сколько секунд отсутствия досчитывается: целые секунды, не больше лимита.</summary>
    public long AbsenceSeconds(TimeSpan away)
    {
        var counted = away < AbsenceCatchUpLimit ? away : AbsenceCatchUpLimit;
        return counted.Ticks / TimeSpan.TicksPerSecond;
    }

    private static double Delta(double multiplier, double growth, double relief) =>
        multiplier >= 0.0 ? multiplier * growth : multiplier * relief;

    private double Capped(double urgency, double growth) =>
        urgency >= UrgentAbove ? urgency : Math.Min(urgency + growth, UrgentAbove);

    private static double FatigueFactor(Personality p) => 1.0 - 0.5 * Personality.Centered(p.Energy);

    private static double BoredomFactor(Personality p) => 1.0 + 0.5 * Personality.Centered(p.Curiosity);

    private static double AttentionFactor(Personality p) =>
        (1.0 + 0.5 * Personality.Centered(p.Sociability)) * (1.0 - 0.5 * Personality.Centered(p.Independence));
}
