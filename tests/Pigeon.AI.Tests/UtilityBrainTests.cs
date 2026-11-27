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
        var options = Brain.Score(Pigeon(), [], new IntentCooldowns(), 0, 0, withMomentum: false);

        Assert.Equal([IntentKind.Wander, IntentKind.Rest, IntentKind.Sleep, IntentKind.Groom], options.Select(o => o.Intent));
        Assert.All(options, o => Assert.Null(o.Target));
    }

    [Fact]
    public void Affordances_AddTargetedCandidatesForMatchingKindsOnly()
    {
        var options = Brain.Score(Pigeon(), [Window, Cursor, OtherMonitor], new IntentCooldowns(), 0, 0, withMomentum: false);

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

        Assert.DoesNotContain(Brain.Score(Pigeon(), [], cooldowns, 99, 0, false), o => o.Intent == IntentKind.Groom);
        Assert.Contains(Brain.Score(Pigeon(), [], cooldowns, 100, 0, false), o => o.Intent == IntentKind.Groom);
    }

    [Fact]
    public void TiredPigeon_RanksRestAboveWandering()
    {
        var tired = Pigeon(needs: new Needs(0.85, 0.1, 0.1));

        var options = Brain.Score(tired, [], new IntentCooldowns(), 0, 0, false);

        Assert.True(Score(options, IntentKind.Rest) > 2 * Score(options, IntentKind.Wander));
    }

    [Fact]
    public void Traits_ChangeTheScoreOfMatchingIntents()
    {
        var bored = new Needs(0.2, 0.5, 0.5);
        var curious = Brain.Score(Pigeon(Personality.Average with { Curiosity = 100 }, bored), [Window], new IntentCooldowns(), 0, 0, false);
        var incurious = Brain.Score(Pigeon(Personality.Average with { Curiosity = 0 }, bored), [Window], new IntentCooldowns(), 0, 0, false);
        var social = Brain.Score(Pigeon(Personality.Average with { Sociability = 100 }, bored), [Cursor], new IntentCooldowns(), 0, 0, false);
        var aloof = Brain.Score(Pigeon(Personality.Average with { Independence = 100 }, bored), [Cursor], new IntentCooldowns(), 0, 0, false);

        Assert.Equal(1.6 / 0.4, Factor(curious, IntentKind.Explore, UtilityBrain.PersonalityFactor) / Factor(incurious, IntentKind.Explore, UtilityBrain.PersonalityFactor), 1e-9);
        Assert.True(Score(curious, IntentKind.Explore) > 4 * Score(incurious, IntentKind.Explore), "незнакомое окно тянет любопытного ещё и новизной");
        Assert.True(Score(social, IntentKind.SeekAttention) > 3 * Score(aloof, IntentKind.SeekAttention));
    }

    [Fact]
    public void BoldPigeon_SeesLessRisk()
    {
        var risky = Window with { Risk = 0.8 };
        var needs = new Needs(0.2, 0.6, 0.1);

        var bold = Brain.Score(Pigeon(Personality.Average with { Boldness = 100 }, needs), [risky], new IntentCooldowns(), 0, 0, false);
        var timid = Brain.Score(Pigeon(Personality.Average with { Boldness = 0 }, needs), [risky], new IntentCooldowns(), 0, 0, false);

        Assert.Equal(0.64, Factor(bold, IntentKind.Explore, "target.risk"), 1e-9);
        Assert.Equal(0.1, Factor(timid, IntentKind.Explore, "target.risk"), 1e-9);
    }

    [Fact]
    public void Pool_HoldsAtMostTopKWithinDeltaOfTheBest()
    {
        var pigeon = Pigeon(needs: new Needs(0.5, 0.5, 0.5));
        var affordances = new[] { Window, Cursor, OtherMonitor };
        var best = Brain.Score(pigeon, affordances, new IntentCooldowns(), 0, 0, false).Max(o => o.Score);

        var decision = Brain.Decide(pigeon, affordances, new IntentCooldowns(), 0, 0, DecisionTrigger.Completed, new Pcg32(1, 1));

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
        pigeon.StartIntent(IntentKind.Explore, Window.Id, Window.PlaceKey, 0, 1_000);

        for (ulong seed = 0; seed < 200; seed++)
        {
            var decision = Brain.Decide(pigeon, [Window, twin], new IntentCooldowns(), 10, 0, DecisionTrigger.Periodic, new Pcg32(seed, 7));

            Assert.True(decision.KeptCurrent);
            Assert.Equal(Window.Id, decision.Choice.Target);
        }
    }

    [Fact]
    public void CompletedIntent_IsChosenAfreshAmongCloseOptions()
    {
        var twin = Window with { Id = new AffordanceId(9), PlaceKey = new PlaceKey("poi:window-2") };
        var pigeon = Pigeon(needs: new Needs(0.2, 0.8, 0.1));
        pigeon.StartIntent(IntentKind.Explore, Window.Id, Window.PlaceKey, 0, 10);

        var targets = Enumerable.Range(0, 200)
            .Select(seed => Brain.Decide(pigeon, [Window, twin], new IntentCooldowns(), 10, 0, DecisionTrigger.Completed, new Pcg32((ulong)seed, 7)))
            .Where(d => d.Choice.Intent == IntentKind.Explore)
            .Select(d => d.Choice.Target)
            .ToHashSet();

        Assert.Equal([Window.Id.Value, twin.Id.Value], targets.Select(t => t!.Value.Value).Order());
    }

    [Fact]
    public void Decision_ExplainsItselfWithLeadingFactors()
    {
        var bored = Pigeon(Personality.Average with { Curiosity = 90 }, new Needs(0.1, 0.9, 0.1));

        var explore = Brain.Score(bored, [Window], new IntentCooldowns(), 0, 0, false).Single(o => o.Intent == IntentKind.Explore);
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
        pigeon.StartIntent(IntentKind.Wander, null, null, 0, 1_000);
        var random = new Pcg32(5, 5);
        var before = random.State;

        var decision = Brain.Decide(pigeon, [], new IntentCooldowns(), 30, 0, DecisionTrigger.Periodic, random);

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

    /// <summary>Реакция на курсор рядом (ТЗ §8.2) — по личности и настроению.</summary>
    public static TheoryData<string, Personality, Needs, Mood, double, IntentKind> CursorReactions() => new()
    {
        { "робкий и раздражённый", Personality.Average with { Boldness = 15, Calmness = 25 }, new Needs(0.2, 0.3, 0.2), new Mood(-0.3, 0.7, 0.1), 0.35, IntentKind.Flee },
        { "резкий курсор", Personality.Average, new Needs(0.2, 0.3, 0.2), new Mood(0.1, 0.55, 0.0), 0.6, IntentKind.Flee },
        { "игривый и смелый", Personality.Average with { Playfulness = 85, Boldness = 75, Energy = 70 }, new Needs(0.2, 0.4, 0.2), new Mood(0.3, 0.55, 0.0), 0.2, IntentKind.Play },
        { "спокойный и любопытный", Personality.Average with { Calmness = 80, Curiosity = 80 }, new Needs(0.2, 0.4, 0.2), new Mood(0.1, 0.35, 0.0), 0.2, IntentKind.Observe },
        { "соскучился", Personality.Average with { Sociability = 85, Independence = 20 }, new Needs(0.2, 0.2, 0.8), new Mood(0.0, 0.5, 0.0), 0.2, IntentKind.SeekAttention },
    };

    [Theory]
    [MemberData(nameof(CursorReactions))]
    public void CursorNearby_ReactionFollowsPersonalityAndMood(string who, Personality traits, Needs needs, Mood mood, double risk, IntentKind expected)
    {
        var pigeon = new PigeonState(traits, mood);
        pigeon.SetNeeds(needs);
        var cursor = Cursor with { Attractiveness = 0.7, Risk = risk, TravelCost = TimeSpan.FromSeconds(3) };

        var best = Brain.Score(pigeon, [cursor], new IntentCooldowns(), 0, 0, withMomentum: false).MaxBy(o => o.Score)!;

        Assert.True(best.Intent == expected, $"{who}: {best.Intent}, ожидалось {expected}");
        Assert.Equal(cursor.Id, best.Target);
    }

    [Fact]
    public void Novelty_PullsTheCuriousToTheUnknownTerritory()
    {
        var curious = Familiar(Personality.Average with { Curiosity = 100, Boldness = 100 });

        var options = Brain.Score(curious, [KnownMonitor, NewMonitor], new IntentCooldowns(), 0, 0, false);

        var known = Option(options, IntentKind.Relocate, KnownMonitor);
        var fresh = Option(options, IntentKind.Relocate, NewMonitor);
        Assert.True(fresh.Score > known.Score);
        Assert.Equal(1.25 / (1.0 + 0.25 * (2.0 * 0.1 - 1.0)), fresh.Score / known.Score, 1e-12);
        Assert.Equal(1.25, fresh.Factors.Single(f => f.Key == UtilityBrain.NoveltyFactor).Value);
        Assert.Contains(fresh.LeadingFactors(10), f => f.Key == UtilityBrain.NoveltyFactor);
        Assert.DoesNotContain(known.LeadingFactors(10), f => f.Key == UtilityBrain.NoveltyFactor);
    }

    [Fact]
    public void Novelty_KeepsTheTimidAtHome()
    {
        var timid = Familiar(Personality.Average with { Curiosity = 0, Boldness = 0 });

        var options = Brain.Score(timid, [KnownMonitor, NewMonitor], new IntentCooldowns(), 0, 0, false);

        Assert.True(Option(options, IntentKind.Relocate, KnownMonitor).Score > Option(options, IntentKind.Relocate, NewMonitor).Score);
    }

    [Fact]
    public void Novelty_DoesNotTouchTheAveragePigeon()
    {
        var average = Familiar(Personality.Average);

        var options = Brain.Score(average, [KnownMonitor, NewMonitor], new IntentCooldowns(), 0, 0, false);

        var known = Option(options, IntentKind.Relocate, KnownMonitor);
        var fresh = Option(options, IntentKind.Relocate, NewMonitor);
        Assert.Equal(known.Score, fresh.Score);
        Assert.Equal(1.0, known.Factors.Single(f => f.Key == UtilityBrain.NoveltyFactor).Value);
        Assert.Equal(1.0, fresh.Factors.Single(f => f.Key == UtilityBrain.NoveltyFactor).Value);
    }

    [Fact]
    public void Novelty_FadesWithForgetting()
    {
        var curious = Familiar(Personality.Average with { Curiosity = 100, Boldness = 100 });

        var monthLater = Brain.Score(curious, [KnownMonitor, NewMonitor], new IntentCooldowns(), 0, 30 * 86_400, false);

        Assert.Equal(
            Option(monthLater, IntentKind.Relocate, NewMonitor).Score,
            Option(monthLater, IntentKind.Relocate, KnownMonitor).Score,
            1e-9);
    }

    [Fact]
    public void IntentsWithoutNoveltyWeight_HaveNoNoveltyFactor()
    {
        var options = Brain.Score(Familiar(Personality.Average with { Curiosity = 100 }), [Window, Cursor, KnownMonitor], new IntentCooldowns(), 0, 0, false);

        Assert.All(options.Where(o => o.Intent is not (IntentKind.Explore or IntentKind.Relocate)),
            o => Assert.DoesNotContain(o.Factors, f => f.Key == UtilityBrain.NoveltyFactor));
        Assert.Contains(Option(options, IntentKind.Explore, Window).Factors, f => f.Key == UtilityBrain.NoveltyFactor);
    }

    [Theory]
    [InlineData(0.9, 0, 0.975)]   // дом знаком так же, как цель: робкого почти не тянет на чужой монитор
    [InlineData(0.1, 0, 1.175)]   // на новом месте робкого тянет обратно в знакомое
    [InlineData(0.1, 100, 0.825)] // а любопытного — нет
    [InlineData(0.9, 100, 1.025)]
    public void NoveltyOfAnotherTerritory_IsRelativeToHome(double home, int traits, double expected)
    {
        var pigeon = PigeonState.FromSnapshot(Pigeon(Personality.Average with { Curiosity = traits, Boldness = traits }, new Needs(0.2, 0.6, 0.1)).ToSnapshot() with
        {
            Memory = [new PlaceMemoryEntry(HomeMonitor, home, 0, 1, true), new PlaceMemoryEntry(KnownMonitor.PlaceKey, 0.9, 0, 1, true)],
        });

        var options = Brain.Score(pigeon, [HomePerch, KnownMonitor], new IntentCooldowns(), 0, 0, false);

        Assert.Equal(expected, Option(options, IntentKind.Relocate, KnownMonitor).Factors.Single(f => f.Key == UtilityBrain.NoveltyFactor).Value, 1e-12);
    }

    [Theory]
    [InlineData(0, 1.0, 0.7)]
    [InlineData(0, 0.5, 0.85)]
    [InlineData(50, 1.0, 1.0)]
    [InlineData(100, 1.0, 1.0)]
    public void HomeAttachment_HoldsTheTimidAtAFamiliarHome(int traits, double home, double expected)
    {
        var pigeon = PigeonState.FromSnapshot(Pigeon(Personality.Average with { Curiosity = traits, Boldness = traits }, new Needs(0.2, 0.6, 0.1)).ToSnapshot() with
        {
            Memory = [new PlaceMemoryEntry(HomeMonitor, home, 0, 1, true)],
        });

        var options = Brain.Score(pigeon, [HomePerch, NewMonitor], new IntentCooldowns(), 0, 0, false);

        Assert.Equal(expected, Option(options, IntentKind.Relocate, NewMonitor).Factors.Single(f => f.Key == UtilityBrain.HomeFactor).Value, 1e-12);
    }

    [Fact]
    public void TheTimid_CarriedToAStrangeMonitor_IsDrawnHome()
    {
        // Человек перенёс робкого с обжитого монитора на чужой: «дом» под ним незнаком, а свой
        // монитор — знакомая цель, к которой робкого тянет.
        var pigeon = PigeonState.FromSnapshot(Pigeon(Personality.Average with { Curiosity = 0, Boldness = 0 }, new Needs(0.2, 0.6, 0.1)).ToSnapshot() with
        {
            Memory = [new PlaceMemoryEntry(KnownMonitor.PlaceKey, 0.9, 0, 1, true)],
        });

        var factors = Option(Brain.Score(pigeon, [HomePerch, KnownMonitor], new IntentCooldowns(), 0, 0, false), IntentKind.Relocate, KnownMonitor).Factors;

        Assert.Equal(1.0, factors.Single(f => f.Key == UtilityBrain.HomeFactor).Value);
        Assert.Equal(1.2, factors.Single(f => f.Key == UtilityBrain.NoveltyFactor).Value, 1e-12);
    }

    [Fact]
    public void HomeAttachment_NeedsToKnowWhereThePigeonSits()
    {
        var options = Brain.Score(Pigeon(Personality.Average with { Curiosity = 0, Boldness = 0 }), [NewMonitor], new IntentCooldowns(), 0, 0, false);

        Assert.DoesNotContain(Option(options, IntentKind.Relocate, NewMonitor).Factors, f => f.Key == UtilityBrain.HomeFactor);
    }

    private static readonly PlaceKey HomeMonitor = new("desktop/home");

    private static readonly Affordance HomePerch = new(
        new AffordanceId(5), new PlaceKey("desktop/home/taskbar"), AffordanceKind.PerchSpot, 0.5, 0.0, TimeSpan.FromSeconds(5));

    private static readonly Affordance KnownMonitor = OtherMonitor with { PlaceKey = new PlaceKey("desktop/known") };

    private static readonly Affordance NewMonitor = OtherMonitor with { Id = new AffordanceId(4), PlaceKey = new PlaceKey("desktop/new") };

    /// <summary>Скучающий голубь, который хорошо знает территорию <see cref="KnownMonitor"/> (знакомство 0,9).</summary>
    private static PigeonState Familiar(Personality personality)
    {
        var snapshot = Pigeon(personality, new Needs(0.2, 0.6, 0.1)).ToSnapshot() with
        {
            Memory = [new PlaceMemoryEntry(KnownMonitor.PlaceKey, 0.9, 0, 1, true)],
        };
        return PigeonState.FromSnapshot(snapshot);
    }

    private static ScoredOption Option(IReadOnlyList<ScoredOption> options, IntentKind intent, Affordance target) =>
        options.Single(o => o.Intent == intent && o.Target == target.Id);

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
