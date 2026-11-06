using Pigeon.AI.Behavior;
using Pigeon.Common.Randomness;
using Pigeon.Core;
using Pigeon.World.Abstractions;

namespace Pigeon.AI.Tests;

/// <summary>Оценка и выбор намерений (ТЗ §9.2).</summary>
public sealed class UtilityBrainTests
{
    private static readonly UtilityBrain Brain = new(BehaviorConfig.Default);

    private static readonly Affordance Window = new(
        new AffordanceId(1), new PlaceKey("poi:window"), AffordanceKind.PointOfInterest, 0.6, 0.1, TimeSpan.FromSeconds(10));

    private static readonly Affordance Cursor = new(
        new AffordanceId(2), new PlaceKey("cursor"), AffordanceKind.AttentionTarget, 0.7, 0.2, TimeSpan.FromSeconds(8));

    private static readonly Affordance OtherMonitor = new(
        new AffordanceId(3), new PlaceKey("monitor:2"), AffordanceKind.TerritoryZone, 0.6, 0.1, TimeSpan.FromSeconds(30));

    [Fact]
    public void WithoutAWorld_OnlyIntentsWithoutATargetAreCandidates()
    {
        var options = Brain.Score(Pigeon(), [], new IntentCooldowns(), 0, withMomentum: false);

        Assert.Equal([IntentKind.Wander, IntentKind.Rest, IntentKind.Sleep, IntentKind.Groom], options.Select(o => o.Intent));
        Assert.All(options, o => Assert.Null(o.Target));
    }

    [Fact]
    public void Affordances_AddTargetedCandidatesForMatchingKindsOnly()
    {
        var options = Brain.Score(Pigeon(), [Window, Cursor, OtherMonitor], new IntentCooldowns(), 0, withMomentum: false);

        Assert.Contains(options, o => o.Intent == IntentKind.Explore && o.Target == Window.Id);
        Assert.Contains(options, o => o.Intent == IntentKind.Observe && o.Target == Cursor.Id);
        Assert.Contains(options, o => o.Intent == IntentKind.Relocate && o.Target == OtherMonitor.Id);
        Assert.DoesNotContain(options, o => o.Intent == IntentKind.SeekAttention && o.Target != Cursor.Id);
        Assert.DoesNotContain(options, o => o.Intent == IntentKind.Explore && o.Target == Cursor.Id);
    }

    [Fact]
    public void CoolingDownIntent_IsNotACandidate()
    {
        var cooldowns = new IntentCooldowns();
        cooldowns.Start(IntentKind.Groom, 100);

        Assert.DoesNotContain(Brain.Score(Pigeon(), [], cooldowns, 99, false), o => o.Intent == IntentKind.Groom);
        Assert.Contains(Brain.Score(Pigeon(), [], cooldowns, 100, false), o => o.Intent == IntentKind.Groom);
    }

    [Fact]
    public void TiredPigeon_RanksRestAboveWandering()
    {
        var tired = Pigeon(needs: new Needs(0.85, 0.1, 0.1));

        var options = Brain.Score(tired, [], new IntentCooldowns(), 0, false);

        Assert.True(Score(options, IntentKind.Rest) > 2 * Score(options, IntentKind.Wander));
    }

    [Fact]
    public void Traits_ChangeTheScoreOfMatchingIntents()
    {
        var bored = new Needs(0.2, 0.5, 0.5);
        var curious = Brain.Score(Pigeon(Personality.Average with { Curiosity = 100 }, bored), [Window], new IntentCooldowns(), 0, false);
        var incurious = Brain.Score(Pigeon(Personality.Average with { Curiosity = 0 }, bored), [Window], new IntentCooldowns(), 0, false);
        var social = Brain.Score(Pigeon(Personality.Average with { Sociability = 100 }, bored), [Cursor], new IntentCooldowns(), 0, false);
        var aloof = Brain.Score(Pigeon(Personality.Average with { Independence = 100 }, bored), [Cursor], new IntentCooldowns(), 0, false);

        Assert.Equal(1.6 / 0.4, Score(curious, IntentKind.Explore) / Score(incurious, IntentKind.Explore), 1e-9);
        Assert.True(Score(social, IntentKind.SeekAttention) > 3 * Score(aloof, IntentKind.SeekAttention));
    }

    [Fact]
    public void BoldPigeon_SeesLessRisk()
    {
        var risky = Window with { Risk = 0.8 };
        var needs = new Needs(0.2, 0.6, 0.1);

        var bold = Brain.Score(Pigeon(Personality.Average with { Boldness = 100 }, needs), [risky], new IntentCooldowns(), 0, false);
        var timid = Brain.Score(Pigeon(Personality.Average with { Boldness = 0 }, needs), [risky], new IntentCooldowns(), 0, false);

        Assert.Equal(0.64, Factor(bold, IntentKind.Explore, "target.risk"), 1e-9);
        Assert.Equal(0.1, Factor(timid, IntentKind.Explore, "target.risk"), 1e-9);
    }

