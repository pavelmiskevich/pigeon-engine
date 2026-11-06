using Pigeon.AI.Behavior;
using Pigeon.Common.Randomness;
using Pigeon.Core;
using Pigeon.World.Abstractions;

namespace Pigeon.AI;

/// <summary>Почему решение пересматривается (ТЗ §9.2, «когда пересчитывать»).</summary>
public enum DecisionTrigger
{
    /// <summary>Плановый пересмотр: текущее намерение можно оставить, если оно всё ещё среди лучших.</summary>
    Periodic = 0,

    /// <summary>Намерение завершилось: выбирается новое, без надбавки текущему.</summary>
    Completed = 1,

    /// <summary>Срочная потребность или значимое событие: пересмотр раньше минимальной длительности.</summary>
    Interrupted = 2,

    /// <summary>Цель намерения пропала из мира.</summary>
    TargetGone = 3,

    /// <summary>Мир не смог исполнить намерение: голубь вздрагивает и выбирает другое.</summary>
    Failed = 4,
}

/// <summary>Один фактор оценки — для объяснения решения (ТЗ §9.2, §36).</summary>
/// <param name="Key">Что это: вход consideration (<c>need.boredom</c>), <c>personality</c>, <c>mood</c>, <c>momentum</c>.</param>
/// <param name="Value">Значение множителя.</param>
public readonly record struct DecisionFactor(string Key, double Value);

/// <summary>Оценённый вариант: намерение, его цель и из чего сложилась оценка.</summary>
public sealed record ScoredOption(IntentKind Intent, AffordanceId? Target, double Score, IReadOnlyList<DecisionFactor> Factors)
{
    /// <summary>
    /// 2–3 ведущих фактора (ТЗ §9.2): самые высокие considerations, кроме постоянных, затем
    /// личность и настроение, если они заметно подняли оценку.
    /// </summary>
    public IReadOnlyList<DecisionFactor> LeadingFactors(int count = 3) =>
        [.. Factors
            .Where(f => f.Key is not (UtilityBrain.PersonalityFactor or UtilityBrain.MoodFactor or UtilityBrain.MomentumFactor or UtilityBrain.ConstantFactor))
            .OrderByDescending(f => f.Value)
            .Concat(Factors.Where(f => f.Key is UtilityBrain.PersonalityFactor or UtilityBrain.MoodFactor && f.Value >= 1.1)
                .OrderByDescending(f => f.Value))
            .Take(count)];
}

/// <summary>Решение: выбранный вариант, оставлено ли текущее намерение и из чего выбирали.</summary>
public sealed record Decision(ScoredOption Choice, bool KeptCurrent, DecisionTrigger Trigger, IReadOnlyList<ScoredOption> Pool);

/// <summary>
/// Utility AI режима Autonomous (ТЗ §9.2, ADR-0015): оценивает намерения из каталога вместе с
/// доступными целями и выбирает одно. Работает и без мира — тогда остаются намерения без цели.
/// </summary>
/// <remarks>
/// Оценка — произведение considerations с поправкой на их число (иначе намерение с четырьмя
/// множителями всегда проигрывало бы намерению с одним), умноженное на вес трейтов, модификатор
/// настроения и надбавку текущему намерению. Выбор — случайный, пропорционально оценке, из
/// лучших <see cref="BehaviorConfig.TopK"/> в пределах <see cref="BehaviorConfig.Delta"/> от
/// лучшего. При плановом пересмотре текущее намерение остаётся, пока оно в этом наборе: так
/// голубь не дёргается между близкими вариантами.
/// </remarks>
public sealed class UtilityBrain
{
    public const string PersonalityFactor = "personality";
    public const string MoodFactor = "mood";
    public const string MomentumFactor = "momentum";
    public const string ConstantFactor = "constant";

    private const double BoldnessRiskShift = 0.5;
    private const double MinMoodModifier = 0.05;

    private readonly BehaviorConfig _config;

    public UtilityBrain(BehaviorConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        var problems = config.Problems();
        if (problems.Count > 0)
        {
            throw new BehaviorConfigException(problems);
        }

        _config = config;
    }

