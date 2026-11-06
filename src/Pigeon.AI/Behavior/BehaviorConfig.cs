using Pigeon.Core;
using Pigeon.World.Abstractions;

namespace Pigeon.AI.Behavior;

/// <summary>Что измеряет consideration (ТЗ §9.2). Все входы приведены к удобному диапазону.</summary>
public enum ConsiderationInput
{
    /// <summary>Входа нет — для кривой <see cref="CurveKind.Constant"/>.</summary>
    None = 0,
    FatigueNeed = 1,
    BoredomNeed = 2,
    AttentionNeed = 3,

    /// <summary>Valence −1..1.</summary>
    Valence = 4,
    Arousal = 5,
    Stress = 6,
    TargetAttractiveness = 7,

    /// <summary>Риск цели, каким его видит голубь: смелый преуменьшает его, робкий — преувеличивает.</summary>
    TargetRisk = 8,

    /// <summary>Время пути к цели, делённое на <see cref="BehaviorConfig.TravelCostScale"/>, не больше 1.</summary>
    TargetTravel = 9,
}

/// <summary>Один множитель оценки: вход через кривую отклика.</summary>
public sealed record Consideration(ConsiderationInput Input, ResponseCurve Curve)
{
    /// <summary>Входы, которые требуют цели; у намерения без цели такие considerations запрещены.</summary>
    public bool NeedsTarget => Input is ConsiderationInput.TargetAttractiveness or ConsiderationInput.TargetRisk or ConsiderationInput.TargetTravel;
}

/// <summary>
/// Намерение в каталоге (ТЗ §9.2): на какие affordances оно направлено, из чего складывается его
/// оценка, как долго длится и как меняет потребности.
/// </summary>
/// <param name="Targets">Типы affordances-целей; пусто — намерение без цели.</param>
/// <param name="TraitWeights">
/// Вес трейта: множитель <c>1 + w · (трейт − 50) / 50</c>, произведение по всем трейтам.
/// </param>
/// <param name="ArousalWeight">Модификатор настроения: <c>1 + a · (2·arousal − 1) + v · valence</c>.</param>
/// <param name="Cooldown">Сколько намерение недоступно после завершения или прерывания.</param>
public sealed record IntentDefinition(
    IntentKind Kind,
    IReadOnlyList<AffordanceKind> Targets,
    IReadOnlyList<Consideration> Considerations,
    IReadOnlyList<KeyValuePair<TraitKind, double>> TraitWeights,
    double ArousalWeight,
    double ValenceWeight,
    TimeSpan MinDuration,
    TimeSpan MaxDuration,
    TimeSpan Cooldown,
    NeedEffects Needs)
{
    public bool HasTarget => Targets.Count > 0;
}

/// <summary>
/// Данные Utility AI (ТЗ §9.2): каталог намерений и параметры выбора. Источник — JSON
/// (<see cref="BehaviorConfigJson"/>), по умолчанию — встроенный <c>behavior.json</c>.
/// </summary>
/// <param name="TopK">Из скольких лучших вариантов выбирать.</param>
/// <param name="Delta">Насколько вариант может отставать от лучшего, чтобы попасть в выбор: 0.1 — на 10 %.</param>
/// <param name="Momentum">Надбавка к оценке текущего намерения, пока оно не завершилось.</param>
/// <param name="TravelCostScale">Время пути, которое считается «далеко» (вход 1).</param>
public sealed record BehaviorConfig(
    int TopK,
    double Delta,
    double Momentum,
    TimeSpan TravelCostScale,
    IReadOnlyList<IntentDefinition> Intents)
{
    private static readonly Lazy<BehaviorConfig> DefaultConfig = new(BehaviorConfigJson.LoadDefault);

    /// <summary>Встроенная конфигурация — та, с которой голубь живёт без Developer Mode.</summary>
    public static BehaviorConfig Default => DefaultConfig.Value;

    /// <summary>Определение намерения или null, если в этой конфигурации его нет.</summary>
    public IntentDefinition? Find(IntentKind kind)
    {
        foreach (var intent in Intents)
        {
            if (intent.Kind == kind)
            {
                return intent;
            }
        }

        return null;
    }

    /// <summary>Все ошибки конфигурации сразу — чтобы правка в Developer Mode показала их одним списком.</summary>
    public IReadOnlyList<string> Problems()
    {
        var problems = new List<string>();

        if (TopK < 1)
        {
            problems.Add($"topK = {TopK}, нужно не меньше 1");
        }

        if (Delta is < 0.0 or >= 1.0 || double.IsNaN(Delta))
        {
            problems.Add($"delta = {Delta}, нужно 0 ≤ delta < 1");
        }

        if (Momentum is < 0.0 or > 2.0 || double.IsNaN(Momentum))
        {
            problems.Add($"momentum = {Momentum}, нужно 0..2");
        }

        if (TravelCostScale <= TimeSpan.Zero)
        {
            problems.Add("travelCostScale должен быть больше нуля");
        }

        if (Intents.Count == 0)
        {
            problems.Add("в каталоге нет намерений");
        }

        foreach (var group in Intents.GroupBy(i => i.Kind).Where(g => g.Count() > 1))
        {
            problems.Add($"{group.Key}: описано {group.Count()} раза");
        }

        foreach (var intent in Intents)
        {
            problems.AddRange(IntentProblems(intent).Select(p => $"{intent.Kind}: {p}"));
        }

        return problems;
    }

    private static IEnumerable<string> IntentProblems(IntentDefinition intent)
    {
        if (!Enum.IsDefined(intent.Kind))
        {
            yield return "неизвестное намерение";
        }

        if (intent.Considerations.Count == 0)
        {
            yield return "нет considerations";
        }

        for (var i = 0; i < intent.Considerations.Count; i++)
        {
            var consideration = intent.Considerations[i];
            if (consideration.NeedsTarget && !intent.HasTarget)
            {
                yield return $"consideration {i + 1} ({consideration.Input}) требует цели, а у намерения целей нет";
            }

            if (consideration.Input == ConsiderationInput.None && consideration.Curve.Kind != CurveKind.Constant)
            {
                yield return $"consideration {i + 1} без входа должна быть constant";
            }

            foreach (var problem in consideration.Curve.Problems())
            {
                yield return $"consideration {i + 1}: {problem}";
            }
        }

        if (intent.MinDuration <= TimeSpan.Zero || intent.MaxDuration < intent.MinDuration)
        {
            yield return $"длительность {intent.MinDuration}..{intent.MaxDuration} некорректна";
        }

        if (intent.Cooldown < TimeSpan.Zero)
        {
            yield return "отрицательный cooldown";
        }

        if (intent.TraitWeights.Any(w => w.Value is < -1.0 or > 1.0 || double.IsNaN(w.Value)))
        {
            yield return "вес трейта вне −1..1";
        }

        if (intent.ArousalWeight is < -1.0 or > 1.0 || intent.ValenceWeight is < -1.0 or > 1.0)
        {
            yield return "вес настроения вне −1..1";
        }

        if (!double.IsFinite(intent.Needs.Fatigue) || !double.IsFinite(intent.Needs.Boredom) || !double.IsFinite(intent.Needs.Attention))
        {
            yield return "множители потребностей должны быть конечными";
        }
    }
}