    [Fact]
    public void Pool_HoldsAtMostTopKWithinDeltaOfTheBest()
    {
        var pigeon = Pigeon(needs: new Needs(0.5, 0.5, 0.5));
        var affordances = new[] { Window, Cursor, OtherMonitor };
        var best = Brain.Score(pigeon, affordances, new IntentCooldowns(), 0, false).Max(o => o.Score);

        var decision = Brain.Decide(pigeon, affordances, new IntentCooldowns(), 0, DecisionTrigger.Completed, new Pcg32(1, 1));

        Assert.InRange(decision.Pool.Count, 1, BehaviorConfig.Default.TopK);
        Assert.All(decision.Pool, o => Assert.True(o.Score >= best * (1 - BehaviorConfig.Default.Delta)));
        Assert.Contains(decision.Choice, decision.Pool);
    }

    [Fact]
    public void PeriodicReview_KeepsTheCurrentIntentWhileItIsAmongTheBest()
    {
        // Два одинаковых окна: без гистерезиса голубь метался бы между ними.
        var twin = Window with { Id = new AffordanceId(9), PlaceKey = new PlaceKey("poi:window-2") };
        var pigeon = Pigeon(needs: new Needs(0.2, 0.6, 0.1));
        pigeon.StartIntent(IntentKind.Explore, Window.Id, 0, 1_000);

        for (ulong seed = 0; seed < 200; seed++)
        {
            var decision = Brain.Decide(pigeon, [Window, twin], new IntentCooldowns(), 10, DecisionTrigger.Periodic, new Pcg32(seed, 7));

            Assert.True(decision.KeptCurrent);
            Assert.Equal(Window.Id, decision.Choice.Target);
        }
    }

    [Fact]
    public void CompletedIntent_IsChosenAfreshAmongCloseOptions()
    {
        var twin = Window with { Id = new AffordanceId(9), PlaceKey = new PlaceKey("poi:window-2") };
        var pigeon = Pigeon(needs: new Needs(0.2, 0.8, 0.1));
        pigeon.StartIntent(IntentKind.Explore, Window.Id, 0, 10);

        var targets = Enumerable.Range(0, 200)
            .Select(seed => Brain.Decide(pigeon, [Window, twin], new IntentCooldowns(), 10, DecisionTrigger.Completed, new Pcg32((ulong)seed, 7)))
            .Where(d => d.Choice.Intent == IntentKind.Explore)
            .Select(d => d.Choice.Target)
            .ToHashSet();

        Assert.Equal([Window.Id.Value, twin.Id.Value], targets.Select(t => t!.Value.Value).Order());
    }

    [Fact]
    public void Decision_ExplainsItselfWithLeadingFactors()
    {
        var bored = Pigeon(Personality.Average with { Curiosity = 90 }, new Needs(0.1, 0.9, 0.1));

        var explore = Brain.Score(bored, [Window], new IntentCooldowns(), 0, false).Single(o => o.Intent == IntentKind.Explore);
        var leading = explore.LeadingFactors();

        Assert.InRange(leading.Count, 2, 3);
        Assert.Contains(leading, f => f.Key == "need.boredom");
        Assert.Contains(explore.Factors, f => f.Key == UtilityBrain.PersonalityFactor && f.Value > 1.4);
        Assert.DoesNotContain(leading, f => f.Key == UtilityBrain.ConstantFactor);
    }

    [Fact]
    public void Decide_UsesRandomnessOnlyWhenItHasToChoose()
    {
        var pigeon = Pigeon();
        pigeon.StartIntent(IntentKind.Wander, null, 0, 1_000);
        var random = new Pcg32(5, 5);
        var before = random.State;

        var decision = Brain.Decide(pigeon, [], new IntentCooldowns(), 30, DecisionTrigger.Periodic, random);

        Assert.True(decision.KeptCurrent);
        Assert.Equal(before, random.State);
    }

    [Fact]
    public void PlanDuration_StaysWithinTheConfiguredRange()
    {
        var random = new Pcg32(3, 3);
        var sleep = BehaviorConfig.Default.Find(IntentKind.Sleep)!;

        for (var i = 0; i < 100; i++)
        {
            Assert.InRange(Brain.PlanDuration(IntentKind.Sleep, random), sleep.MinDuration, sleep.MaxDuration);
        }
    }

    private static PigeonState Pigeon(Personality? personality = null, Needs? needs = null)
    {
        var traits = personality ?? Personality.Average;
        var pigeon = new PigeonState(traits, MoodModel.Default.Baseline(traits));
        pigeon.SetNeeds(needs ?? new Needs(0.2, 0.2, 0.2));
        return pigeon;
    }

    private static double Score(IReadOnlyList<ScoredOption> options, IntentKind intent) =>
        options.Where(o => o.Intent == intent).Max(o => o.Score);

    private static double Factor(IReadOnlyList<ScoredOption> options, IntentKind intent, string key) =>
        options.First(o => o.Intent == intent).Factors.Single(f => f.Key == key).Value;
}
