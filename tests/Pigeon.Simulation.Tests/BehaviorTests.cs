using Pigeon.AI;
using Pigeon.AI.Behavior;
using Pigeon.Core;
using Pigeon.World.Abstractions;

namespace Pigeon.Simulation.Tests;

/// <summary>Голубь сам выбирает намерения (ТЗ §9.2): характерность, устойчивость, мир в журнале.</summary>
public sealed class BehaviorTests
{
    private static readonly SimulationOptions Options = SimulationOptions.Desktop;
    private static readonly ulong[] Seeds = [1, 2];
    private const long Hours = 3;
    private const long TicksPerHour = 36_000;

    [Fact]
    public void CuriousPigeon_ExploresMuchMoreThanAnIncuriousOne()
    {
        var curious = Shares(Personality.Average with { Curiosity = 90 });
        var incurious = Shares(Personality.Average with { Curiosity = 10 });

        Assert.True(curious[IntentKind.Explore] > 0.15, Describe(curious));
        Assert.True(curious[IntentKind.Explore] > 5 * incurious[IntentKind.Explore], Describe(incurious));
    }

    [Fact]
    public void SocialPigeon_SeeksAttentionAndAnIndependentOneHardlyDoes()
    {
        var social = Shares(Personality.Average with { Sociability = 90, Independence = 15 });
        var independent = Shares(Personality.Average with { Sociability = 20, Independence = 90 });

        Assert.True(social[IntentKind.SeekAttention] > 0.05, Describe(social));
        Assert.True(social[IntentKind.SeekAttention] > 3 * independent[IntentKind.SeekAttention], Describe(independent));
    }

    [Fact]
    public void PlayfulPigeon_PlaysMoreAndRestsLessThanALazyOne()
    {
        var playful = Shares(Personality.Average with { Playfulness = 90, Energy = 80 });
        var lazy = Shares(Personality.Average with { Playfulness = 20, Energy = 10 });

        Assert.True(playful[IntentKind.Play] > 0.08, Describe(playful));
        Assert.True(playful[IntentKind.Play] > 3 * lazy[IntentKind.Play], Describe(lazy));
        Assert.True(lazy[IntentKind.Rest] > 1.4 * playful[IntentKind.Rest], Describe(lazy));
    }

    [Fact]
    public void AnyPigeon_LivesAVariedLifeAndSleepsEveryFewHours()
    {
        var shares = Shares(Personality.Average);

        Assert.True(shares.Count(s => s.Value > 0.02) >= 6, Describe(shares));
        Assert.True(shares[IntentKind.Sleep] > 0.03, Describe(shares));
        Assert.True(shares[IntentKind.Wander] < 0.5, Describe(shares));
    }

    [Fact]
    public void PeriodicReview_NeverCutsAnIntentShorterThanItsMinimum()
    {
        var simulation = Live(Personality.Average, 7);
        var config = BehaviorConfig.Default;
        long switches = 0;

        for (long t = 0; t < Hours * TicksPerHour; t++)
        {
            var intent = simulation.Pigeon.Intent;
            var started = simulation.Pigeon.IntentStartedTick;
            simulation.RunTicks(1);

            if (simulation.Pigeon.IntentStartedTick == started)
            {
                continue;
            }

            switches++;
            if (simulation.LastDecision!.Trigger == DecisionTrigger.Periodic)
            {
                var lasted = TimeSpan.FromTicks((simulation.Tick - started) * Options.Step.Ticks);
                Assert.True(lasted >= config.Find(intent)!.MinDuration, $"{intent} прервано плановым пересмотром через {lasted}.");
            }
        }

        Assert.InRange(switches / (double)Hours, 20, 120);
    }

    [Fact]
    public void World_IsJournaledOnlyWhenItChanges()
    {
        var world = new FakeWorld(FakeWorld.Desktop);
        var simulation = new PigeonSimulation(3, Options);
        simulation.AttachWorld(world);

        simulation.RunTicks(3_000);
        world.Items = [.. FakeWorld.Desktop.Take(2)];
        simulation.RunTicks(3_000);

        var observed = simulation.Journal.Where(e => e.Event is AffordancesObserved).ToList();
        Assert.Equal(2, observed.Count);
        Assert.Equal(1, observed[0].Tick);
        Assert.Equal(FakeWorld.Desktop.Take(2), simulation.Affordances);

        var replayed = PigeonSimulation.Replay(3, Options, simulation.Journal, simulation.Tick);
        Assert.Equal(simulation.ComputeStateHash(), replayed.ComputeStateHash());
    }

