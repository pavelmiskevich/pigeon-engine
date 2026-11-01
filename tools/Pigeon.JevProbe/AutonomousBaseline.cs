namespace Pigeon.JevProbe;

/// <summary>
/// Упрощённый Utility AI по формуле ТЗ §9.2: considerations × traitWeight × moodModifier, затем
/// взвешенный выбор из лучших K = 3 в пределах δ = 10 % от лучшего. Даёт распределение выбора
/// Autonomous для сравнения с Jev.
/// </summary>
internal static class AutonomousBaseline
{
    public const int TopK = 3;
    public const double Delta = 0.10;

    /// <summary>Насколько трейт может сдвинуть оценку: при k = 1 и трейте 100 — множитель 1.6.</summary>
    private const double TraitStrength = 0.6;

    private static readonly Dictionary<Intent, string?> RelatedNeed = new()
    {
        [Intent.Rest] = Needs.Fatigue,
        [Intent.Sleep] = Needs.Fatigue,
        [Intent.Wander] = Needs.Boredom,
        [Intent.Explore] = Needs.Boredom,
        [Intent.Observe] = Needs.Boredom,
        [Intent.Perch] = null,
        [Intent.SeekAttention] = Needs.Attention,
        [Intent.FollowTarget] = Needs.Attention,
        [Intent.Play] = Needs.Boredom,
        [Intent.Eat] = Needs.Hunger,
        [Intent.Groom] = null,
        [Intent.Poop] = Needs.ToiletPressure,
        [Intent.Mischief] = Needs.Boredom,
        [Intent.Hide] = null,
        [Intent.Flee] = null,
        [Intent.Relocate] = null,
        [Intent.LeaveOffscreen] = null,
    };

    private static readonly Dictionary<Intent, (string Trait, double K)[]> TraitWeights = new()
    {
        [Intent.Rest] = [(Traits.Energy, -0.6), (Traits.Calmness, 0.4)],
        [Intent.Sleep] = [(Traits.Energy, -0.8), (Traits.Calmness, 0.5)],
        [Intent.Wander] = [(Traits.Energy, 0.4), (Traits.Curiosity, 0.3)],
        [Intent.Explore] = [(Traits.Curiosity, 1.0), (Traits.Boldness, 0.6)],
        [Intent.Observe] = [(Traits.Curiosity, 0.5), (Traits.Calmness, 0.4), (Traits.Boldness, -0.3)],
        [Intent.Perch] = [(Traits.Territoriality, 0.5), (Traits.Calmness, 0.3)],
        [Intent.SeekAttention] = [(Traits.Sociability, 1.0), (Traits.Independence, -0.8)],
        [Intent.FollowTarget] = [(Traits.Playfulness, 0.7), (Traits.Sociability, 0.4)],
        [Intent.Play] = [(Traits.Playfulness, 1.0), (Traits.Energy, 0.5)],
        [Intent.Eat] = [(Traits.Greed, 1.0)],
        [Intent.Groom] = [(Traits.Calmness, 0.5)],
        [Intent.Poop] = [(Traits.Mischief, 0.3)],
        [Intent.Mischief] = [(Traits.Mischief, 1.0), (Traits.Playfulness, 0.4)],
        [Intent.Hide] = [(Traits.Boldness, -1.0), (Traits.Calmness, -0.5)],
        [Intent.Flee] = [(Traits.Boldness, -1.0), (Traits.Calmness, -0.6)],
        [Intent.Relocate] = [(Traits.Independence, 0.4), (Traits.Curiosity, 0.3), (Traits.Territoriality, -0.5)],
        [Intent.LeaveOffscreen] = [(Traits.Independence, 0.8), (Traits.Sociability, -0.4)],
    };

    private static readonly Dictionary<string, Dictionary<Intent, double>> MoodModifiers = new(StringComparer.Ordinal)
    {
        ["Content"] = new() { [Intent.Groom] = 1.2, [Intent.Perch] = 1.2, [Intent.Observe] = 1.2 },
        ["Happy"] = new() { [Intent.SeekAttention] = 1.2, [Intent.Play] = 1.2 },
        ["Playful"] = new() { [Intent.Play] = 1.4, [Intent.FollowTarget] = 1.4, [Intent.Mischief] = 1.4 },
        ["Excited"] = new() { [Intent.Explore] = 1.3, [Intent.Play] = 1.3, [Intent.Rest] = 0.7 },
        ["Bored"] = new() { [Intent.Explore] = 1.4, [Intent.Play] = 1.4, [Intent.Mischief] = 1.4, [Intent.Rest] = 0.8 },
        ["Grumpy"] = new() { [Intent.Mischief] = 1.3, [Intent.LeaveOffscreen] = 1.3, [Intent.SeekAttention] = 0.7 },
        ["Startled"] = new() { [Intent.Flee] = 1.8, [Intent.Hide] = 1.8, [Intent.Play] = 0.5 },
        ["Sleepy"] = new() { [Intent.Rest] = 1.5, [Intent.Sleep] = 1.5, [Intent.Play] = 0.6, [Intent.Explore] = 0.6 },
    };

    public static double Score(Scenario scenario, IntentOption option)
    {
        var need = RelatedNeed[option.Intent];
        var needConsideration = need is null ? 0.5 : 0.2 + 0.8 * scenario.Situation.Needs[need];
        var considerations = Math.Sqrt(needConsideration * option.Attractiveness);

        var traitWeight = 1.0;
        foreach (var (trait, k) in TraitWeights[option.Intent])
        {
            var centered = (scenario.Archetype.Traits[trait] - 50) / 50.0;
            traitWeight *= Math.Max(0.1, 1.0 + TraitStrength * k * centered);
        }

        var mood = MoodModifiers.TryGetValue(scenario.Situation.Mood, out var table)
            && table.TryGetValue(option.Intent, out var modifier)
            ? modifier
            : 1.0;

        return considerations * traitWeight * mood;
    }

    public static IReadOnlyDictionary<string, double> Scores(Scenario scenario) =>
        scenario.Situation.Options.ToDictionary(o => o.Key, o => Score(scenario, o), StringComparer.Ordinal);

    /// <summary>Вероятности выбора: лучшие K в пределах δ от лучшего, пропорционально оценке.</summary>
    public static IReadOnlyDictionary<string, double> Distribution(Scenario scenario)
    {
        var scores = Scores(scenario);
        var best = scores.Values.Max();
        var pool = scores
            .Where(s => s.Value >= best * (1 - Delta))
            .OrderByDescending(s => s.Value)
            .ThenBy(s => s.Key, StringComparer.Ordinal)
            .Take(TopK)
            .ToList();
        var total = pool.Sum(s => s.Value);

        return scores.ToDictionary(
            s => s.Key,
            s => pool.Any(p => p.Key == s.Key) ? s.Value / total : 0.0,
            StringComparer.Ordinal);
    }
}
