using Pigeon.Common.Randomness;
using Pigeon.Common.Time;
using Pigeon.Core;
using Pigeon.Testing;

namespace Pigeon.Simulation.Tests;

/// <summary>Контракт детерминизма (ТЗ §3.3, ADR-0016).</summary>
public sealed class DeterminismTests
{
    private const ulong Seed = GoldenSimulation.Seed;

    private static readonly SimulationOptions Options = SimulationOptions.Desktop;

    /// <summary>Эталон одинаков на всех ОС и архитектурах CI и в WASM (см. <see cref="GoldenSimulation"/>).</summary>
    [Fact]
    public void GoldenRun_ProducesTheSameHashOnEveryPlatform()
    {
        Assert.Equal(GoldenSimulation.ExpectedHash, GoldenSimulation.Run());
    }

    /// <summary>Эталон проверяет то, что должен: цели в мире, сон и пропажу цели.</summary>
    [Fact]
    public void GoldenRun_CoversTargetedIntentsAndSleep()
    {
        var simulation = new PigeonSimulation(GoldenSimulation.Seed, Options);
        var journal = new Queue<JournalEntry>(GoldenSimulation.Journal());
        var intents = new HashSet<IntentKind>();
        var triggers = new HashSet<Pigeon.AI.DecisionTrigger>();

        for (long tick = 1; tick <= GoldenSimulation.Ticks; tick++)
        {
            while (journal.TryPeek(out var entry) && entry.Tick == tick)
            {
                simulation.Submit(journal.Dequeue().Event);
            }

            simulation.RunTicks(1);
            intents.Add(simulation.Pigeon.Intent);
            if (simulation.LastDecision is { } decision)
            {
                triggers.Add(decision.Trigger);
            }
        }

        Assert.Equal(GoldenSimulation.ExpectedHash, simulation.ComputeStateHash());
        Assert.Contains(intents, i => BehaviorConfigTargets(i));
        Assert.Contains(IntentKind.Rest, intents);
        Assert.Contains(Pigeon.AI.DecisionTrigger.Interrupted, triggers);
    }

    private static bool BehaviorConfigTargets(IntentKind intent) =>
        Pigeon.AI.Behavior.BehaviorConfig.Default.Find(intent)!.HasTarget;

    [Fact]
    public void SameTotalTime_GivesTheSameState_RegardlessOfFrameSlicing()
    {
        var total = TimeSpan.FromMinutes(1);
        var jitter = new Pcg32(11, 1);

        var at60Fps = Pump(total, () => TimeSpan.FromTicks(166_667));
        var at30Fps = Pump(total, () => TimeSpan.FromMilliseconds(33));
        var jittered = Pump(total, () => TimeSpan.FromMilliseconds(1 + jitter.NextInt32(80)));

        Assert.Equal(600, at60Fps.Tick);
        Assert.Equal(at60Fps.Tick, at30Fps.Tick);
        Assert.Equal(at60Fps.Tick, jittered.Tick);
        Assert.Equal(at60Fps.ComputeStateHash(), at30Fps.ComputeStateHash());
        Assert.Equal(at60Fps.ComputeStateHash(), jittered.ComputeStateHash());
    }

    [Fact]
    public void Replay_OfALiveSession_ReproducesStateAndJournal()
    {
        var live = new PigeonSimulation(Seed, Options);
        var frames = new Pcg32(21, 2);
        for (var frame = 0; frame < 20_000; frame++)
        {
            if (frame % 1_733 == 0)
            {
                live.Submit(new UserPoked());
            }

            live.Advance(TimeSpan.FromMilliseconds(5 + frames.NextInt32(40)));
        }

        var replayed = PigeonSimulation.Replay(Seed, Options, live.Journal, live.Tick);

        Assert.NotEmpty(live.Journal);
        Assert.Equal(live.Journal, replayed.Journal);
        Assert.Equal(live.ComputeStateHash(), replayed.ComputeStateHash());
    }

    [Fact]
    public void DifferentSeeds_GiveDifferentStates()
    {
        var a = PigeonSimulation.Replay(1, Options, [], 1_000);
        var b = PigeonSimulation.Replay(2, Options, [], 1_000);

        Assert.NotEqual(a.ComputeStateHash(), b.ComputeStateHash());
    }

    [Fact]
    public void Driver_FeedsClockTimeIntoTheSimulation()
    {
        var clock = new ManualClock();
        var simulation = new PigeonSimulation(Seed, Options);
        var driver = new SimulationDriver(clock, simulation);

        clock.Advance(TimeSpan.FromMilliseconds(250));
        var result = driver.Pump();

        Assert.Equal(2, result.TicksToRun);
        Assert.Equal(2, simulation.Tick);
    }

    [Fact]
    public void Pigeon_ChoosesIntentsOnItsOwnWithoutAWorld()
    {
        var simulation = new PigeonSimulation(Seed, Options);
        var intents = new HashSet<IntentKind>();

        // Два часа жизни без мира: остаются намерения без цели.
        for (var i = 0; i < 72_000; i++)
        {
            simulation.RunTicks(1);
            intents.Add(simulation.Pigeon.Intent);
        }

        Assert.Equal([IntentKind.Wander, IntentKind.Rest, IntentKind.Sleep, IntentKind.Groom], intents.Order());
    }

    [Fact]
    public void Poke_InterruptsRest()
    {
        var simulation = new PigeonSimulation(Seed, Options);
        while (simulation.Pigeon.Intent != IntentKind.Rest)
        {
            simulation.RunTicks(1);
        }

        simulation.Submit(new UserPoked());
        simulation.RunTicks(1);

        Assert.NotEqual(IntentKind.Rest, simulation.Pigeon.Intent);
        Assert.Equal(simulation.Tick, simulation.Journal[^1].Tick);
    }

    [Fact]
    public void Replay_RejectsUnorderedJournal()
    {
        JournalEntry[] journal = [new(10, new UserPoked()), new(5, new UserPoked())];

        Assert.Throws<ArgumentException>(() => PigeonSimulation.Replay(Seed, Options, journal, 20));
    }

    private static PigeonSimulation Pump(TimeSpan total, Func<TimeSpan> nextFrame)
    {
        var simulation = new PigeonSimulation(Seed, Options);
        var elapsed = TimeSpan.Zero;
        while (elapsed < total)
        {
            var frame = nextFrame();
            if (elapsed + frame > total)
            {
                frame = total - elapsed;
            }

            simulation.Advance(frame);
            elapsed += frame;
        }

        return simulation;
    }
}
