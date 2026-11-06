using Pigeon.Common.Numerics;

namespace Pigeon.Core;

/// <summary>
/// Правила настроения (ТЗ §7): базовая точка от личности, тяга срочных потребностей, затухание
/// к цели и дискретные метки.
/// </summary>
/// <remarks>
/// Затухание за шаг — линейное приближение экспоненты, <c>v += (цель − v) · Δt / τ</c>: только
/// четыре действия арифметики, поэтому оно детерминировано на всех платформах (ADR-0016). За
/// долгое отсутствие то же затухание считается сразу через <see cref="DeterministicMath.PowInt"/>.
/// Отношения (ТЗ §21) базовую точку пока не сдвигают — их ещё нет в домене.
/// </remarks>
public sealed record MoodModel
{
    public static MoodModel Default { get; } = new();

    /// <summary>Постоянная времени затухания τ у голубя со средним Calmness.</summary>
    public TimeSpan DecayTime { get; init; } = TimeSpan.FromMinutes(10);

    /// <summary>Окно скользящего среднего для <see cref="Mood.Stress"/>.</summary>
    public TimeSpan StressWindow { get; init; } = TimeSpan.FromMinutes(5);

    /// <summary>Насколько самая острая потребность опускает valence при полной срочности.</summary>
    public double NeedValencePull { get; init; } = 0.5;

    /// <summary>
    /// Насколько потребности сдвигают arousal: скука и нехватка внимания поднимают его, усталость
    /// опускает — уставший голубь вялый, а не взвинченный.
    /// </summary>
    public double NeedArousalPull { get; init; } = 0.3;

    /// <summary>С какого отсутствия возвращение пользователя — событие для настроения (ТЗ §6.3).</summary>
    public TimeSpan LongAbsence { get; init; } = TimeSpan.FromHours(12);

    // Границы меток (ТЗ §7) на плоскости valence × arousal.
    internal const double HighArousal = 0.8;
    internal const double LivelyArousal = 0.55;
    internal const double SleepyArousalBelow = 0.45;
    internal const double NegativeValence = -0.25;
    internal const double PositiveValence = 0.25;
    internal const double StartledValenceBelow = -0.2;

    /// <summary>
    /// Базовая точка (ТЗ §7): Playfulness и Sociability сдвигают valence (−0.1..0.3 вокруг 0.1),
    /// Calmness снижает arousal (0.25..0.55 вокруг 0.4). Стресса в покое нет.
    /// </summary>
    public Mood Baseline(Personality personality) =>
        new(
            0.1 + 0.1 * Personality.Centered(personality.Playfulness) + 0.1 * Personality.Centered(personality.Sociability),
            0.4 - 0.15 * Personality.Centered(personality.Calmness),
            0.0);

    /// <summary>Постоянная времени затухания в секундах: спокойный голубь успокаивается до 1,5 раза быстрее.</summary>
    public double DecaySeconds(Personality personality) =>
        DecayTime.Ticks / (double)TimeSpan.TicksPerSecond / (1.0 + 0.5 * Personality.Centered(personality.Calmness));

    /// <summary>Куда тянется настроение при текущих потребностях.</summary>
    public Mood Target(Personality personality, Needs needs, NeedModel needModel)
    {
        ArgumentNullException.ThrowIfNull(needModel);

        var fatigue = needModel.PressureOf(needs.Fatigue);
        var restless = Math.Max(needModel.PressureOf(needs.Boredom), needModel.PressureOf(needs.Attention));
        var worst = Math.Max(fatigue, restless);

        return Baseline(personality).Nudge(-NeedValencePull * worst, NeedArousalPull * (restless - fatigue));
    }

    /// <summary>Настроение через <paramref name="seconds"/> секунд: шаг затухания к цели и обновление стресса.</summary>
    public Mood Advance(Mood mood, Personality personality, Needs needs, NeedModel needModel, double seconds)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(seconds);

        var target = Target(personality, needs, needModel);
        var k = Math.Min(1.0, seconds / DecaySeconds(personality));
        var valence = mood.Valence + (target.Valence - mood.Valence) * k;
        var arousal = mood.Arousal + (target.Arousal - mood.Arousal) * k;

        var sample = Math.Max(0.0, -valence) * arousal;
        var stressK = Math.Min(1.0, seconds / StressWindowSeconds);
        var stress = mood.Stress + (sample - mood.Stress) * stressK;

        return new Mood(Math.Clamp(valence, -1.0, 1.0), Math.Clamp(arousal, 0.0, 1.0), Math.Clamp(stress, 0.0, 1.0));
    }

    /// <summary>
    /// Отсутствие длиной <paramref name="seconds"/> секунд: голубь спал, настроение затухает к
    /// базовой точке, стресс — к нулю.
    /// </summary>
    public Mood CatchUpAbsence(Mood mood, Personality personality, long seconds)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(seconds);

        var baseline = Baseline(personality);
        var keep = DeterministicMath.PowInt(1.0 - 1.0 / Math.Max(1.0, DecaySeconds(personality)), seconds);
        var keepStress = DeterministicMath.PowInt(1.0 - 1.0 / Math.Max(1.0, StressWindowSeconds), seconds);

        return new Mood(
            baseline.Valence + (mood.Valence - baseline.Valence) * keep,
            baseline.Arousal + (mood.Arousal - baseline.Arousal) * keep,
            mood.Stress * keepStress);
    }

    /// <summary>
    /// Встреча после долгого отсутствия (ТЗ §6.3): голубь оживляется, а радуется или обижается —
    /// по личности: общительный радуется, независимый дуется. Наказанием это не является.
    /// </summary>
    public Mood WelcomeBack(Mood mood, Personality personality, TimeSpan away)
    {
        if (away < LongAbsence)
        {
            return mood;
        }

        var warmth = (Personality.Centered(personality.Sociability) - Personality.Centered(personality.Independence)) / 2.0;
        return mood.Nudge(0.3 * warmth, 0.2);
    }

    /// <summary>
    /// Метка настроения (ТЗ §7). Сначала сильное возбуждение (испуг или восторг), затем доминирующая
    /// потребность (сонливость, если голубь вялый; скука), затем квадранты valence × arousal.
    /// </summary>
    public MoodLabel Classify(Mood mood, Needs needs, NeedModel needModel)
    {
        ArgumentNullException.ThrowIfNull(needModel);

        if (mood.Arousal >= HighArousal)
        {
            return mood.Valence < StartledValenceBelow ? MoodLabel.Startled : MoodLabel.Excited;
        }

        var dominant = needs.Dominant;
        if (needModel.LevelOf(needs[dominant]) != NeedLevel.Comfort)
        {
            if (dominant == NeedKind.Fatigue && mood.Arousal < SleepyArousalBelow)
            {
                return MoodLabel.Sleepy;
            }

            if (dominant == NeedKind.Boredom)
            {
                return MoodLabel.Bored;
            }
        }

        if (mood.Valence <= NegativeValence)
        {
            return mood.Arousal >= LivelyArousal ? MoodLabel.Anxious : MoodLabel.Grumpy;
        }

        if (mood.Valence >= PositiveValence)
        {
            return mood.Arousal >= LivelyArousal ? MoodLabel.Playful : MoodLabel.Happy;
        }

        return MoodLabel.Content;
    }

    private double StressWindowSeconds => StressWindow.Ticks / (double)TimeSpan.TicksPerSecond;
}