    [Fact]
    public void VanishedTarget_EndsTheIntentAtTheNextLook()
    {
        var world = new FakeWorld(FakeWorld.Desktop);
        var simulation = new PigeonSimulation(5, Options, Personality.Average with { Curiosity = 100 });
        simulation.AttachWorld(world);
        while (simulation.Pigeon.IntentTarget is null)
        {
            simulation.RunTicks(1);
        }

        var target = simulation.Pigeon.IntentTarget;
        var observations = simulation.Journal.Count(e => e.Event is AffordancesObserved);
        world.Items = [.. FakeWorld.Desktop.Where(a => a.Id != target)];
        simulation.RunTicks(Options.DecisionIntervalTicks);

        Assert.Equal(observations + 1, simulation.Journal.Count(e => e.Event is AffordancesObserved));
        Assert.NotEqual(target, simulation.Pigeon.IntentTarget);
        Assert.Equal(simulation.Journal[^1].Tick, simulation.Pigeon.IntentStartedTick);
        Assert.Equal(DecisionTrigger.TargetGone, simulation.LastDecision!.Trigger);
    }

    [Theory]
    [InlineData(IntentKind.Rest)]
    [InlineData(IntentKind.Sleep)]
    public void Poke_WakesThePigeonAndItDoesNotDozeOffAgainAtOnce(IntentKind dozing)
    {
        var simulation = new PigeonSimulation(11, Options);
        var guard = 0;
        while (simulation.Pigeon.Intent != dozing)
        {
            simulation.RunTicks(1);
            Assert.True(++guard < 20 * TicksPerHour, $"{dozing} не наступил за 20 часов.");
        }

        simulation.Submit(new UserPoked());
        simulation.RunTicks(1);

        Assert.NotEqual(dozing, simulation.Pigeon.Intent);
        Assert.Equal(DecisionTrigger.Interrupted, simulation.LastDecision!.Trigger);
        simulation.RunTicks(Options.DecisionIntervalTicks * 5);
        Assert.NotEqual(dozing, simulation.Pigeon.Intent);
    }

    [Fact]
    public void CustomConfig_ChangesBehavior()
    {
        const string onlyWander = """
            { "selection": { "topK": 1, "delta": 0, "momentum": 0, "travelCostScaleSeconds": 60 },
              "intents": { "wander": { "considerations": [ { "curve": "constant", "value": 0.5 } ], "durationSeconds": [5, 5] } } }
            """;
        var simulation = new PigeonSimulation(1, Options, behavior: BehaviorConfigJson.Parse(onlyWander));

        var intents = new HashSet<IntentKind>();
        for (var t = 0; t < TicksPerHour; t++)
        {
            simulation.RunTicks(1);
            intents.Add(simulation.Pigeon.Intent);
        }

        Assert.Equal([IntentKind.Wander], intents);
    }

    private static PigeonSimulation Live(Personality personality, ulong seed)
    {
        var simulation = new PigeonSimulation(seed, Options, personality);
        simulation.AttachWorld(new FakeWorld(FakeWorld.Desktop));
        return simulation;
    }

    /// <summary>Доля времени по намерениям за несколько часов жизни с двумя seed.</summary>
    private static Dictionary<IntentKind, double> Shares(Personality personality)
    {
        var ticks = Enum.GetValues<IntentKind>().ToDictionary(i => i, _ => 0L);
        foreach (var seed in Seeds)
        {
            var simulation = Live(personality, seed);
            for (long t = 0; t < Hours * TicksPerHour; t++)
            {
                simulation.RunTicks(1);
                ticks[simulation.Pigeon.Intent]++;
            }
        }

        var total = (double)ticks.Values.Sum();
        return ticks.ToDictionary(t => t.Key, t => t.Value / total);
    }

    private static string Describe(Dictionary<IntentKind, double> shares) =>
        string.Join(", ", shares.Where(s => s.Value > 0).Select(s => $"{s.Key} {s.Value:P0}"));

    /// <summary>Мир без геометрии (ТЗ §32, FakeWorldAdapter): только список affordances.</summary>
    private sealed class FakeWorld(IReadOnlyList<Affordance> items) : IAffordanceSource
    {
        public static readonly Affordance[] Desktop =
        [
            new(new AffordanceId(1), new PlaceKey("poi:window"), AffordanceKind.PointOfInterest, 0.6, 0.1, TimeSpan.FromSeconds(10)),
            new(new AffordanceId(2), new PlaceKey("perch:taskbar"), AffordanceKind.PerchSpot, 0.5, 0.0, TimeSpan.FromSeconds(5)),
            new(new AffordanceId(3), new PlaceKey("cursor"), AffordanceKind.AttentionTarget, 0.7, 0.2, TimeSpan.FromSeconds(8)),
            new(new AffordanceId(4), new PlaceKey("monitor:2"), AffordanceKind.TerritoryZone, 0.6, 0.1, TimeSpan.FromSeconds(30)),
        ];

        public IReadOnlyList<Affordance> Items { get; set; } = items;

        public IReadOnlyList<Affordance> Current() => Items;
    }
}