    public BehaviorConfig Config => _config;

    /// <summary>Все допустимые варианты с оценками, в стабильном порядке: по намерению, затем по цели.</summary>
    /// <param name="affordances">Текущие affordances мира, упорядоченные по идентификатору.</param>
    public IReadOnlyList<ScoredOption> Score(
        PigeonState pigeon,
        IReadOnlyList<Affordance> affordances,
        IntentCooldowns cooldowns,
        long tick,
        bool withMomentum)
    {
        ArgumentNullException.ThrowIfNull(pigeon);
        ArgumentNullException.ThrowIfNull(affordances);
        ArgumentNullException.ThrowIfNull(cooldowns);

        var options = new List<ScoredOption>();
        foreach (var intent in _config.Intents)
        {
            if (cooldowns.IsCoolingDown(intent.Kind, tick))
            {
                continue;
            }

            if (!intent.HasTarget)
            {
                options.Add(Evaluate(intent, pigeon, null, withMomentum));
                continue;
            }

            foreach (var affordance in affordances)
            {
                if (intent.Targets.Contains(affordance.Kind))
                {
                    options.Add(Evaluate(intent, pigeon, affordance, withMomentum));
                }
            }
        }

        return options;
    }

    /// <summary>
    /// Выбор намерения. Случайность берётся из <paramref name="random"/>, только когда выбирать
    /// действительно приходится, поэтому неизменное решение не сдвигает поток.
    /// </summary>
    public Decision Decide(
        PigeonState pigeon,
        IReadOnlyList<Affordance> affordances,
        IntentCooldowns cooldowns,
        long tick,
        DecisionTrigger trigger,
        IRandomSource random)
    {
        ArgumentNullException.ThrowIfNull(random);

        var keepable = trigger is DecisionTrigger.Periodic or DecisionTrigger.Interrupted;
        var options = Score(pigeon, affordances, cooldowns, tick, withMomentum: keepable);
        var pool = Pool(options);

        if (pool.Count == 0)
        {
            var idle = new ScoredOption(IntentKind.Wander, null, 0.0, []);
            return new Decision(idle, KeptCurrent: pigeon.Intent == IntentKind.Wander && pigeon.IntentTarget is null, trigger, []);
        }

        if (keepable)
        {
            var current = pool.FirstOrDefault(o => o.Intent == pigeon.Intent && o.Target == pigeon.IntentTarget);
            if (current is not null)
            {
                return new Decision(current, KeptCurrent: true, trigger, pool);
            }
        }

        var total = pool.Sum(o => o.Score);
        var roll = random.NextDouble() * total;
        var chosen = pool[^1];
        foreach (var option in pool)
        {
            roll -= option.Score;
            if (roll < 0.0)
            {
                chosen = option;
                break;
            }
        }

        // После завершения, неудачи или пропажи цели то же намерение — новая попытка, а не продолжение.
        var kept = keepable && chosen.Intent == pigeon.Intent && chosen.Target == pigeon.IntentTarget;
        return new Decision(chosen, kept, trigger, pool);
    }

    /// <summary>Длительность нового намерения: равномерно между минимальной и максимальной.</summary>
    public TimeSpan PlanDuration(IntentKind intent, IRandomSource random)
    {
        ArgumentNullException.ThrowIfNull(random);

        var definition = Definition(intent);
        var span = definition.MaxDuration.Ticks - definition.MinDuration.Ticks;
        return TimeSpan.FromTicks(definition.MinDuration.Ticks + (long)(span * random.NextDouble()));
    }

    /// <summary>Как намерение меняет потребности; намерения вне каталога считаются обычной активностью.</summary>
    public NeedEffects EffectsOf(IntentKind intent) => _config.Find(intent)?.Needs ?? NeedEffects.Active;

    public TimeSpan MinDurationOf(IntentKind intent) => _config.Find(intent)?.MinDuration ?? TimeSpan.Zero;

    public TimeSpan CooldownOf(IntentKind intent) => _config.Find(intent)?.Cooldown ?? TimeSpan.Zero;

    private IntentDefinition Definition(IntentKind intent) =>
        _config.Find(intent) ?? throw new ArgumentOutOfRangeException(nameof(intent), intent, "Намерения нет в каталоге.");

    private List<ScoredOption> Pool(IReadOnlyList<ScoredOption> options)
    {
        var positive = options.Where(o => o.Score > 0.0).ToList();
        if (positive.Count == 0)
        {
            return [];
        }

        var best = positive.Max(o => o.Score);
        var threshold = best * (1.0 - _config.Delta);

        // Сортировка устойчивая: при равных оценках порядок — порядок каталога и целей.
        return [.. positive
            .Where(o => o.Score >= threshold)
            .OrderByDescending(o => o.Score)
            .Take(_config.TopK)];
    }

    private ScoredOption Evaluate(IntentDefinition intent, PigeonState pigeon, Affordance? target, bool withMomentum)
    {
        var factors = new List<DecisionFactor>(intent.Considerations.Count + 3);
        var count = intent.Considerations.Count;
        var compensation = 1.0 - 1.0 / count;
        var score = 1.0;

        foreach (var consideration in intent.Considerations)
        {
            var value = consideration.Curve.Evaluate(Input(consideration.Input, pigeon, target));
            factors.Add(new DecisionFactor(Key(consideration), value));

            // Поправка на число множителей (Dave Mark): каждый тянется к 1 пропорционально недобору.
            score *= value + (1.0 - value) * compensation * value;
        }

        var personality = 1.0;
        foreach (var (trait, weight) in intent.TraitWeights)
        {
            personality *= 1.0 + weight * Personality.Centered(pigeon.Personality[trait]);
        }

        personality = Math.Max(0.0, personality);
        factors.Add(new DecisionFactor(PersonalityFactor, personality));

        var mood = Math.Max(
            MinMoodModifier,
            1.0 + intent.ArousalWeight * (2.0 * pigeon.Mood.Arousal - 1.0) + intent.ValenceWeight * pigeon.Mood.Valence);
        factors.Add(new DecisionFactor(MoodFactor, mood));

        score *= personality * mood;

        if (withMomentum && pigeon.Intent == intent.Kind && pigeon.IntentTarget == target?.Id)
        {
            score *= 1.0 + _config.Momentum;
            factors.Add(new DecisionFactor(MomentumFactor, 1.0 + _config.Momentum));
        }

        return new ScoredOption(intent.Kind, target?.Id, score, factors);
    }

    private double Input(ConsiderationInput input, PigeonState pigeon, Affordance? target) => input switch
    {
        ConsiderationInput.None => 0.0,
        ConsiderationInput.FatigueNeed => pigeon.Needs.Fatigue,
        ConsiderationInput.BoredomNeed => pigeon.Needs.Boredom,
        ConsiderationInput.AttentionNeed => pigeon.Needs.Attention,
        ConsiderationInput.Valence => pigeon.Mood.Valence,
        ConsiderationInput.Arousal => pigeon.Mood.Arousal,
        ConsiderationInput.Stress => pigeon.Mood.Stress,
        ConsiderationInput.TargetAttractiveness => Target(target).Attractiveness,
        ConsiderationInput.TargetRisk => Math.Clamp(
            Target(target).Risk * (1.0 - BoldnessRiskShift * Personality.Centered(pigeon.Personality.Boldness)), 0.0, 1.0),
        ConsiderationInput.TargetTravel => Math.Min(1.0, Target(target).TravelCost.Ticks / (double)_config.TravelCostScale.Ticks),
        _ => throw new InvalidOperationException($"Неизвестный вход consideration: {input}."),
    };

    private static Affordance Target(Affordance? target) =>
        target ?? throw new InvalidOperationException("Consideration цели у намерения без цели.");

    private static string Key(Consideration consideration) =>
        consideration.Input == ConsiderationInput.None ? ConstantFactor : BehaviorConfigJson.InputName(consideration.Input);
}
